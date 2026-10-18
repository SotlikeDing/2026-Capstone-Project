"""Run the end-to-end suites in order and summarise every failing check.

  python run_all.py            API suites t0-t9 (+ cleanup)
  python run_all.py --ui       also the browser suites t10/t11 (needs Playwright + Chrome)
Exit code 1 when any check failed, so CI marks the job red.
"""
import glob, json, os, subprocess, sys

from st import HERE

SRC = os.path.dirname(os.path.abspath(__file__))
SUITES = ["t0_accounts", "t1_sweep", "t2_profile", "t3_market", "t4_documents", "t5_bookings", "t6_social",
          "t7_admin", "t8_mobile", "t9_ratings"]
if "--ui" in sys.argv:
    SUITES += ["t10_ui", "t11_ui_flows"]

for f in glob.glob(os.path.join(HERE, "results_*.json")):
    os.remove(f)
env = {**os.environ, "PYTHONIOENCODING": "utf-8"}
subprocess.run([sys.executable, "inventory.py"], cwd=SRC, env=env, capture_output=True)
crashed = []
for suite in SUITES:
    print(f"--- {suite}", flush=True)
    p = subprocess.run([sys.executable, f"{suite}.py"], cwd=SRC, env=env, capture_output=True, text=True, encoding="utf-8", errors="replace")
    with open(os.path.join(HERE, f"{suite}.log"), "w", encoding="utf-8") as log:
        log.write(p.stdout + "\n--- stderr\n" + p.stderr)
    tail = [l for l in p.stdout.splitlines() if l.startswith("===")]
    if not tail:
        crashed.append(suite)
    print("   ", tail[-1] if tail else f"(crashed) {p.stderr.strip().splitlines()[-1] if p.stderr.strip() else ''}", flush=True)

if "--no-cleanup" not in sys.argv:
    subprocess.run([sys.executable, "cleanup.py"], cwd=SRC, env=env)

total, failed = 0, []
for f in sorted(glob.glob(os.path.join(HERE, "results_*.json"))):
    for area, name, ok, det in json.load(open(f, encoding="utf-8")):
        total += 1
        if not ok:
            failed.append((os.path.basename(f)[8:-5], area, name, det))
print(f"\n===== {total - len(failed)}/{total} checks passed{f', crashed: {crashed}' if crashed else ''} =====")
for suite, area, name, det in failed:
    print(f"FAIL {suite:14} [{area}] {name}  -> {det[:220]}")
sys.exit(1 if failed or crashed else 0)
