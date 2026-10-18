"""Measure Outo-skoot's thresholds instead of guessing them.

Runs the SAME YAMNet TFLite model the app ships (src/jagbestuur_app/lib/assets/models/yamnet.tflite) over labelled clips,
the same way the app does: find the onset with the same energy-jump rule as OnsetGate (lib/services/gunshot/onset_gate.dart),
take a 0.975 s window from 0.2 s before it, and score it with the max of the gun classes. Then it reports recall / false-pin rate
per threshold and picks the three sensitivity levels:

  Hoog   (high)   - the highest threshold that still keeps >= 95% of the gunshots
  Medium          - the threshold with the best F1
  Laag   (low)    - the lowest threshold with <= 0.5% false pins on the non-gunshot clips

Data (download yourself; licences are non-commercial research - used here only to measure, never shipped in the app):
  --us8k   UrbanSound8K root (https://zenodo.org/records/1203745): class 6 gun_shot = positives; car_horn, children_playing,
           dog_bark, engine_idling (and any other class present) = negatives
  --esc50  ESC-50 root (https://github.com/karolpiczak/ESC-50): all 50 classes are non-gunshot negatives (door knocks, footsteps,
           clapping, wood creaks, wind, thunder, fireworks, ...), reported per category

  pip install ai-edge-litert numpy scipy
  python tools/audio/calibrate_yamnet.py --us8k <dir>/UrbanSound8K --esc50 <dir>/ESC-50-master

Note: dataset clips are normalised differently from a phone microphone, so only the MODEL threshold is calibrated here. The
absolute loudness gate (dBFS) and the near/far level are checked on a real phone (field test).
"""
from __future__ import annotations

import argparse
import csv
import json
import math
import sys
from pathlib import Path

import numpy as np
from scipy.io import wavfile
from scipy.signal import resample_poly

ROOT = Path(__file__).resolve().parents[2]
MODEL = ROOT / "src/jagbestuur_app/lib/assets/models/yamnet.tflite"
CLASS_MAP = ROOT / "src/jagbestuur_app/lib/assets/models/yamnet_class_map.csv"
GUN_CLASSES = {"Gunshot, gunfire", "Machine gun", "Fusillade", "Cap gun"}   # = kGunshotClassNames in shot_classifier.dart

SR = 16000
WINDOW = 15600
FRAME = 320
PRE_ROLL = SR // 5
LEAD_IN = SR // 2      # many dataset clips start AT the bang; the phone always hears background before it
LEAD_IN_DBFS = -60.0   # quiet bushveld background
MIN_RISE_DB = 18.0     # OnsetGate.minRiseDb
HISTORY = 10           # OnsetGate.historyFrames


