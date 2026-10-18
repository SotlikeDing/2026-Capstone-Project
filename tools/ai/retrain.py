"""One command for the price model's continuous-learning loop.

  1. asks the WebAPI to harvest the platform's current listings into observations          (POST api/aihelper/admin/harvest)
  2. downloads all observations as JSONL                                                     (GET  api/aihelper/admin/observations)
  3. retrains: bootstrap data + real observations (+ any hand-collected files), real share growing with the data
  4. with --upload:  ships the new bundle to the RUNNING (deployed) API's persistent model volume, where it is validated,
                     activated and survives restarts/redeploys - no rebuild needed                (POST api/aihelper/admin/upload-model)
     with --promote: installs it into the repo's src/WebAPI/.../AiModels (commit it so future images ship it) and hot-reloads
                     the API you are talking to                                                   (POST api/aihelper/admin/reload-models)
     Use --upload for a deployed server, --promote for local development; both together is fine.

  python retrain.py --api https://localhost:7038 --token <ADMIN JWT> --insecure --promote     # local dev
  python retrain.py --api http://<vm>:8081 --token <ADMIN JWT> --upload                       # deployed server

Also accepts --extra data/real/*.jsonl for hand-collected real prices (published price lists you typed in, quotes, invoices):
one JSON object per line  {"kind": "SPECIES_FEE", "raw": {...}, "target_usd": 3500, "source": "manual"}  - same schema as the
observations export; see README "Adding real data".
Read the printed "prior_error_on_real": it says how wrong the bootstrap assumptions were on YOUR data, per offer kind.
"""
from __future__ import annotations

import argparse
import datetime as dt
import os
import subprocess
import sys
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "llm"))
from advice_common import Api  # noqa: E402

ROOT = Path(__file__).resolve().parent


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--api", required=True, help="WebAPI base URL, e.g. https://localhost:7038")
    ap.add_argument("--token", default=os.environ.get("MIKPUNT_ADMIN_TOKEN"), help="Admin JWT (or env MIKPUNT_ADMIN_TOKEN)")
    ap.add_argument("--insecure", action="store_true", help="accept the self-signed localhost certificate")
    ap.add_argument("--extra", nargs="*", default=[], help="extra JSONL files of real observations")
    ap.add_argument("--promote", action="store_true", help="install into the repo's WebAPI AiModels folder and reload the API")
    ap.add_argument("--upload", action="store_true", help="upload the trained bundle to the API's persistent model volume (deployed servers)")
    args = ap.parse_args()
    if not args.token:
        raise SystemExit("Pass --token (an Admin JWT) or set MIKPUNT_ADMIN_TOKEN.")

    api = Api(args.api, args.token, args.insecure)
    print("harvest:", api._call("POST", "/api/aihelper/admin/harvest"))
    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M%S")
    obs = ROOT / "data" / "real" / f"observations-{stamp}.jsonl"
    obs.parent.mkdir(parents=True, exist_ok=True)
    obs.write_text(api.download("/api/aihelper/admin/observations"), encoding="utf-8")
    print(f"observations -> {obs}  ({sum(1 for _ in obs.open(encoding='utf-8'))} rows)")

    # Only the newest observation export is used (each export already contains ALL observations), plus any hand-collected files.
    cmd = [sys.executable, str(ROOT / "train_price_model.py"), "--real", str(obs), "--version", stamp, *args.extra]
    if args.promote:
        cmd.append("--promote")
    rc = subprocess.call(cmd, cwd=ROOT)
    if rc != 0:
        raise SystemExit("Training finished with failing sanity checks - NOT reloading. Inspect the output above.")
    if args.upload:
        bundle = ROOT / "out" / "models" / stamp
        zpath = ROOT / "out" / "models" / f"{stamp}.zip"
        with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
            for f in sorted(bundle.iterdir()):
                if f.is_file():
                    z.write(f, f.name)
        print("upload:", api._call("POST", "/api/aihelper/admin/upload-model", raw=zpath.read_bytes(), content_type="application/zip")[:400])
    if args.promote:
        print("reload:", api._call("POST", "/api/aihelper/admin/reload-models")[:300])


if __name__ == "__main__":
    main()
