"""Shared feature contract for the MikPunt package-price models.

The SAME feature logic is re-implemented in C# (src/WebAPI/WebAPI/Services/Ai/FeatureEncoder.cs).
Both sides are pinned together by golden vectors (see train_price_model.py -> golden_vectors.json and
WebAPI.Tests/AiFeatureParityTests.cs), so change one only together with the other.

All money is USD (the currency the international market publishes in). The API converts to/from ZAR.
"""
from __future__ import annotations

import json
import math
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent
DATA = ROOT / "data"

PROVINCES = [
    "limpopo", "gauteng", "mpumalanga", "north_west", "free_state",
    "northern_cape", "eastern_cape", "western_cape", "kwazulu_natal",
]

_PROVINCE_ALIASES = {
    "limpopo": "limpopo", "gauteng": "gauteng", "mpumalanga": "mpumalanga",
    "north west": "north_west", "northwest": "north_west", "noordwes": "north_west", "noord-wes": "north_west", "nw": "north_west",
    "free state": "free_state", "vrystaat": "free_state", "fs": "free_state",
    "northern cape": "northern_cape", "noord-kaap": "northern_cape", "noordkaap": "northern_cape", "nc": "northern_cape",
    "eastern cape": "eastern_cape", "oos-kaap": "eastern_cape", "ooskaap": "eastern_cape", "ec": "eastern_cape",
    "western cape": "western_cape", "wes-kaap": "western_cape", "weskaap": "western_cape", "wc": "western_cape",
    "kwazulu-natal": "kwazulu_natal", "kwazulu natal": "kwazulu_natal", "kzn": "kwazulu_natal", "natal": "kwazulu_natal",
}

KINDS = ["PJ_PACKAGE", "PH_RATE", "VG_PACKAGE", "FARM_DAILY_RATE", "SPECIES_FEE"]

# Which kinds rest ONLY on unsourced assumptions (no published market data) - the API reports low confidence for these.
ASSUMED_ONLY_KINDS = ["PH_RATE"]

_COMMON = [f"prov_{p}" for p in PROVINCES] + ["month_sin", "month_cos", "peak_season", "market_domestic"]

FEATURES = {
    "PJ_PACKAGE": [
        "duration_days", "hunters_per_ph", "svc_ph_included", "svc_accommodation", "svc_meals", "svc_field_prep",
        "svc_airport_transfer", "svc_videography", "svc_drone", "included_fee_log", "n_included", "has_dangerous",
    ] + _COMMON,
    "PH_RATE": [
        "years_experience", "has_rating", "rating_avg", "rating_n_log", "hunts_log", "is_dg_rate", "dg_accredited",
    ] + _COMMON,
    "VG_PACKAGE": [
        "days_covered", "drone", "tier_idx", "crew_size", "uhd_4k", "years_experience",
    ] + _COMMON,
    "FARM_DAILY_RATE": [
        "hunters_per_ph", "farm_size_log", "lodge_rooms", "catering", "cold_room", "guide_included", "pool", "wifi",
        "n_species", "game_fenced",
    ] + _COMMON,
    "SPECIES_FEE": [
        "species_ref_log", "is_dangerous", "tier_female", "tier_male", "tier_trophy", "farm_size_log", "game_fenced",
    ] + _COMMON,
}

TIERS = {"basic": 0, "standard": 1, "documentary": 2}


def load_catalog():
    doc = json.loads((DATA / "species_catalog.json").read_text(encoding="utf-8"))
    by_alias = {}
    for sp in doc["species"]:
        by_alias[sp["id"]] = sp
        by_alias[sp["af"].lower()] = sp
        by_alias[sp["en"].lower()] = sp
        for a in sp.get("aliases", []):
            by_alias[a.lower()] = sp
    return doc["species"], by_alias


SPECIES, _BY_ALIAS = load_catalog()


def resolve_species(name):
    """Returns the catalogue entry for a free-text species name, or None when unknown."""
    if not name:
        return None
    key = re.sub(r"\s+", " ", str(name).strip().lower())
    return _BY_ALIAS.get(key)


def normalize_province(value):
    if not value:
        return "limpopo"
    key = re.sub(r"\s+", " ", str(value).strip().lower().replace("_", " "))
    if key.replace(" ", "_") in PROVINCES:
        return key.replace(" ", "_")
    return _PROVINCE_ALIASES.get(key, _PROVINCE_ALIASES.get(key.replace(" ", ""), "limpopo"))


def _b(v):
    return 1.0 if v else 0.0


