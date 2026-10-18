"""Bootstrap ("dummy") training data for the MikPunt package-price models.

Why synthetic: the platform itself only holds a handful of listings, far too few to train on. So the first model
is trained on data generated from PUBLISHED 2026/27 South African outfitter prices (see data/market_facts.json and
data/species_catalog.json) plus clearly-labelled assumptions. Every price is USD.

Each formula below is tagged:
  [ANCHORED]  reproduces a figure published by real outfitters (with realistic listing-to-listing spread)
  [ASSUMED]   a modelling assumption with no published source - real MikPunt data should overwrite it

The result is a model that starts out consistent with the public market and is then corrected by real
observations (see train_price_model.py --real ...). Nothing here is presented to end users as measured fact:
the API reports data provenance and confidence with every answer.

Usage:  python generate_synthetic.py --n 6000 --seed 42 --out out/synthetic
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path

import numpy as np

from common import KINDS, PROVINCES, SPECIES

# [ASSUMED] regional / seasonal multipliers - see market_facts.json "assumed"
PROV_MULT = {
    "limpopo": 1.00, "north_west": 0.97, "gauteng": 1.05, "mpumalanga": 1.03, "free_state": 0.94,
    "northern_cape": 0.96, "eastern_cape": 0.95, "western_cape": 1.04, "kwazulu_natal": 1.02,
}
PROV_WEIGHT = [0.30, 0.05, 0.08, 0.14, 0.07, 0.10, 0.14, 0.04, 0.08]  # Limpopo/NW/EC dominate SA hunting
TIER_MULT = {"female": 0.40, "male": 0.65, "trophy": 1.00}           # [ASSUMED]


def season_mult(month: int) -> float:
    if 5 <= month <= 8:
        return 1.06     # [ASSUMED] peak winter hunting season
    if month in (12, 1, 2):
        return 0.96     # [ASSUMED] hot, off-peak
    return 1.0


def _noise(rng, sigma):
    return math.exp(rng.normal(0.0, sigma))


# [ASSUMED] domestic (ZAR, local-hunter) price level relative to the published international USD price level.
# No published domestic list could be verified; the only anchor is ~R1,000 per person per night incl. meals and the
# platform's own defaults. See market_facts.json "domestic".
DOMESTIC_MULT = {"FARM_DAILY_RATE": 0.16, "SPECIES_FEE": 0.25, "PJ_PACKAGE": 0.30, "PH_RATE": 0.85, "VG_PACKAGE": 0.55}
DOMESTIC_SHARE = 0.35   # MikPunt is a South African platform: a large share of traffic is local
DOMESTIC_SIGMA = {"FARM_DAILY_RATE": .20, "SPECIES_FEE": .22, "PJ_PACKAGE": .20, "PH_RATE": .20, "VG_PACKAGE": .18}


def _common_raw(rng):
    return {"province": str(rng.choice(PROVINCES, p=PROV_WEIGHT)), "month": int(rng.integers(1, 13)),
            "market": "domestic" if rng.random() < DOMESTIC_SHARE else "international"}


def _apply_market(kind, raw, value, rng):
    """Scales an international-level price to the domestic level and widens the noise (the domestic level is assumed)."""
    if raw["market"] == "domestic":
        return value * DOMESTIC_MULT[kind] * _noise(rng, DOMESTIC_SIGMA[kind])
    return value


def gen_pj_package(rng):
    raw = _common_raw(rng)
    d = int(rng.choice([3, 4, 5, 6, 7, 8, 10, 12, 14], p=[.07, .10, .22, .13, .20, .10, .08, .05, .05]))
    hpp = int(rng.choice([1, 2], p=[.40, .60]))
    video = bool(rng.random() < .15)
    svc = {
        "ph_included": bool(rng.random() < .93), "accommodation": bool(rng.random() < .95),
        "meals": bool(rng.random() < .95), "field_prep": bool(rng.random() < .92),
        "airport_transfer": bool(rng.random() < .30), "videography": video,
        "drone": bool(rng.random() < (.45 if video else .02)),
    }
    included = []
    if rng.random() < .40:
        cheap_first = np.array([1.0 / math.sqrt(s["refFeeUsd"]) for s in SPECIES])
        picks = rng.choice(len(SPECIES), size=int(rng.integers(1, 6)), replace=False, p=cheap_first / cheap_first.sum())
        included = [SPECIES[i] for i in picks]
    raw.update({"duration_days": d, "hunters_per_ph": hpp, "services": svc,
                "included_species": [s["id"] for s in included]})

    daily = 438.0 if hpp == 1 else 362.0                                    # [ANCHORED] 1x1 380-500, 2x1 335-400 (3 outfitters)
    daily *= (1 - 0.40 * (0 if svc["ph_included"] else 1))                  # [ASSUMED]
    daily *= (1 - 0.25 * (0 if svc["accommodation"] else 1))                # [ASSUMED]
    daily *= (1 - 0.20 * (0 if svc["meals"] else 1))                        # [ASSUMED]
    daily *= (1 - 0.05 * (0 if svc["field_prep"] else 1))                   # [ASSUMED]
    daily *= PROV_MULT[raw["province"]] * season_mult(raw["month"])
    extras_per_day = (380.0 * svc["videography"] + 60.0 * svc["drone"]) / hpp   # [ANCHORED] video 300-480/day (median ~380) shared by the party
    airport = 170.0 * svc["airport_transfer"]                               # [ANCHORED] 170 per person
    dur_factor = max(0.90, 1 - 0.01 * max(0, d - 7))                        # [ASSUMED] mild long-stay discount
    fees = sum(s["refFeeUsd"] * (1 + rng.normal(0, .05)) for s in included) # [ANCHORED] published trophy fees
    total = ((daily + extras_per_day) * d * dur_factor + airport + fees) * _noise(rng, .10)
    return raw, _apply_market("PJ_PACKAGE", raw, total, rng)


def gen_ph_rate(rng):
    raw = _common_raw(rng)
    years = int(min(40, rng.gamma(2.2, 6.0)))
    n = 0 if rng.random() < .40 else int(rng.integers(1, 80))
    avg = float(np.clip(rng.normal(4.5, .35), 3.0, 5.0)) if n else 0.0
    accredited = bool(rng.random() < .25)
    raw.update({"years_experience": years, "rating_count": n, "rating_avg": round(avg, 2),
                "hunts_completed": int(rng.integers(0, 400)), "dg_accredited": accredited,
                "is_dg_rate": accredited and bool(rng.random() < .35)})
    # [ASSUMED] published daily rates bundle the PH's cost (~35-45% of 350-500), so no standalone PH price is published.
    base = 160 + 4.5 * min(years, 30)
    if n:
        base *= 1 + 0.08 * (avg - 4.3) * min(1.0, n / 10.0)
    base *= 1 + 0.015 * math.log1p(raw["hunts_completed"])
    if raw["is_dg_rate"]:
        base *= 1.8
    base *= PROV_MULT[raw["province"]] * season_mult(raw["month"])
    return raw, _apply_market("PH_RATE", raw, base * _noise(rng, .15), rng)


def gen_vg_package(rng):
    raw = _common_raw(rng)
    days = int(rng.choice(range(1, 15), p=[.03, .05, .17, .14, .14, .10, .10, .09, .05, .04, .03, .02, .02, .02]))
    tier = str(rng.choice(["basic", "standard", "documentary"], p=[.25, .5, .25]))
    drone = bool(rng.random() < .30)
    uhd = bool(rng.random() < .35)
    crew = int(rng.choice([1, 2], p=[.80, .20]))
    years = int(rng.integers(0, 21))
    raw.update({"days_covered": days, "tier": tier, "drone": drone, "uhd_4k": uhd, "crew_size": crew,
                "years_experience": years})
    day_rate = {"basic": 320.0, "standard": 380.0, "documentary": 450.0}[tier]  # [ANCHORED] quotes 300-480/day, median ~380
    day_rate += 150.0 * uhd                                                    # [ANCHORED] 4K upgrade +150/day
    day_rate += 80.0 * drone                                                   # [ASSUMED] drone add-on
    day_rate *= 1 + 0.007 * min(years, 20)                                     # [ASSUMED]
    if crew == 2:
        day_rate *= 1.7                                                        # [ASSUMED]
    day_rate *= math.sqrt(PROV_MULT[raw["province"]]) * math.sqrt(season_mult(raw["month"]))
    return raw, _apply_market("VG_PACKAGE", raw, day_rate * days * _noise(rng, .12), rng)   # [ANCHORED] flat per-day, no long-stay discount


def gen_farm_daily_rate(rng):
    raw = _common_raw(rng)
    hpp = int(rng.choice([1, 2], p=[.40, .60]))
    size = float(np.clip(rng.lognormal(math.log(2500), .9), 150, 40000))
    rooms = int(min(24, rng.geometric(.25)))
    catering = bool(rng.random() < .85)
    guide = bool(rng.random() < .70)
    nsp = int(rng.integers(3, 26))
    raw.update({"hunters_per_ph": hpp, "farm_size_ha": round(size), "lodge_rooms": rooms, "catering": catering,
                "cold_room": bool(rng.random() < .60), "guide_included": guide, "pool": bool(rng.random() < .35),
                "wifi": bool(rng.random() < .55), "n_species": nsp, "game_fenced": bool(rng.random() < .35)})
    rate = 438.0 if hpp == 1 else 362.0                                        # [ANCHORED]
    rate *= 0.60 + 0.40 * guide                                                # [ASSUMED] PH ~40% of the all-in rate
    rate *= 1.0 if catering else 0.80                                          # [ASSUMED]
    rate *= 1 + 0.012 * min(rooms, 20)                                         # [ASSUMED]
    rate *= 1.04 if raw["pool"] else 1.0
    rate *= 1.02 if raw["wifi"] else 1.0
    rate *= 1.03 if raw["cold_room"] else 1.0
    rate *= 1 + 0.03 * float(np.clip(math.log(size / 2000.0), -1.5, 1.5))
    rate *= 1 + 0.004 * min(nsp, 25)
    rate *= 1.03 if raw["game_fenced"] else 1.0
    rate *= PROV_MULT[raw["province"]] * season_mult(raw["month"])
    return raw, _apply_market("FARM_DAILY_RATE", raw, rate * _noise(rng, .12), rng)


def gen_species_fee(rng):
    raw = _common_raw(rng)
    sp = SPECIES[int(rng.integers(0, len(SPECIES)))]
    tier = str(rng.choice(["female", "male", "trophy"], p=[.25, .30, .45]))
    size = float(np.clip(rng.lognormal(math.log(2500), .9), 150, 40000))
    fenced = bool(rng.random() < .30)
    raw.update({"species": sp["id"], "tier": tier, "farm_size_ha": round(size), "game_fenced": fenced})
    fee = sp["refFeeUsd"] * TIER_MULT[tier]                                     # [ANCHORED] ref fee; [ASSUMED] tier multipliers
    fee *= PROV_MULT[raw["province"]] ** 0.7
    fee *= 1 + 0.03 * float(np.clip(math.log(size / 2000.0), -1.5, 1.5))
    fee *= 1.08 if (fenced and sp["refFeeUsd"] > 2500) else 1.0                # [ASSUMED] high-fence premium on premium species
    return raw, _apply_market("SPECIES_FEE", raw, fee * _noise(rng, .14), rng)   # [ANCHORED] outfitters differ by ~10-30%


GENERATORS = {
    "PJ_PACKAGE": gen_pj_package, "PH_RATE": gen_ph_rate, "VG_PACKAGE": gen_vg_package,
    "FARM_DAILY_RATE": gen_farm_daily_rate, "SPECIES_FEE": gen_species_fee,
}


def generate(kind: str, n: int, seed: int):
    rng = np.random.default_rng(seed + KINDS.index(kind))
    rows = []
    for _ in range(n):
        raw, target = GENERATORS[kind](rng)
        rows.append({"kind": kind, "raw": raw, "target_usd": round(float(target), 2), "source": "synthetic"})
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--n", type=int, default=6000, help="rows per kind")
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--out", default="out/synthetic")
    args = ap.parse_args()
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    for kind in KINDS:
        rows = generate(kind, args.n, args.seed)
        with open(out / f"{kind.lower()}.jsonl", "w", encoding="utf-8") as fh:
            for r in rows:
                fh.write(json.dumps(r) + "\n")
        print(f"{kind}: {len(rows)} rows -> {out / (kind.lower() + '.jsonl')}")


if __name__ == "__main__":
    main()
