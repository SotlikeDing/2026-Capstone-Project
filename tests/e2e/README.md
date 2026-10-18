# End-to-end tests

About 1 000 checks that drive the deployed system the way users do: every role registers, signs in,
builds farms and packages, books, pays, chats, rates, uploads documents and media, and every
endpoint and web page is visited. They run against a real deployment (staging by default), never
against production data you care about.

| Suite | What it covers |
|---|---|
| `smoke.py` | Post-deploy check (CI runs it after every deploy): services up, DB healthy, security headers, auth enforced, forged tokens refused. No credentials needed. |
| `t0_accounts` | Registration for every role, duplicate/weak/admin/no-terms refusals, verification, sign-in |
| `t1_sweep` | Every endpoint of both APIs (from `inventory.py`): no 5xx, auth enforced |
| `t2_profile` | Profiles, roles, password change, brute-force lockout |
| `t3_market` | Farms, packages, marketplace, ads, AI price advice |
| `t4_documents` | Document upload, admin verification, visibility rules |
| `t5_bookings` | Booking life cycle, validation, cancellation, payments checkout |
| `t6_social` | Direct and group chat, moderation, team invites |
| `t7_admin` | Admin console: audit log, flags, settings, suspension, corrections, AI admin |
| `t8_mobile` | Mobile API: bookings, GPS routes, media, recaps, trophies, permits |
| `t9_ratings` | Post-trip ratings, disputes, moderation |
| `t10_ui` / `t11_ui_flows` | Every web page per role in headless Chrome, plus key flows |

## Running

```bash
pip install -r requirements.txt            # + `playwright install chromium` for the UI suites
export E2E_ADMIN_EMAIL=...                 # an admin account on the target
export E2E_ADMIN_PASSWORD=...
export E2E_SQL_SERVER="10.0.0.41,1433" E2E_SQL_USER=... E2E_SQL_PASSWORD=...
python smoke.py                            # quick
python run_all.py                          # API suites, then cleanup
python run_all.py --ui                     # + browser suites
```

Defaults point at staging through the proxy names (`webapi/webapp/mobileapi.mikpunt.ddns.net`, NetBird needed); the containers refuse direct `IP:port` requests. `E2E_WEB_URL`, `E2E_MOBILE_URL`, `E2E_APP_URL` override them.
Never commit credentials: CI reads them from masked CI/CD variables.

Each run creates its own accounts (`<role>-<run>@staging-e2e.test`, random password) and
`cleanup.py` removes what it can afterwards. Rows held by the append-only audit tables
(`ratingHistory`, `documentAuditLog`, `adminAuditLog`) can't be deleted by design, so those test
accounts are suspended and their packages/farms set Inactive instead.

Outputs (state, per-check results, logs, screenshots) go to `out/` (git-ignored).

## In CI

- `smoke-test` runs after every deploy of `main`/`main_dev`.
- `e2e-full` runs from a pipeline schedule (CI/CD → Schedules, e.g. nightly on `main_dev`) with
  the masked variables `E2E_ADMIN_EMAIL`, `E2E_ADMIN_PASSWORD`, `E2E_SQL_SERVER`, `E2E_SQL_USER`,
  `E2E_SQL_PASSWORD`. Results and screenshots are kept as job artifacts.