def _common(raw):
    prov = normalize_province(raw.get("province"))
    month = int(raw.get("month") or 6)
    month = min(12, max(1, month))
    out = {f"prov_{p}": 1.0 if p == prov else 0.0 for p in PROVINCES}
    out["month_sin"] = math.sin(2 * math.pi * (month - 1) / 12)
    out["month_cos"] = math.cos(2 * math.pi * (month - 1) / 12)
    out["peak_season"] = 1.0 if 5 <= month <= 8 else 0.0
    out["market_domestic"] = 1.0 if str(raw.get("market") or "international").strip().lower() == "domestic" else 0.0
    return out


def featurize(kind, raw):
    """raw -> {feature_name: float}. Missing optional inputs fall back to the same defaults the C# encoder uses."""
    f = _common(raw)
    svc = raw.get("services") or {}
    if kind == "PJ_PACKAGE":
        included = [resolve_species(s) for s in (raw.get("included_species") or [])]
        included = [s for s in included if s]
        f["duration_days"] = float(raw.get("duration_days") or 5)
        f["hunters_per_ph"] = 2.0 if int(raw.get("hunters_per_ph") or 1) >= 2 else 1.0
        f["svc_ph_included"] = _b(svc.get("ph_included", True))
        f["svc_accommodation"] = _b(svc.get("accommodation", True))
        f["svc_meals"] = _b(svc.get("meals", True))
        f["svc_field_prep"] = _b(svc.get("field_prep", True))
        f["svc_airport_transfer"] = _b(svc.get("airport_transfer", False))
        f["svc_videography"] = _b(svc.get("videography", False))
        f["svc_drone"] = _b(svc.get("drone", False))
        f["included_fee_log"] = math.log1p(sum(s["refFeeUsd"] for s in included))
        f["n_included"] = float(len(included))
        f["has_dangerous"] = _b(any(s["dangerous"] for s in included))
    elif kind == "PH_RATE":
        n = float(raw.get("rating_count") or 0)
        avg = float(raw.get("rating_avg") or 0)
        f["years_experience"] = float(raw.get("years_experience") or 0)
        f["has_rating"] = _b(n >= 1 and avg > 0)
        f["rating_avg"] = avg if n >= 1 else 0.0
        f["rating_n_log"] = math.log1p(n)
        f["hunts_log"] = math.log1p(float(raw.get("hunts_completed") or 0))
        f["is_dg_rate"] = _b(raw.get("is_dg_rate", False))
        f["dg_accredited"] = _b(raw.get("dg_accredited", False))
    elif kind == "VG_PACKAGE":
        f["days_covered"] = float(raw.get("days_covered") or 3)
        f["drone"] = _b(raw.get("drone", False))
        f["tier_idx"] = float(TIERS.get(str(raw.get("tier") or "standard").lower(), 1))
        f["crew_size"] = float(raw.get("crew_size") or 1)
        f["uhd_4k"] = _b(raw.get("uhd_4k", False))
        f["years_experience"] = float(raw.get("years_experience") or 0)
    elif kind == "FARM_DAILY_RATE":
        f["hunters_per_ph"] = 2.0 if int(raw.get("hunters_per_ph") or 1) >= 2 else 1.0
        f["farm_size_log"] = math.log1p(float(raw.get("farm_size_ha") or 2000))
        f["lodge_rooms"] = float(raw.get("lodge_rooms") or 0)
        f["catering"] = _b(raw.get("catering", True))
        f["cold_room"] = _b(raw.get("cold_room", False))
        f["guide_included"] = _b(raw.get("guide_included", True))
        f["pool"] = _b(raw.get("pool", False))
        f["wifi"] = _b(raw.get("wifi", False))
        f["n_species"] = float(raw.get("n_species") or 0)
        f["game_fenced"] = _b(raw.get("game_fenced", False))
    elif kind == "SPECIES_FEE":
        sp = resolve_species(raw.get("species"))
        tier = str(raw.get("tier") or "trophy").lower()
        f["species_ref_log"] = math.log(sp["refFeeUsd"]) if sp else math.log(1400.0)
        f["is_dangerous"] = _b(sp and sp["dangerous"])
        f["tier_female"] = _b(tier == "female")
        f["tier_male"] = _b(tier == "male")
        f["tier_trophy"] = _b(tier not in ("female", "male"))
        f["farm_size_log"] = math.log1p(float(raw.get("farm_size_ha") or 2000))
        f["game_fenced"] = _b(raw.get("game_fenced", False))
    else:
        raise ValueError(f"Unknown kind {kind}")
    return f


def vector(kind, raw):
    f = featurize(kind, raw)
    return [float(f[name]) for name in FEATURES[kind]]
