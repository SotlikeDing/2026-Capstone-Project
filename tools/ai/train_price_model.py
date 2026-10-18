"""Train the MikPunt package-price models (one gradient-boosted regressor per offer kind) and export them to ONNX.

Continuous-learning behaviour
-----------------------------
* Day 0 (no real data): trains on the synthetic bootstrap only (generate_synthetic.py, anchored on published prices).
* As real observations arrive (--real <jsonl files>, e.g. from export_platform_data.py or a hand-collected price list),
  the real share of the training weight grows:  real_share = min(0.80, n_real / (n_real + 200)).
  50% at 200 real rows, capped at 80% so a handful of odd listings can never fully override the market prior.
* The uncertainty band is recalibrated on held-out data (split-conformal style, in log space). Once >= 30 real rows
  exist for a kind, the band comes from the REAL residuals, so it honestly widens if the priors were off.
* The report shows how wrong the synthetic prior was on the real rows ("prior_error_on_real") - that is the
  learning signal: it tells you which assumptions in generate_synthetic.py to distrust.

Usage
-----
  python train_price_model.py                                   # synthetic bootstrap only
  python train_price_model.py --real data/real/*.jsonl          # blend in real rows
  python train_price_model.py --real data/real/*.jsonl --promote   # ...and install into the WebAPI

Output: out/models/<version>/{*.onnx, feature_spec.json, model_card.json, golden_vectors.json, species_catalog.json}
"""
from __future__ import annotations

import argparse
import datetime as dt
import glob
import json
import shutil
from pathlib import Path

import numpy as np
import onnxruntime as ort
from skl2onnx import convert_sklearn
from skl2onnx.common.data_types import FloatTensorType
from sklearn.ensemble import HistGradientBoostingRegressor
from sklearn.model_selection import train_test_split

from common import ASSUMED_ONLY_KINDS, DATA, FEATURES, KINDS, PROVINCES, ROOT, vector
from generate_synthetic import generate

ONNX_FILES = {k: f"{k.lower()}.onnx" for k in KINDS}
REAL_SHARE_CAP = 0.80
REAL_SHARE_HALF_POINT = 200
MIN_REAL_FOR_REAL_BAND = 30

# Independent sanity anchors taken straight from published price lists (NOT from the generator).
# (kind, raw input, low USD, high USD, where the range comes from)
SANITY = [
    ("PJ_PACKAGE", {"province": "limpopo", "month": 7, "duration_days": 7, "hunters_per_ph": 1}, 2660, 3500,
     "7 days x published 1x1 rate 380-500"),
    ("PJ_PACKAGE", {"province": "limpopo", "month": 7, "duration_days": 5, "hunters_per_ph": 2}, 1675, 2000,
     "5 days x published 2x1 rate 335-400"),
    ("FARM_DAILY_RATE", {"province": "limpopo", "month": 7, "hunters_per_ph": 1, "guide_included": True, "catering": True}, 380, 520,
     "published all-in 1x1 daily rate 380-500"),
    ("SPECIES_FEE", {"province": "limpopo", "month": 7, "species": "kudu", "tier": "trophy"}, 3000, 4500,
     "published kudu fee 3,100-4,250"),
    ("SPECIES_FEE", {"province": "limpopo", "month": 7, "species": "impala", "tier": "trophy"}, 450, 650,
     "published impala fee 525-550"),
    ("SPECIES_FEE", {"province": "limpopo", "month": 7, "species": "buffalo", "tier": "trophy"}, 10500, 14500,
     "published buffalo bull fee 12,000-13,500"),
    ("VG_PACKAGE", {"province": "limpopo", "month": 7, "days_covered": 8, "tier": "standard"}, 2300, 3500,
     "published 8-day filming package 2,800"),
    # Domestic segment: the only anchors are a ~R1,000 pppn snippet and the platform's own defaults (R850-R1,400) - weak.
    ("FARM_DAILY_RATE", {"province": "limpopo", "month": 7, "market": "domestic", "hunters_per_ph": 1, "guide_included": False,
                         "catering": True}, 40, 95, "domestic ~R1,000 pppn incl. meals (R720-R1,700 at 18 ZAR/USD)"),
]


def load_real(patterns):
    rows = []
    for pat in patterns or []:
        for path in glob.glob(pat):
            for line in Path(path).read_text(encoding="utf-8").splitlines():
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                r = json.loads(line)
                if r.get("kind") in KINDS and r.get("target_usd", 0) > 0:
                    r.setdefault("source", "real")
                    rows.append(r)
    return rows


