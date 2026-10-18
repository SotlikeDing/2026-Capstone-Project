"""Shared helpers for judging the AI Helper's written advice (used by compare_providers.py).

Everything here talks to the running WebAPI (admin token) so that prompts are BYTE-IDENTICAL to production prompts: the WebAPI's
admin dry-run endpoint returns the exact system prompt, user prompt and JSON schema for a request without calling any LLM.
Never hand-write prompts for an evaluation.
"""
from __future__ import annotations

import json
import math
import random
import re
import ssl
import sys
import urllib.error
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from common import SPECIES  # noqa: E402  (tools/ai/common.py)
from generate_synthetic import GENERATORS  # noqa: E402
import numpy as np  # noqa: E402


class Api:
    """Tiny stdlib HTTP client for the WebAPI (self-signed dev certificates allowed with insecure=True)."""

    def __init__(self, base: str, token: str, insecure: bool = False):
        self.base = base.rstrip("/")
        self.token = token
        self.ctx = ssl._create_unverified_context() if insecure else None

    def _call(self, method: str, path: str, body=None, timeout=120, raw: bytes | None = None, content_type: str = "application/json"):
        data = raw if raw is not None else (json.dumps(body).encode("utf-8") if body is not None else None)
        req = urllib.request.Request(f"{self.base}{path}", data=data, method=method,
                                     headers={"Authorization": f"Bearer {self.token}", "Content-Type": content_type})
        try:
            with urllib.request.urlopen(req, timeout=timeout, context=self.ctx) as r:
                return r.read().decode("utf-8")
        except urllib.error.HTTPError as e:
            raise RuntimeError(f"{method} {path} -> HTTP {e.code}: {e.read().decode('utf-8', 'replace')[:300]}") from None

    def dry_run(self, request: dict) -> dict:
        return json.loads(self._call("POST", "/api/aihelper/admin/dry-run", request))

    def status(self) -> dict:
        return json.loads(self._call("GET", "/api/aihelper/status"))

    def download(self, path: str) -> str:
        return self._call("GET", path)


# --------------------------------------------------------------------------- random, realistic requests

_PROVINCE_AF = {"limpopo": "Limpopo", "gauteng": "Gauteng", "mpumalanga": "Mpumalanga", "north_west": "Noordwes",
                "free_state": "Vrystaat", "northern_cape": "Noord-Kaap", "eastern_cape": "Oos-Kaap",
                "western_cape": "Wes-Kaap", "kwazulu_natal": "KwaZulu-Natal"}
_SPECIES_BY_ID = {s["id"]: s for s in SPECIES}

_PJ_TITLES = ["{prov} Trofee Safari", "{sp} & Rooibok Jagpakket", "Bosveld Jagekspedisie", "Naweek Jagpakket {prov}",
              "Groot Wild Ervaring", "Vlaktes Jagsafari", "{prov} Familie Jagtrip", "Premium {sp} Jag"]
_PJ_DESC = ["{d} dae jag op 'n wildsplaas in {prov}. Verblyf en etes ingesluit.",
            "Persoonlike jagervaring met ervare gids. {d} dae in die veld.",
            "Volledige pakket vir {d} dae: jag, verblyf en veldvoorbereiding.",
            "Ontspanne jagtrip met goeie wildstand. Geskik vir beginners en ervare jagters.", ""]
_VG_TITLES = ["4K Safari Film", "Jagvideo Hoogtepunte", "Filmiese Jagdokumentêr", "Hommeltuig Jagbeelde", "Jag-teaser vir sosiale media"]
_VG_DESC = ["Professionele beeldmateriaal van u jag met klank en redigering.", "Hoogtepunte-video binne 'n week afgelewer.", ""]
_AP_TITLES = ["Handgemaakte biltongmesse", "Taksidermie van wêreldgehalte", "4x4 Toebehore vir die veld", "Jagkamp-akkommodasie te huur",
              "Optika en kykers teen groothandelspryse", "Ammunisie en vuurwapenkuns"]
_AP_DESC = ["Kwaliteit produkte vir die ernstige jagter. Kontak ons vandag.", "Al u jagbehoeftes op een plek.", "Bestel nou en ontvang gratis aflewering.", ""]


