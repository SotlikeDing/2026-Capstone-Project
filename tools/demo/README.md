# MikPunt 13-minute demo data

This directory implements the repeatable staging data set described in
`demo_data_plan.md`. Every account ends in `@demo.mikpunt.co.za`; SQL cleanup and
reset predicates are scoped to that domain or to IDs stored in
`dbo.demoSeedRegistry`.

The tool deliberately separates three concerns:

- `seed_demo.py` registers genuine accounts through WebAPI, then adds deterministic
  historical/volume data with parameterised SQL. Core credential documents are
  uploaded through the real document API.
- `reset_demo.py` restores only rehearsal-mutated state and revokes demo sessions.
  It never deletes from `ratingHistory`, `documentAuditLog`, or `adminAuditLog`.
- `check_demo.py` prints a pass/fail line for each demo segment and returns a non-zero
  exit code when anything is missing.

## Prerequisites and configuration

Apply the normal WebAPI migrations and start WebAPI before the first seed. Install
the one Python dependency:

```powershell
python -m pip install -r tools/demo/requirements.txt
```

Set the same environment used by E2E/staging. Secrets must remain outside Git:

```powershell
$env:DEMO_PASSWORD = '<12+ chars, upper/lower/digit/special>'
$env:E2E_WEB_API_URL = 'https://staging-api.example'
$env:E2E_MOBILE_API_URL = 'https://staging-mobile-api.example'
$env:E2E_SQL_CONNECTION_STRING = 'DRIVER={ODBC Driver 18 for SQL Server};SERVER=...;DATABASE=Mikpunt;UID=...;PWD=...;Encrypt=yes;TrustServerCertificate=yes;'
```

Instead of `E2E_SQL_CONNECTION_STRING`, the scripts also accept
`E2E_SQL_SERVER`, `E2E_SQL_DATABASE`, `E2E_SQL_USER`, `E2E_SQL_PASSWORD`, and
`E2E_SQL_DRIVER`. Local development defaults to `(localdb)\Mikpunt` / `Mikpunt`.
For a self-signed staging certificate only, explicitly set
`E2E_ALLOW_INSECURE_TLS=true`.

## Commands

Review the deterministic plan without connecting to anything:

```powershell
python tools/demo/seed_demo.py --demo-date 2026-10-20 --dry-run
```

Build or update the data set (idempotent):

```powershell
python tools/demo/seed_demo.py --demo-date 2026-10-20
```

The seed hides obvious legacy `E2E`, `Toets`, and `@test.com` farms/packages rather
than deleting them. Pass `--keep-legacy-visible` to skip that step. The
`--skip-document-api` switch is diagnostic only; a presentation-ready seed should
not use it.

Before every rehearsal:

```powershell
python tools/demo/reset_demo.py --demo-date 2026-10-20
python tools/demo/check_demo.py --demo-date 2026-10-20 --api
```

The demo date must be the same for seed, reset, and pre-flight. If it changes, rerun
the seed; registry-backed rows are updated to the new relative dates.

## Media

See `assets/README.md` and populate `assets/manifest.json` source/licence notes.
Local asset readiness can be checked without SQL:

```powershell
python tools/demo/check_demo.py --assets
```

When all 12 hunt photos are present, the seed signs in as Johan and Thabo, uploads
them through MobileAPI with route-matching time/GPS metadata, selects five for the
20-25 second story, requests the story through the recap API, and waits for the
FFmpeg worker. `DEMO_RECAP_WAIT_SECONDS` controls that wait (default: 180).

MobileAPI supports both photo uploads and VG MP4 video clips. When the two short
VG video clips are present, the seed signs in as Vusi and uploads them through
MobileAPI into bookingMedia with sourceType='Video'.

## Currency and narrative

All seeded amounts are South African rand. The presentation should use the
"what we found -> what we fixed" narrative and the latest study close of 78% easy,
0 blocked. Some existing WebApp screens still label booking totals as USD; resolve
that product decision before presenting rather than treating seeded ZAR values as
dollars.
