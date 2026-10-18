# Decisions needed

These came out of the end-to-end test loop on staging. I can't (or shouldn't) decide them on my own, so I've carried on with everything else.

Each item lists what I would recommend. Reply with the number and your choice (e.g. "D2: option B").

*Last updated: workstreams A–C (13 Oct 2026).*

---

### D1. HTTPS: partly there (checked 13 Oct)
- **Works:** `https://mikpunt.ddns.net` has a valid certificate, is reachable from the internet and forwards to the **MobileAPI**. That's the release app's API address, so a release APK can now sign in and work. The login lockout works through it too.
- **Still missing:** the proxy has **no certificate for `webapp`, `webapi` and `mobileapi.mikpunt.ddns.net`** ("unrecognized name" on 443); they only work over plain HTTP inside NetBird. Consequences:
  - the website has no HTTPS;
  - the app's "sign in / register on the website" links (`https://webapi.mikpunt.ddns.net/...`) fail in a release build;
  - the YouTube OAuth callback (`Youtube__RedirectUri=https://mobileapi.mikpunt.ddns.net/...`) can't complete, see D3.
- **Action (whoever manages the proxy 10.0.0.47):** add Let's Encrypt certificates for those three names, or point the app/website links at a name that has one.
- **Note:** the MobileAPI is now reachable from the **public internet**, not only via NetBird. The new login lockout and rate limits matter there.

### D2. PayFast can't confirm payments on staging
- **Problem:** PayFast confirms a payment by calling our `notify_url` from the internet. Staging is only reachable over NetBird, so no payment can ever become "Paid" automatically.
  - Since iteration 1 the **checkout** works (the return URL is no longer refused); only the confirmation is missing.
- **Options:**
  - **A:** expose `/api/payfast/itn` publicly (port forward or tunnel) and set `PayFast__NotifyUrl`.
  - **B (recommended for staging):** turn on the existing payment **simulator** on staging (`PayFast:EnableSimulator`; it now only works in Development). Testers then click "pay" and the booking becomes Paid.
  - **C:** leave it; an admin marks bookings Paid by hand (works now).

### D3. Reconnect the YouTube channel
- **Problem:** the channel was connected on 11 Oct at 12:45, before the MobileAPI's key folder became a persistent volume (17:31 the same day). The key that encrypted the connection was lost when the container was recreated, so every publish failed with "key not found in the key ring".
- **Done (13 Oct):** the old connection is marked Revoked, and the MobileAPI now handles this case once with a clear message instead of retrying with stack traces. The key folder is a volume, so a new connection survives deploys.
- **Why it's yours:** reconnecting needs the channel owner's Google login. It also needs D1 (the callback URL is `https`).
- **Action:** either get a certificate for `mobileapi.mikpunt.ddns.net` (D1), or change `Youtube__RedirectUri` in `/opt/project/.env` to `https://mikpunt.ddns.net/api/admin/youtube/oauth/callback` **and** add that exact URL under *Authorized redirect URIs* of the OAuth client in Google Cloud Console. Then an admin calls `.../api/admin/youtube/oauth/start` and completes the Google login.

### D4. Remove the remaining test data completely?
- **Problem:** 7 suspended test accounts (`*@staging-e2e.test`, userIds 53–59) remain, plus 5 ratings, 8 bookings, 3 packages and 1 farm (all Inactive).
  - They can't be deleted because `ratingHistory` and `documentAuditLog` are append-only (delete triggers).
  - Each new test iteration adds a few more test accounts.
- **Options:**
  - **A (recommended):** leave them; they're suspended and hidden.
  - **B:** let me temporarily disable the two `*_NoDelete` triggers on staging, purge the test data and turn them back on (bypasses an audit protection for a moment).

### D5. Role-less seed accounts (duplicates: done)
- **Done (13 Oct, workstream C):** the duplicate users 11–20, the 14 duplicate role rows and the 8 duplicate document types are removed by the migration `DedupeSeedDuplicates.sql`. Unique indexes on e-mail, (user, role) and document type name now stop it happening again.
- **Still your call:** the documented test accounts `jagter@test.com`, `boer@`, `ph@`, `pj@`, `vg@`, `admin@test.com` have **no roles**. Their password (`password123`) is in the repo, so giving `admin@test.com` the admin role would create an admin with a public password.
  - **A (recommended):** give only the 5 non-admin accounts their roles; drop `admin@test.com`.
  - **B:** leave them; the e2e suite creates its own accounts each run.

### D6. Cancelling approved or paid bookings
- **Done in iteration 1:** a hunter can now cancel their own booking while it's still a request (Pending / waiting for staff / rejected); status becomes "Cancelled".
- **Your call:** what should happen after approval or payment (refund rules, who may cancel, by when)? For now it returns: *"Kontak asseblief die uitruster (PJ)."* (Please contact the outfitter.)

### D7. Which compose file is the source of truth?
- **Problem:** the server runs `/opt/project/docker-compose.prod.yml`, which differs from the repo's `docker-compose.prod-local.yml` (different service names, ports and env lists, no `ai_models` volume). Changes in the repo don't reach the server.
  - In iteration 1 I edited the **server file** directly; backups are `*.bak-20261012-224426`.