def _fx(status: dict) -> float:
    return float(status.get("usdToZarRate", 18.0))


def _price(usd: float, currency: str, fx: float, rng: random.Random, spread: float = 0.30) -> float:
    v = usd * math.exp(rng.gauss(0, spread))          # spread so the dataset covers below / within / above verdicts
    return round(v * (fx if currency == "ZAR" else 1.0), -1 if v >= 50 else 0)


def random_request(role: str, seed: int, fx: float) -> dict:
    """A realistic AiOptimizeRequest for `role`, drawn from the same generators as the price model's bootstrap data."""
    rng = random.Random(seed)
    nrng = np.random.default_rng(seed)
    kind = {"PJ": "PJ_PACKAGE", "PH": "PH_RATE", "VG": "VG_PACKAGE", "FARMER": "FARM_DAILY_RATE"}.get(role)
    goal = rng.choice(["balanced", "profit", "sales"])

    if role == "AP":
        return {"roleCode": "AP", "goal": goal, "draft": {
            "title": rng.choice(_AP_TITLES), "description": rng.choice(_AP_DESC),
            "adCategory": rng.choice(["Toerusting", "Taksidermie", "Vervoer", "Akkommodasie", "Vuurwapens"]),
            "adPlacement": rng.choice(["Banner", "Spotlight", "Featured"])}}

    raw, target = GENERATORS[kind](nrng)
    market = raw["market"]
    currency = "ZAR" if (market == "domestic" or rng.random() < 0.15) else "USD"
    prov = raw["province"]
    req = {"roleCode": role, "province": _PROVINCE_AF[prov], "currency": currency, "market": market, "goal": goal,
           "month": raw["month"], "draft": {}}
    d = req["draft"]
    d["price"] = _price(target, currency, fx, rng) if rng.random() < 0.92 else None    # sometimes no current price

    if role == "PJ":
        names = [_SPECIES_BY_ID[i]["af"] for i in raw["included_species"]]
        sp = names[0] if names else "Koedoe"
        d.update(title=rng.choice(_PJ_TITLES).format(prov=_PROVINCE_AF[prov], sp=sp),
                 description=rng.choice(_PJ_DESC).format(prov=_PROVINCE_AF[prov], d=raw["duration_days"]),
                 durationDays=raw["duration_days"], huntersPerPh=raw["hunters_per_ph"],
                 services=[k for k, v in raw["services"].items() if v], species=names)
        if d["price"] and rng.random() < 0.6:
            d["observerPrice"] = round(d["price"] * rng.uniform(0.2, 0.8), -1)
    elif role == "PH":
        d.update(yearsExperience=raw["years_experience"], ratingAverage=raw["rating_avg"] or None,
                 ratingCount=raw["rating_count"], huntsCompleted=raw["hunts_completed"],
                 dangerousGameAccredited=raw["dg_accredited"], dangerousGameRate=raw["is_dg_rate"],
                 description=rng.choice(["Ervare PH met kennis van die Waterberg.", "Spesialiseer in leeftydsgroepe en beginners.", ""]))
    elif role == "VG":
        d.update(title=rng.choice(_VG_TITLES), description=rng.choice(_VG_DESC), durationDays=raw["days_covered"],
                 vgTier=raw["tier"], drone=raw["drone"], uhd4k=raw["uhd_4k"], crewSize=raw["crew_size"],
                 yearsExperience=raw["years_experience"])
    elif role == "FARMER":
        d.update(farmSizeHa=raw["farm_size_ha"], lodgeRooms=raw["lodge_rooms"], catering=raw["catering"], coldRoom=raw["cold_room"],
                 guideIncluded=raw["guide_included"], pool=raw["pool"], wifi=raw["wifi"], gameFenced=raw["game_fenced"],
                 huntersPerPh=raw["hunters_per_ph"])
        rows = []
        for i in range(rng.randint(1, 3)):
            sraw, starget = GENERATORS["SPECIES_FEE"](np.random.default_rng(seed * 31 + i))
            sraw = dict(sraw, market=market)
            sp = _SPECIES_BY_ID[sraw["species"]]
            rows.append({"species": sp["af"], "tier": sraw["tier"], "price": _price(sp["refFeeUsd"] * {"female": .4, "male": .65, "trophy": 1}[sraw["tier"]]
                                                                                 * (0.25 if market == "domestic" else 1), currency, fx, rng)})
        d["speciesPricing"] = rows
        d["species"] = [r["species"] for r in rows]
    return req


