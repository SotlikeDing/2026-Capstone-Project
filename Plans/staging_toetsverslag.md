# Staging test report — what doesn't work (12–13 Oct 2026)

**Environment:** staging, `10.0.0.51` (Docker: `webapi`, `mobileapi-prod`, `webapp`, deployed from `main_dev` `86b07b95`) + SQL Server `10.0.0.41` (`Mikpunt`).
**Method:** 1 087 automated checks against the live servers:

- a sweep of all 279 API endpoints (anonymous + low-privilege user)
- full role journeys over HTTP (registration → documents → farm/package → booking → staff → payment → chat → team → permits → trophies → media/story → ratings → admin)
- headless Chrome over 88 webapp pages for every role
- server-side ffmpeg/ffprobe, proxy/TLS and log checks

Test accounts were registered through the real API (`*@staging-e2e.test`).

Only the problems are listed here. Everything not listed passed (see *What works* at the end).

Severity: 🔴 critical (security / a whole feature down) · 🟠 high · 🟡 medium · ⚪ low

---

## 🔴 Critical

### C1. Anyone can forge an admin login (JWT keys not configured)
- **Evidence:** a token signed with the placeholder key from the public repo (`appsettings.json` → `Jwt:Key = "DEV-ONLY-PLACEHOLDER-..."`) was accepted as **admin** by staging WebAPI (`GET /api/admin/audit-logs` and `/api/admin/settings` → 200). MobileAPI also accepts tokens signed with its placeholder `CHANGE-THIS-TO-A-LONG-RANDOM-SECRET-AT-LEAST-32-CHARS`.
- **Cause:** `/opt/project/.env` has no `Jwt__Key` / `Jwt__SecretKey`. The `webapi` service in `docker-compose.prod.yml` only passes an explicit `environment:` list, so a new key would also have to be added there.
- **Fix:**
  1. Generate two random 48+ character keys and put them in `.env` (`Jwt__Key`, `Jwt__SecretKey`).
  2. Add `- Jwt__Key=${Jwt__Key}` to the `webapi` environment list.
  3. Run `docker compose -f docker-compose.prod.yml up -d`.
  4. Code: make both APIs refuse to start in Production while the key is a placeholder.

### C2. Refresh token returns a token for *someone else* (user 1)
- **Evidence:** `POST /api/user/refreshToken` with a valid refresh token for user 53 returned an access token with `nameid = 1` (johan@mikpunt.com, HUNTER/PH).
- **Cause:** `UserController.RefreshToken` builds `new User { Email = ..., UserId = 1 }` and signs that (the comment even says so).
- **Fix:** look the user up by email (`GetUserByEmail`) and sign a token with their real id and roles.

### C3. Document upload was broken: no new PJ/PH/VG/farmer could ever become visible
- **Evidence:** every `POST /api/documents/{userRoleId}/upload` → 500 `Invalid column name 'udVerifiedBy' / 'udVerifiedAt' / 'udRejectionReason'`; `GET /api/admin/document-verifications` → 500.
  - Because packages, farms and PH/VG listings only show once documents are approved, **nothing new could be published or booked**.