- **Recommendation:** commit the server file into the repo as `docker-compose.prod.yml` and let CI copy it to `/opt/project`. Then it's version-controlled.

### D8. Firearm licences and personal permits: only one per type
- **Problem:** weapons and personal permits are stored in `userDocumentation`, which allows only one active document per type per role. A hunter with **two** rifles can't register the second one; since iteration 1 they get a clear 409 instead of a 500.
- **Options:**
  - **A (recommended):** move these to the `permits` wallet table (created in iteration 1), which allows any number.
  - **B:** leave it as-is.

### D9. Currency: done (13 Oct), one setting to check
- **Found:** outfitters confirm a booking in **US$ or rand** (it's stored with the booking), but the hunter's pages always showed "$ … USD", and **PayFast charged the US$ number as rand** (a US$ 4 500 quote was charged R 4 500).
- **Fixed:**
  - every booking amount now shows in the currency it was quoted in;
  - US$ amounts show "≈ R …" for local hunters;
  - PayFast is charged in rand at the new admin setting **`UsdToZarRate`** (default 18.00; Admin → Settings). The PayFast item description shows the US$ amount and rate.
- **Your call:** set `UsdToZarRate` to the rate you want, and decide how often someone updates it.

### D13. Switch on WebSockets in the proxy for webapp.mikpunt.ddns.net (urgent)
- **Problem:** the proxy (10.0.0.47) doesn't pass WebSockets through for the website. Blazor then falls back to long polling: the site works, but every page takes several seconds to become interactive and feels sluggish. The browser tests time out through the proxy, while the same tests passed 88/88 through the direct IP.
- **Why it matters now:** since 13 Oct the containers only accept their own names (see below), so everyone has to go through the proxy.
- **Action (proxy admin):** in Nginx Proxy Manager, open the proxy host `webapp.mikpunt.ddns.net` and switch on **"Websockets Support"**. Do the same for `webapi.mikpunt.ddns.net` (chat hub). Nothing changes on our side.
- **Emergency workaround, if the demo is before the proxy is fixed:** on the docker host, add `;10.0.0.51:8080` to `AllowedHosts` of the `webapp` service in `/opt/project/docker-compose.prod.yml`, then run `docker compose -f docker-compose.prod.yml up -d webapp`. Then `http://10.0.0.51:8080` works again with WebSockets.

### D14. Direct IP access is now refused (done 13 Oct)
- **Found:** the website API received 818 requests on `10.0.0.51:8081` in a day. All came from our own test runs and the CI smoke test; real users only use the names or the containers' internal calls.
- **Done:**
  - each container only accepts its own names (`AllowedHosts` in the server's compose file; backup `docker-compose.prod.yml.bak-*`), so `10.0.0.51:8080-8082` now answers **400**;
  - tests and CI go through the proxy names;
  - the CI runner maps those names to the proxy (`extra_hosts`, backup `config.toml.bak-*`).
- **Note:** this is host-name hygiene, not a firewall. Anyone can still reach the ports and fake the name. A firewall rule that only lets the proxy reach 8080-8082 would close that, but it needs to know whether NetBird traffic arrives as the proxy's address. Check with the proxy admin first.

### D10. Revoke the e-mail password that is in git history (urgent)
- **Problem:** a real SMTP app password was committed to `.env.example` on 7 Aug (commit `1a12f65`). It's in the history of `main_dev` and other branches. It isn't the value the server uses now, but Google may still accept it.
- **Action (account owner):** in the Google account that sends Mikpunt mail → Security → App passwords, revoke every old app password. If the server's current one is in the same account, create a new one and put it in `/opt/project/.env` (`EmailSettings__SenderPassword`). Removing it from git history isn't worth it (forks/clones keep it); revoking makes it useless.

### D11. Switch on the CI checks that need GitLab settings (Maintainer)
- **Action:**
  - Settings → CI/CD → Variables: add **masked** `E2E_ADMIN_EMAIL`, `E2E_ADMIN_PASSWORD`, `E2E_SQL_SERVER` (`10.0.0.41,1433`), `E2E_SQL_USER`, `E2E_SQL_PASSWORD`. The smoke test also uses the admin pair.
  - CI/CD → Schedules: new schedule, nightly (e.g. 03:00), branch `main_dev`. This runs the full e2e regression.
- **Why it's yours:** I have no GitLab API access. Everything else for workstream B is in place.
- **Recommendation for the SQL login:** don't use a personal login there; create a separate SQL login with only the rights the tests need.

### D12. Off-site copy of the nightly backups
- **Problem:** the nightly backups (7 rolling days) sit on the SQL server's own disk. If that disk or VM is lost, so are the backups.
- **Action:** someone with access to 10.0.0.41 syncs `/var/opt/mssql/data/backups` to another machine (rsync cron or a mounted share).

---

## Already done by me (no decision needed)
- **Staging server secrets:** JWT keys (both APIs), `InternalApi__Key` and the webapp addresses are now set. Forged tokens are refused (401), and the VG media / app ratings no longer return 503 after the deploy.
- **Applied manually on the staging DB:** `AddDocumentVerification.sql` (is now also in the startup migrations).