def real_share(n_real):
    return min(REAL_SHARE_CAP, n_real / (n_real + REAL_SHARE_HALF_POINT)) if n_real else 0.0


def make_model(seed):
    return HistGradientBoostingRegressor(
        loss="squared_error", max_iter=260, learning_rate=0.07, max_leaf_nodes=15, min_samples_leaf=15,
        l2_regularization=0.5, random_state=seed,
    )


def mape(y_true_log, y_pred_log):
    t, p = np.exp(y_true_log), np.exp(y_pred_log)
    return float(np.mean(np.abs(p - t) / t))


def train_kind(kind, syn_rows, real_rows, seed):
    rows = syn_rows + real_rows
    X = np.array([vector(kind, r["raw"]) for r in rows], dtype=np.float32)
    y = np.log(np.array([r["target_usd"] for r in rows], dtype=np.float64))
    is_real = np.array([r.get("source") != "synthetic" for r in rows])
    n_real, n_syn = int(is_real.sum()), int((~is_real).sum())
    s = real_share(n_real)
    N = len(rows)
    w = np.where(is_real, (s * N / max(1, n_real)), ((1 - s) * N / max(1, n_syn)))
    row_w = np.array([r.get("weight", 1.0) for r in rows])
    w = w * row_w

    idx = np.arange(N)
    tr, va = train_test_split(idx, test_size=0.2, random_state=seed, stratify=is_real if 0 < n_real >= 2 else None)
    model = make_model(seed).fit(X[tr], y[tr], sample_weight=w[tr])
    res_va = y[va] - model.predict(X[va])

    # Band: real residuals once there are enough of them, otherwise the (synthetic-dominated) validation residuals.
    real_va = is_real[va]
    band_src = "real" if real_va.sum() >= MIN_REAL_FOR_REAL_BAND else "validation-mixed"
    band_res = res_va[real_va] if band_src == "real" else res_va
    lo, hi = float(np.quantile(band_res, 0.10)), float(np.quantile(band_res, 0.90))

    metrics = {
        "val_mape_all": round(mape(y[va], y[va] - res_va), 4),
        "val_band_coverage": round(float(np.mean((res_va >= lo) & (res_va <= hi))), 3),
        "val_rows": int(len(va)),
    }
    if real_va.sum():
        metrics["val_mape_real"] = round(mape(y[va][real_va], (y[va] - res_va)[real_va]), 4)

    prior_error = None
    if n_real:
        prior_model = make_model(seed).fit(X[~is_real], y[~is_real])
        prior_error = round(mape(y[is_real], prior_model.predict(X[is_real])), 4)

    final = make_model(seed).fit(X, y, sample_weight=w)          # refit on everything with the same hyper-parameters
    return final, X, {
        "n_synthetic": n_syn, "n_real": n_real, "real_share": round(s, 3), "band_source": band_src,
        "bandLog": {"lo": round(lo, 4), "hi": round(hi, 4)}, "metrics": metrics, "prior_error_on_real": prior_error,
    }


def export_onnx(kind, model, out_dir):
    n = len(FEATURES[kind])
    onx = convert_sklearn(model, initial_types=[("input", FloatTensorType([None, n]))], target_opset={"": 17, "ai.onnx.ml": 3})
    path = out_dir / ONNX_FILES[kind]
    path.write_bytes(onx.SerializeToString())
    return path


def onnx_predict(path, X):
    sess = ort.InferenceSession(str(path), providers=["CPUExecutionProvider"])
    return sess.run(None, {"input": X.astype(np.float32)})[0].reshape(-1)