def load_audio(path: Path) -> np.ndarray | None:
    try:
        rate, data = wavfile.read(path)
    except Exception:
        return None
    # Scale integers to [-1, 1] before mixing down: mean() turns them into floats and the scaling would be skipped.
    if data.dtype.kind == "i":
        data = data / float(np.iinfo(data.dtype).max)
    elif data.dtype.kind == "u":
        data = (data.astype(np.float64) - 128) / 128.0
    if data.ndim > 1:
        data = data.mean(axis=1)
    data = data.astype(np.float32)
    if rate != SR:
        g = math.gcd(int(rate), SR)
        data = resample_poly(data, SR // g, int(rate) // g).astype(np.float32)
    return np.clip(data, -1, 1)


def first_onset(x: np.ndarray) -> int | None:
    """Same rule as OnsetGate, minus the absolute dBFS level (dataset levels are not phone levels)."""
    history: list[float] = []
    for start in range(0, len(x) - FRAME + 1, FRAME):
        rms = float(np.sqrt(np.mean(x[start:start + FRAME] ** 2)))
        if len(history) >= HISTORY // 2:
            bg = sum(history) / len(history)
            rise = 20 * math.log10(max(rms, 1e-9)) - 20 * math.log10(max(bg, 1e-9))
            if rise >= MIN_RISE_DB:
                return start
        history.append(rms)
        if len(history) > HISTORY:
            history.pop(0)
    return None


def window_at(x: np.ndarray, onset: int) -> np.ndarray:
    start = max(0, onset - PRE_ROLL)
    w = x[start:start + WINDOW]
    if len(w) < WINDOW:
        w = np.pad(w, (0, WINDOW - len(w)))
    return w.astype(np.float32)


class Yamnet:
    def __init__(self):
        from ai_edge_litert.interpreter import Interpreter
        self.it = Interpreter(model_path=str(MODEL))
        self.inp = self.it.get_input_details()[0]["index"]
        self.it.resize_tensor_input(self.inp, [WINDOW])
        self.it.allocate_tensors()
        self.out = self.it.get_output_details()[0]["index"]
        with open(CLASS_MAP, encoding="utf-8") as f:
            names = [row["display_name"] for row in csv.DictReader(f)]
        self.gun = [i for i, n in enumerate(names) if n in GUN_CLASSES]
        assert self.gun, "no gun classes in the class map"

    def gun_score(self, w: np.ndarray) -> float:
        self.it.set_tensor(self.inp, w)
        self.it.invoke()
        scores = self.it.get_tensor(self.out)[0]
        return float(scores[self.gun].max())


def collect(us8k: Path | None, esc50: Path | None):
    """Yields (path, label 0/1, category)."""
    if us8k:
        meta = us8k / "metadata" / "UrbanSound8K.csv"
        with open(meta, encoding="utf-8") as f:
            for row in csv.DictReader(f):
                p = us8k / "audio" / f"fold{row['fold']}" / row["slice_file_name"]
                if p.exists():
                    yield p, int(row["classID"] == "6"), "us8k:" + row["class"]
    if esc50:
        with open(esc50 / "meta" / "esc50.csv", encoding="utf-8") as f:
            for row in csv.DictReader(f):
                yield esc50 / "audio" / row["filename"], 0, "esc50:" + row["category"]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--us8k", type=Path)
    ap.add_argument("--esc50", type=Path)
    ap.add_argument("--out", type=Path, default=Path(__file__).with_name("calibration_result.json"))
    args = ap.parse_args()
    if not args.us8k and not args.esc50:
        sys.exit("Pass --us8k and/or --esc50.")

    model = Yamnet()
    lead_in = (np.random.default_rng(0).standard_normal(LEAD_IN) * 10 ** (LEAD_IN_DBFS / 20)).astype(np.float32)
    rows = []
    for path, label, cat in collect(args.us8k, args.esc50):
        x = load_audio(path)
        if x is None or len(x) < FRAME * 6:
            continue
        x = np.concatenate([lead_in, x])         # same for every clip, so positives and negatives stay comparable
        onset = first_onset(x)
        gate = onset is not None
        if onset is None:                        # the app would not have run the model; score the loudest frame for the report
            frames = len(x) // FRAME
            onset = int(np.argmax([np.abs(x[i * FRAME:(i + 1) * FRAME]).max() for i in range(frames)])) * FRAME
        rows.append({"label": label, "cat": cat, "gate": gate, "score": model.gun_score(window_at(x, onset))})
    pos = [r for r in rows if r["label"] == 1]
    neg = [r for r in rows if r["label"] == 0]
    if not pos or not neg:
        sys.exit(f"Need gunshot and non-gunshot clips (have {len(pos)} / {len(neg)}).")

    def fires(r, t):  # the app needs the gate AND the score
        return r["gate"] and r["score"] >= t

    grid = [round(t, 3) for t in np.concatenate([np.arange(0.01, 0.1, 0.01), np.arange(0.1, 0.95, 0.025)])]
    table = []
    for t in grid:
        tp = sum(fires(r, t) for r in pos)
        fp = sum(fires(r, t) for r in neg)
        recall = tp / len(pos)
        fpr = fp / len(neg)
        precision = tp / (tp + fp) if tp + fp else 1.0
        f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
        table.append({"t": t, "recall": recall, "fpr": fpr, "precision": precision, "f1": f1})

    high = max((r for r in table if r["recall"] >= 0.95), key=lambda r: r["t"], default=table[0])
    medium = max(table, key=lambda r: r["f1"])
    low = min((r for r in table if r["fpr"] <= 0.005), key=lambda r: r["t"], default=table[-1])
    picks = {"high": high, "medium": medium, "low": low}

    print(f"clips: {len(pos)} gunshot, {len(neg)} other   |   onset gate passes {sum(r['gate'] for r in pos) / len(pos):.0%} of gunshots, "
          f"{sum(r['gate'] for r in neg) / len(neg):.0%} of other sounds (= how often the model would run)\n")
    print(f"{'threshold':>9} {'recall':>7} {'false pins':>11} {'precision':>10} {'F1':>6}")
    for r in table:
        mark = " <- " + ", ".join(k for k, v in picks.items() if v is r) if any(v is r for v in picks.values()) else ""
        print(f"{r['t']:9.3f} {r['recall']:7.1%} {r['fpr']:11.2%} {r['precision']:10.1%} {r['f1']:6.3f}{mark}")

    print("\nfalse pins per non-gunshot category at the Medium threshold (worst first):")
    cats: dict[str, list] = {}
    for r in neg:
        cats.setdefault(r["cat"], []).append(fires(r, medium["t"]))
    for cat, hits in sorted(cats.items(), key=lambda kv: -sum(kv[1]) / len(kv[1]))[:12]:
        print(f"  {cat:32s} {sum(hits):3d} / {len(hits):3d}")

    args.out.write_text(json.dumps({
        "clips": {"gunshot": len(pos), "other": len(neg)},
        "thresholds": {k: v["t"] for k, v in picks.items()},
        "at_threshold": picks,
        "table": table,
    }, indent=2), encoding="utf-8")
    print(f"\nthresholds -> GunshotCalibration in gunshot_settings.dart: high={high['t']} medium={medium['t']} low={low['t']}")
    print(f"written {args.out}")


if __name__ == "__main__":
    main()
