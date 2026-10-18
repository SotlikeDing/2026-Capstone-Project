# Operations runbook

How the hosted system is deployed, checked, backed up and restored. Hostnames and the people who hold
the passwords are in the team's private notes. **This file contains no secrets** and must stay that way.

## Topology

| Piece | Where | Notes |
|---|---|---|
| WebAPI (ASP.NET Core 8) | docker host, container `webapi`, port 8081 | Runs the DB migrations on start |
| MobileAPI (ASP.NET Core 8) | docker host, container `mobileapi-prod`, port 8082 | App sign-in, media, recaps, YouTube |
| WebApp (Blazor Server) | docker host, container `webapp`, port 8080 | Website |
| SQL Server 2022 | separate DB host, port 1433 | Database `Mikpunt`, app login `mikpunt_app` (db_owner on Mikpunt only) |
| GitLab runner | docker host, container `gitlab-runner` | Docker executor, 1 job at a time |
| Reverse proxy | NetBird proxy host | `*.mikpunt.ddns.net`; HTTPS still to be set up (decision D1) |

Each container only answers to its own names (`AllowedHosts` in `docker-compose.prod.yml`): `webapi` /
`webapi.mikpunt.ddns.net`, `mobileapi` / `mobileapi.mikpunt.ddns.net` / `mikpunt.ddns.net`, `webapp.mikpunt.ddns.net`.
A request straight to `10.0.0.51:8080-8082` gets **400 Invalid Hostname**: go through the proxy names. The CI runner
maps those names to the proxy (`extra_hosts` in `/srv/gitlab-runner/config/config.toml`).

Configuration and secrets live only in `/opt/project/.env` on the docker host (owner-only readable).
`docker restart` does **not** re-read it; use `docker compose -f docker-compose.prod.yml up -d <service>`.

## Pipeline (`.gitlab-ci.yml`)

| Stage | Job | When | What |
|---|---|---|---|
| test | `unit-tests` | every push / MR | builds the website, runs the WebAPI + MobileAPI unit tests, JUnit + coverage reports |
| test | `flutter-tests` | when `src/jagbestuur_app/**` changed | `flutter test` |
| build/push/deploy | `build-*`, `push`, `deploy` | `main`, `main_dev` | images tagged with the 8-char commit, `compose up -d` |
| verify | `smoke-test` | after every deploy | `tests/e2e/smoke.py`: up, DB healthy, headers, auth enforced |
| verify | `e2e-full` | pipeline schedule | the whole E2E regression incl. browser suites (see `tests/e2e/README.md`) |

A failing test stops the pipeline before anything is built. **Set up once** (Settings → CI/CD → Variables,
masked): `E2E_ADMIN_EMAIL`, `E2E_ADMIN_PASSWORD`, `E2E_SQL_SERVER`, `E2E_SQL_USER`, `E2E_SQL_PASSWORD`; then
CI/CD → Schedules → nightly on `main_dev`.

### Rollback

Every deploy leaves its images on the host, tagged with the commit (`docker images | grep <service>`).

```bash
cd /opt/project
docker tag gitlab.akademia.ac.za:5050/rkw/nswiw370/2026/span2/webapi:<good-commit> webapi:latest   # per service
docker compose -f docker-compose.prod.yml up -d webapi
```

A schema change is not rolled back by this: migrations are forward-only and idempotent, and every
migration so far only adds. Restore a backup (below) if data must go back.

## Database migrations

`src/WebAPI/WebAPI/Services/DatabaseBootstrapper.cs` runs on every WebAPI start:

1. Empty server → `db/Complete_create.sql` creates the database.
2. Every script in `MigrationScriptOrder` that hasn't run in its current form (by SHA-256, recorded in
   `dbo.schemaVersions`) is applied, under an `sp_getapplock` so two instances can't race.
3. **A failing migration stops the start**: the container restarts, the smoke test goes red, and the log
   says which script failed (`docker logs webapi | grep "DB BOOTSTRAP"`). Fix the script and redeploy.
   Emergency only: `Database__ContinueOnMigrationFailure=true` in `.env` starts the app anyway.