def golden_cases(kind, syn_rows, count=10):
    cases = []
    for r in syn_rows[:count]:
        cases.append({"kind": kind, "raw": r["raw"]})
    return cases


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--n", type=int, default=6000, help="synthetic rows per kind")
    ap.add_argument("--seed", type=int, default=42)
    ap.add_argument("--real", nargs="*", default=[], help="JSONL files of real observations (globs ok)")
    ap.add_argument("--version", default=dt.datetime.now(dt.timezone.utc).strftime("%Y.%m.%d.%H%M"))
    ap.add_argument("--out", default=str(ROOT / "out" / "models"))
    ap.add_argument("--promote", action="store_true", help="copy the trained bundle into the WebAPI AiModels folder")
    args = ap.parse_args()

    out_dir = Path(args.out) / args.version
    out_dir.mkdir(parents=True, exist_ok=True)
    real_all = load_real(args.real)
    print(f"model version {args.version}: {len(real_all)} real rows loaded")

    spec = {"schemaVersion": 1, "modelVersion": args.version, "currency": "USD", "provinces": PROVINCES,
            "assumedMarkets": ["domestic"],
            "trainedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(), "kinds": {}}
    card = {"modelVersion": args.version, "currency": "USD", "kinds": {}, "sanityChecks": [],
            "dataProvenance": {
                "synthetic": "Generated by generate_synthetic.py from published 2026/27 SA outfitter prices "
                             "(data/market_facts.json, data/species_catalog.json) plus labelled assumptions.",
                "assumedOnlyKinds": ASSUMED_ONLY_KINDS,
                "real": f"{len(real_all)} real observations blended in" if real_all else "none yet - bootstrap model"}}
    golden = []
    models = {}

    for kind in KINDS:
        syn = generate(kind, args.n, args.seed)
        real = [r for r in real_all if r["kind"] == kind]
        model, X, info = train_kind(kind, syn, real, args.seed)
        path = export_onnx(kind, model, out_dir)
        diff = float(np.max(np.abs(onnx_predict(path, X[:300]) - model.predict(X[:300]))))
        assert diff < 1e-3, f"{kind}: ONNX vs sklearn mismatch {diff}"
        models[kind] = path

        evidence = "assumed" if kind in ASSUMED_ONLY_KINDS else ("real+published" if info["n_real"] >= MIN_REAL_FOR_REAL_BAND else "published")
        spec["kinds"][kind] = {"onnx": path.name, "features": FEATURES[kind], "logTarget": True, "bandLog": info["bandLog"],
                               "evidence": evidence, "nReal": info["n_real"], "nSynthetic": info["n_synthetic"]}
        card["kinds"][kind] = {**info, "evidence": evidence, "onnxVsSklearnMaxDiff": diff}
        print(f"{kind:16s} rows={info['n_synthetic']}+{info['n_real']}  val MAPE={info['metrics']['val_mape_all']:.3f}  "
              f"band=[{np.exp(info['bandLog']['lo']):.2f}x..{np.exp(info['bandLog']['hi']):.2f}x] coverage={info['metrics']['val_band_coverage']}"
              + (f"  prior_error_on_real={info['prior_error_on_real']}" if info["prior_error_on_real"] is not None else ""))
        for c in golden_cases(kind, syn):
            v = np.array([vector(kind, c["raw"])], dtype=np.float32)
            golden.append({**c, "features": [float(x) for x in v[0]],
                           "p50Usd": float(np.exp(onnx_predict(path, v)[0]))})

    print("\nSanity checks against PUBLISHED prices:")
    failed = 0
    for kind, raw, lo, hi, why in SANITY:
        v = np.array([vector(kind, raw)], dtype=np.float32)
        p = float(np.exp(onnx_predict(models[kind], v)[0]))
        ok = lo <= p <= hi
        failed += not ok
        card["sanityChecks"].append({"kind": kind, "raw": raw, "predictedUsd": round(p, 2), "expectedLow": lo, "expectedHigh": hi,
                                     "basis": why, "pass": ok})
        print(f"  [{'ok' if ok else 'FAIL'}] {kind:16s} {p:9.2f}  expected {lo}-{hi}  ({why})")

    (out_dir / "feature_spec.json").write_text(json.dumps(spec, indent=2), encoding="utf-8")
    (out_dir / "model_card.json").write_text(json.dumps(card, indent=2), encoding="utf-8")
    (out_dir / "golden_vectors.json").write_text(json.dumps(golden, indent=2), encoding="utf-8")
    shutil.copy(DATA / "species_catalog.json", out_dir / "species_catalog.json")
    shutil.copy(DATA / "market_facts.json", out_dir / "market_facts.json")
    print(f"\nWrote bundle to {out_dir}   ({failed} sanity check(s) failed)")

    if args.promote:
        target = ROOT.parent.parent / "src" / "WebAPI" / "WebAPI" / "AiModels" / "package_price"
        if target.exists():
            shutil.rmtree(target)
        shutil.copytree(out_dir, target)
        print(f"Promoted to {target}  (restart the WebAPI or POST /api/aihelper/admin/reload-models)")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