# --------------------------------------------------------------------------- quality gates

_NUM = re.compile(r"\d+(?:[.,]\d+)?")
_AF_HINTS = {"die", "en", "van", "vir", "jou", "u", "is", "om", "te", "met", "nie", "kan", "wat", "as", "dit", "op", "ook", "sal", "moet", "prys", "jag"}
_EN_HINTS = {"the", "and", "of", "for", "you", "your", "is", "to", "with", "not", "can", "that", "this", "price", "hunt"}


# Thousands separators (space, NBSP, thin space, comma) between digit groups: "4 080", "3,236", "R3 236" -> "4080", "3236".
_SEP = re.compile(r"(?<=\d)[    ,](?=\d{3}(?!\d))")


def _canon(tok: str) -> str:
    """'048' -> '48', '48,30' -> '48.3', '3236' -> '3236' (one canonical spelling per numeric value)."""
    t = tok.replace(",", ".")
    try:
        v = float(t)
    except ValueError:
        return t
    return str(int(v)) if v == int(v) else f"{v:.4f}".rstrip("0").rstrip(".")


def numbers_in(text: str) -> set[str]:
    return {_canon(t) for t in _NUM.findall(_SEP.sub("", text))}


def allowed_numbers(context: str) -> set[str]:
    """Every number in the context, plus its rounded forms (a teacher may say '48%' for 48.3), plus small counts and years."""
    out = {str(i) for i in range(0, 11)} | {"2026", "2027"}
    for tok in numbers_in(context):
        out.add(tok)
        try:
            v = float(tok)
        except ValueError:
            continue
        out.add(str(round(v)))
        out.add(_canon(f"{round(v, 1)}"))
    return out


def _all_text(advice: dict) -> str:
    parts = [advice.get("headline", ""), advice.get("descriptionRewrite", ""), advice.get("seasonalAdvice", "")]
    parts += advice.get("titleSuggestions", []) + advice.get("risks", [])
    for k in ("inclusionSuggestions", "upsells", "tips"):
        for it in advice.get(k, []):
            parts += [str(v) for kk, v in it.items() if isinstance(v, str)]
    return "\n".join(p for p in parts if p)


def check_advice(advice: dict, user_prompt: str) -> list[str]:
    """Returns the list of reasons this advice must be rejected (empty = accepted)."""
    problems = []
    required = ["headline", "titleSuggestions", "descriptionRewrite", "inclusionSuggestions", "upsells", "seasonalAdvice", "tips", "risks"]
    for k in required:
        if k not in advice:
            problems.append(f"missing:{k}")
    if problems:
        return problems
    if len(advice["titleSuggestions"]) < 2 or len(advice["tips"]) < 3 or len(advice["inclusionSuggestions"]) < 2:
        problems.append("too-few-items")
    titles = [t.get("title", "").lower() for t in advice["tips"]]
    if len(set(titles)) != len(titles):
        problems.append("duplicate-tips")
    if len(advice["headline"]) < 20:
        problems.append("headline-too-short")

    text = _all_text(advice)
    # Rule 3 of the system prompt: never state a number that is not in the context. Allow small counts (1-10) and years.
    allowed = allowed_numbers(user_prompt)
    invented = sorted(n for n in numbers_in(text) if n not in allowed)
    if invented:
        problems.append("invented-numbers:" + ",".join(invented[:5]))

    words = re.findall(r"[a-zA-Zëïöüêôéè']+", text.lower())
    af = sum(w in _AF_HINTS for w in words)
    en = sum(w in _EN_HINTS for w in words)
    if words and af < max(3, en):
        problems.append("not-afrikaans")
    return problems