- **Cause:** `db/AddDocumentVerification.sql` (commit `47b8e0f`) is **not** in `DatabaseBootstrapper.MigrationScriptOrder` or `WebAPI.csproj`, so it never ran on staging. Local LocalDB only works because it was once run by hand.
- **Done on staging (12 Oct):** I ran the script manually (it's idempotent and additive). Afterwards all 48 document checks passed.
- **Still needed in code:** add `db.AddDocumentVerification.sql` to the csproj `EmbeddedResource`s and to `MigrationScriptOrder` (after `AddUserDocumentManagement.sql`). Otherwise every new database will be broken again.

### C4. SAPS 520 forms are saved under the wrong person (privacy leak) and a second submission crashes
- **Evidence:**
  - Hunter user 53's SAPS form was stored as `userDocumentation.udUserRole = 53`. userRole 53 belongs to **toets@helgard.com (AP)**, so another user's profile receives the hunter's passport and firearm details.
  - A second submission → 500 `UQ_userDocumentation_ActiveRoleType` (duplicate key).
  - `POST /api/Permit/createUserPermit` → the same 500.
- **Cause:**
  - `HuntingPermit_db.SaveSapsForm(userId, ...)` writes the **userId** into the `udUserRole` column.
  - SAPS forms and permits are stored as document type 1 (**"ID Dokument"**). The unique index (one active document per role + type) then allows only one.
- **Fix:**
  - Resolve the hunter's `userRoleId` before inserting.
  - Give SAPS 520 and permits their own document type, or a separate table.
  - Move the existing misfiled rows to the right role.
  - Refuse an incomplete SAPS form (see M7).

### C5. Paying is impossible on staging
- **Evidence:**
  - In the webapp, clicking pay on an approved booking shows **"Ongeldige terugkeer-adres."** (invalid return address). The API: `POST /api/Payments/checkout` → 400 for any staging URL.
  - The PayFast form also contains `notify_url = http://10.0.0.51:8081/api/payfast/itn`, a private IP that PayFast's servers can never reach, so a payment would never be confirmed.
- **Cause:**
  - `WebAppOrigins.All` only knows localhost + `webapp.capstone.server`.
  - `WebAppBaseUrl` isn't set on staging (default `https://localhost:7285`).
  - `PayFast:NotifyUrl` isn't set.
  - The email links (verify / reset) use the same `WebAppBaseUrl`, so they point to localhost.
- **Fix:** set `WebAppBaseUrl=https://webapp.mikpunt.ddns.net` (or the real public address) and `PayFast__NotifyUrl=https://<public webapi>/api/payfast/itn` in `.env`, and pass them in the compose file. The notify URL must be reachable from the internet.

### C6. A release build of the app can't reach the server (HTTPS broken)
- **Evidence:**
  - All `*.mikpunt.ddns.net` names resolve to the proxy `10.0.0.47`. HTTP works, but **HTTPS fails the handshake** (`UNEXPECTED_EOF`).
  - In release mode the Flutter app uses `https://mikpunt.ddns.net` and `https://webapi.mikpunt.ddns.net` (`config/app_config.dart`), so every API call fails.
  - The YouTube OAuth redirect URI is also `https://mobileapi.mikpunt.ddns.net/...`.
- **Works:** debug builds (`http://mobileapi…`): login and 4.7 MB / 14 MB uploads through the proxy all succeeded.
- **Fix:** put a valid certificate on the proxy (e.g. Let's Encrypt for the ddns names), or build the APK with `--dart-define=API_BASE_URL=http://mobileapi.mikpunt.ddns.net` for the demo.

---

## 🟠 High

### H1. Logout doesn't log anyone out
- **Evidence:** `POST /api/user/logout` replies "Token revoked", but the same token still works (200).
- **Cause:** `Program.cs` → `OnTokenValidated` casts `context.SecurityToken as JwtSecurityToken`. In .NET 8 the handler produces a `JsonWebToken`, so the cast is `null` and the blacklist is never checked. The webapp's `AuthService.LogoutAsync` also never calls the API.
- **Fix:** read the raw token through `JsonWebToken.EncodedToken` (or `context.Request.Headers.Authorization`), and call `api/user/logout` from the webapp.

### H2. A suspended user keeps working until their token expires (24 h)
- **Evidence:** after `POST /api/admin/users/{id}/status` → Suspended, new logins are refused, but the existing token still returns 200.
- **Fix:** check `uIsSuspended` in `OnTokenValidated` (cached), or revoke the user's tokens on suspension.

### H3. Two public lists crash entirely on a single bad row
- **Evidence:** `GET /api/farmer/getAllFarms` → 500 and `GET /api/Ads/getAds` → 500 (`SqlNullValueException`). Both for everyone, anonymous included.
- **Cause:**
  - Farm 1 ("Bennie se Bosveld") has no address, and `Farmer_db.GetAllFarms` calls `GetString()` on the NULL city/province.
  - Ad 1 has NULL `adApId`, `adViews` and `adClicks`, and `Ads_db.GetAds` calls `GetInt32()` on them.
- **Fix:** guard with `IsDBNull` in the readers, and fix or delete the two bad dummy rows. The webapp's search pages use other queries, so they don't show the problem, but any other consumer of these endpoints breaks.

### H4. New features not switched on: website VG media and app team ratings → 503
- **Evidence:** `api/vg/bookings/*` → 503 "Media-oplaai is nog nie … opgestel nie" (shown in the VG panel); `api/bookings/{id}/team-ratings` (app) → 503.
- **Fix:**
  1. Put one shared `InternalApi__Key` (32+ chars) in `.env`.
  2. Add `- InternalApi__Key=${InternalApi__Key}` and `- InternalApi__MobileApiBaseUrl=http://mobileapi:80` to the `webapi` environment list.
  3. Add `InternalApi__WebApiBaseUrl=http://webapi:80` to `.env.prod` for mobileapi.

### H5. YouTube publishing fails
- **Evidence:** the publish job → `RetryableFailure: The key {95b1434e…} was not found in the key ring`.
- **Cause:** the stored channel connection was encrypted with a DataProtection key from before `/data/youtube-keys` was a persistent volume. The volume now holds only a newer key (created 12 Oct 09:59).
- **Fix:** an admin reconnects the channel (`GET /api/admin/youtube/oauth/start` → Google). This also needs HTTPS on the proxy (C6), because the redirect URI is `https`.

### H6. Bookings with impossible dates are accepted
- **Evidence:** `POST /api/bookings/create` with dates in the past → 200; with an end date before the start date → 200.
- **Fix:** validate `StartDate >= today` and `EndDate >= StartDate` (as the mobile route validation already does).

### H7. Email verification can be guessed and disappears on every deploy
- **Evidence:** 30 wrong codes in a row → only 400s, never 429.
- **Cause:**
  - The 6-digit code comes from `new Random()`.
  - It lives in a static, non-thread-safe `Dictionary` in memory. On every container restart (= every deploy) all pending codes are lost, and users get "invalid code".
- **Fix:** store codes in the DB (`AuthTokens`), use `RandomNumberGenerator`, and lock an email after ~5 failed attempts.

### H8. Cancelling a booking that has staff → 500
- **Evidence:** `DELETE /api/bookings/deleteBooking/{id}/{userId}` → 500 `FK__bookingSt__booki…` (`bookingStaffAssignments`).
- **Fix:** delete or close the assignments first, or rather use a soft "Cancelled" status.

### H9. The permits wallet in the webapp is broken
- **Evidence:** `GET /api/Permits/{userId}` → 500 `Invalid object name 'permits'`, and the webapp calls it (`MikpuntApiService.cs:1584`).
- **Fix:** create the table (there's a loose `create_permits.sql` in the root that isn't in the bootstrapper), or remove the endpoint and the call.

---

## 🟡 Medium

### M1. An admin can suspend themselves
`POST /api/admin/users/{ownId}/status` → 200. The test reverted it immediately. Refuse `id == caller`.

### M2. Error responses show internal details
500 responses contain the SQL message and exception type (e.g. `"type":"SqlException"`, table names). Return a generic message in Production and log the details.

### M3. Poor dummy data
- **Seed test users have no roles:** `jagter@test.com`, `boer@`, `ph@`, `pj@`, `vg@`, `admin@test.com` have no `userRoles`, so the documented test accounts can't be used. The app refuses them all.
- **Duplicate emails:** 10 emails exist twice (users 1–10 and 11–20). Users 1–10 also have every role twice. `users.uEmail` has no unique constraint.
- **Provinces 3×:** 27 rows, so every province dropdown shows each one three times.
- **Duplicate document types:** 8 `documentType` names are duplicated.

### M4. "Completed trips" is always 0, and the VG sees "Beplan" for finished trips
`profile-stats.completedTrips` only counts `bookStatus = 'Completed'`, and nothing in the system sets that. The VG bookings page shows "Beplan" (planned) for a paid trip that's already over. Count trips that are past their end date, as `TripLifecycle` / `IsRateable` already do.

### M5. A team invite drops the expedition name and terms
`SendInviteRequest.ExpeditionName/Terms` are accepted, but `teamInvites` has no columns for them, so the PH never sees the offer.

### M6. The booking audit trail is incomplete or misleading
- An admin booking correction doesn't appear in `getAuditLogs/{id}` (only in the admin audit log).
- A manual "Paid" by the admin is recorded as "Betalingstelsel (PayFast)".

### M7. SAPS 520 isn't validated
`submitSaps520` saves any JSON (`{"rubbish":true}` was accepted). The `SapsForm` validation rules (ID or passport, etc.) are never run.

### M8. 500 instead of 400/404 on invalid input
- `POST /api/bookings/create` with an empty body → 500 (SqlDateTime overflow).
- `POST /api/Chats/direct/{unknown user}` → 500 (FK error).

### M9. Configuration drift and persistence on the server
- **Compose drift:** the server's `docker-compose.prod.yml` doesn't match the repo's `docker-compose.prod-local.yml` (different service names and ports, no `ai_models` volume, explicit env lists). Changes in the repo (e.g. `InternalApi__*`) therefore don't reach the server.
- **Lost keys:** webapi and webapp store DataProtection keys inside the container (log warning), so they're lost on every deploy.
- **Lost models:** retrained AI price models aren't kept in a volume.
- **Typo:** `Kestrel_EndpointsHttp_Url` should be `Kestrel__Endpoints__Http__Url` (harmless).

### M10. Everything runs over plain HTTP
Logins and JWTs travel unencrypted between the proxy and the browser/app (see C6).

---

## ⚪ Low / observations
- The marketplace shows only **1 package per page** (pages 1–4 for 4 packages).
- Documents are stored as base64 data URLs in SQL Server; that will grow quickly with real PDFs.
- `GET /api/profile/getProfile/{id}` is public. ID/passport, email and phone are hidden correctly, but name and roles are public (probably intended).

---

## What I changed on staging
1. **Ran `db/AddDocumentVerification.sql`** (12 Oct): added `udVerifiedBy`, `udVerifiedAt` and `udRejectionReason`, and set old 'Active' documents to 'Approved'. This is what C3 needs; it hasn't been changed in code yet.
2. **Test data:**
   - **Removed:** everything the 7 test accounts created (ads, media files, stories, chats, team rows, invites, routes, media in the Docker volumes, trophy photos).
   - **Kept but hidden:** what the append-only audit tables (`ratingHistory`, `documentAuditLog`) protect — 5 ratings, 8 bookings, 3 packages, 1 farm and the 7 accounts. The accounts are **suspended**, packages and farm are `Inactive`, and the trophy is private.
   - **Still there:** the misfiled SAPS row from C4 (`userDocumentation.udId = 2`, on toets@helgard.com's AP role) also stays, because it's linked to the audit log.
   - **To remove completely:** temporarily disable the two `*_NoDelete` triggers, or leave it as-is.
3. Admin user 42 was briefly suspended by the self-suspend test (M1) and **immediately reinstated**. A feature flag and a setting were changed and restored.

---

## What works (all passed)
- **Registration and sign-in:** registration with every role, plus the validations (duplicate email incl. case, weak password, admin role, terms); sign-in/out on the web; mobile sign-in with role selection and capabilities; app token refresh.
- **Profile:** profile + every role registration/update, PH rates, subscription rules, and ownership checks (403) on all write endpoints.
- **Farms, packages, ads:** farm, animals, packages (CRUD + ownership); ads incl. "not live before payment" and admin payment recording.
- **Documents:** after the migration: upload, reject, replace, approve, audit trail, and gating of listings.
- **Bookings:** request, PJ assigns PH + VG, PH/VG accept/decline, PJ rejection with reason, extra hunters (roster without emails, limit), admin payment status, quote/checkout logic and the open-redirect guard.
- **Chat:** direct and group chats, the app ↔ website link, keyword moderation, read status, deleting your own message, real-time hub (SignalR).
- **PJ teams and AI helper:** team invites → accept → team, notifications; AI price advice and team matches.
- **Trophies:** Rowland Ward search/match/evaluate; trophy submission → PH correction → resubmit → approval (idempotent) → trophy room; public/private photos.
- **Media and stories:** farm rating; GPS route + PH selection; photo + VG video upload with server ffprobe; PH exclusion; **the story rendered on the server** (22 s, H.264 1280×720, no audio, clean decode).
- **Ratings:** ratings + moderation + dispute + admin upheld (excluded from the average).
- **Admin console:** every admin console tab and endpoint.
- **Webapp:** 86 of 88 pages per role with no errors (the other 2 are deliberate: login required / AI helper not for hunters).
- **Email:** sending through Gmail SMTP works.