Adding a migration: write an idempotent `db/<Name>.sql` (guards like `IF COL_LENGTH(...) IS NULL`), add it
to `WebAPI.csproj` as an `EmbeddedResource` and to the end of `MigrationScriptOrder`. The unit tests fail if
a script is embedded but not listed, listed but not embedded, or listed twice. A `USE <db>;` line is
fine for running a script by hand; the bootstrapper strips it so the app always migrates the database it is
configured for.

```sql
SELECT script, appliedAt, durationMs, runCount FROM dbo.schemaVersions ORDER BY appliedAt;  -- what ran when
```

**Hand-run SQL:** `users` has a filtered unique index (one account per e-mail), so every INSERT/UPDATE on
it needs `SET QUOTED_IDENTIFIER ON`. The apps and SSMS do that by default; `sqlcmd` does not - run it
with `-I` or start the script with `SET QUOTED_IDENTIFIER ON;`, otherwise you get error 1934.

## Backups

- **Schedule:** `/etc/cron.d/mikpunt-backup` on the docker host runs `/opt/project/scripts/backup-db.sh`
  (source: `scripts/ops/backup-db.sh`) daily at 02:15 UTC; log in `/var/log/mikpunt-backup.log`.
- **What:** a compressed full backup `WITH CHECKSUM`, written by SQL Server to
  `/var/opt/mssql/data/backups/Mikpunt_<Mon..Sun>.bak` on the DB host: seven rolling daily copies.
- **How it authenticates:** it reads the app's connection string from `/opt/project/.env` at run time and
  runs Microsoft's `mssql-tools` image, so no password is stored twice and nothing is installed on the host.
- **Limitation:** the files sit on the DB server's own disk. Protection against losing that disk needs an
  off-host copy (someone with access to the DB host should sync `/var/opt/mssql/data/backups` elsewhere).

### Restore drill (last run 2026-10-13: 7 s, row counts identical, `DBCC CHECKDB` clean)

Run as a sysadmin login. Restore to a *new* name first; never overwrite `Mikpunt` until the copy checks out.

```sql
RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/data/backups/Mikpunt_Tue.bak' WITH CHECKSUM;
RESTORE DATABASE Mikpunt_restoretest FROM DISK = N'/var/opt/mssql/data/backups/Mikpunt_Tue.bak'
  WITH MOVE 'Mikpunt' TO '/var/opt/mssql/data/Mikpunt_restoretest.mdf',
       MOVE 'Mikpunt_log' TO '/var/opt/mssql/data/Mikpunt_restoretest_log.ldf', RECOVERY, CHECKSUM;
DBCC CHECKDB('Mikpunt_restoretest') WITH NO_INFOMSGS;
-- compare: SELECT COUNT(*) FROM Mikpunt_restoretest.dbo.users / bookings / ratings ...
DROP DATABASE Mikpunt_restoretest;   -- when done
```

Real restore: stop `webapi` and `mobileapi-prod`, restore over `Mikpunt` `WITH REPLACE`, start them again.

## Secrets

| Secret | Lives in | Rotate by |
|---|---|---|
| DB password (`mikpunt_app`) | `.env` connection strings | `ALTER LOGIN mikpunt_app WITH PASSWORD = ...`, update `.env`, `compose up -d` |
| JWT signing keys (`Jwt__Key`, `Jwt__SecretKey`) | `.env` | new 64-char random values, `compose up -d` (everyone signs in again) |
| `InternalApi__Key` (WebAPI ↔ MobileAPI) | `.env`, same value for both | new random value, `compose up -d webapi mobileapi` |
| SMTP app password | `.env` | revoke in the Google account, create a new app password |
| LLM API key, YouTube OAuth client | `.env` | provider console |

Both APIs refuse to start outside Development with the public placeholder JWT keys from `appsettings.json`.
Never commit a real value, including in `.env.example` or docs.
