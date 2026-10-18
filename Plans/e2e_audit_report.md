# MikPunt end-to-end audit (2026-10-08/09)

**Status: complete for the WebApp + WebAPI.** Every WebApp page was opened as every role, access
between roles was checked, and the main business flows were run through the real API. The
Jagbestuur app on a phone was **not** tested (no device or emulator here).

## Summary

| Area | Result |
|---|---|
| Pages walked through (8 roles incl. signed-out) | **206 page visits** over all 59 routes; no crashes, error banners or console errors |
| Access between roles | **72 checks**, 67 correctly refused; the 5 "open" ones are `/pj/browse` (a shared directory page, by design) and `/jagter/bookings` (see #15) |
| Business flows through the API | **40 steps**: 36 passed. The booking chain (farm → package → documents → admin verification → booking → PJ assigns PH → PH accepts → paid) works end to end. Double payment is caught; outsiders are blocked. |
| Automated suites (commit `08d82a4`) | WebAPI 455/455, MobileAPI 68/68, Flutter 57/57 |
| Found and fixed during the audit | PJ portal showed half the page twice (not yet committed) |

The two most serious new findings (both now fixed):
1. **Any user can make themselves a farmer** by creating a farm. This skips the admin approval the Boer role needs.
2. **Any user can publish a package** that goes straight onto the public marketplace with no PJ and no document checks.

## Fixes (2026-10-09): all High findings fixed and retested

| # | Fix | Retest |
|---|---|---|
| 1 | `Farmer_db.CreateFarm` no longer grants the Farmer role; only an account that already holds it can own a farm. `FarmerController` answers **403** otherwise. | API: Jagter → 403 *"Slegs 'n geregistreerde boer kan 'n plaas skep."*; Jagter still only HUNTER, owns no farm. Boer still creates farms. |
| 2 | `Package_db.CreatePacket` saves nothing when the caller isn't a PJ; `PackageController` answers **403**. | API: Jagter → 403; no package without a PJ in the DB; PJ still creates packages. |
| 3 | `Ads_db`: the index puts `adExpirationTimestamp` as the key with `adStatus` as an included column (`NVARCHAR(MAX)` can't be a key). `SET NOCOUNT ON` is removed from the setup batch and the view/click updates. Schema failures are logged, not swallowed. | API: ad payment, owner edit, view and click all return **200**; both ad indexes now exist. |
| 4 | New `GetFarmBookingsAsync` (WebApp) uses the farmer-scoped `api/bookings/getFarmBookings`. That endpoint now also returns farm, farm id, total price, payment status and reference. Boer dashboard and bookings use it (an Admin without the Boer role still sees all). **Also fixed:** the hunter dashboard had the same mistake and now uses the hunter's own bookings. | Browser: the farmer's dashboard and bookings page list the hunter's booking; the hunter dashboard shows "1 Aktiewe besprekings"; no more 403s on these pages. |
| 5 | `api/Team/browse` now returns each profile's listing status (`isSubscriptionActive` = active and not expired, plus the expiry date), and nothing else from the subscription. The WebApp no longer asks the owner-only endpoint per profile, no longer invents demo profiles, and no longer treats demo users 1/2/4/5 as subscribers: that rule is removed from both the fallback and the cache the service pre-filled on start-up. | Browser: `/pj/browse` lists exactly the profiles with an active paid listing in the DB; no 403s. |
| 6 | The farm page books through one of **that farm's packages** via the real API. Signed-out visitors are asked to sign in. Nothing is invented locally, and errors are shown. Payment only after PJ approval. **Also fixed:** `RequestPackageAsync` (used by the marketplace) used to invent a local booking when the API failed; it now returns the server's reason, which the marketplace shows. The in-memory `SaveBookingAsync` is removed. | Browser: signed out → "Teken in"; signed in → request sent → a real row in `bookings` → visible under "My Besprekings". |
| 7 | *(Medium, fixed because it blocked #6)* `GetFarmByIdAsync` used a route that doesn't exist, so a farm opened by link stayed on "Plaasbesonderhede word gelaai…" forever. It now reads the farm from `api/farmer/all`. | Browser: farm page loads by direct link. |

**Tests added:** `HighFixesIntegrationTests` (6 LocalDB tests) plus 2 controller tests. WebAPI suite **463/463**.
End-to-end rerun: API flows **39/40**. The one failure is the old `api/farmer/{id}` URL itself, which the WebApp no longer calls. Browser retest **12/12** (one check needed rewording: the hunter dashboard shows counts, not titles).

## Fixes (2026-10-09): all Medium findings fixed and retested

| # | Fix | Retest |
|---|---|---|
| 8 | Removed the stray `=======` (and its note) from `PacketsMarketplace.razor`. | Browser: none on `/packets` or `/marketplace`. |
| 9 | Payment page requires sign-in. A booking deposit is computed from the signed-in hunter's **own, Approved** booking (`?amount=` / `?title=` in the URL are ignored). Bookings that aren't approved, aren't yours or have no price are refused with an explanation. The "Simuleer … betaling" button only shows when the WebApp runs in **Development**. The subscription flow's fallback to hardcoded demo users is removed. | Browser: signed out → "Teken asseblief in"; no booking → explained; Pending booking → "nog nie goedgekeur nie"; `?amount=1&title=Gekaap` → page shows the booking's R 8 000,00 and its own title; someone else's booking → "nie gevind nie". |
| 10 | `&amp;` inside C# strings replaced with `&` (`BrowseTeamRoles`, `PhDashboard`); no other entities found in C# strings. | Browser: "Spanbou & Werwing". |
| 11 | `SavePackageAsync` / `DeletePackageAsync` return the server's real result and only change the local cache after success. The API now says when a package with bookings was **deactivated instead of deleted** (to keep booking history), and the page says so. | Browser: deleting a package with bookings → "gedeaktiveer (nie geskrap nie)", still listed; deleting one without bookings → really gone, "suksesvol verwyder". |
| 12 | Registering as Boer-only or AP-only now shows and **requires** their details (farm name/region/registration number; business name/VAT/address). | Browser: empty → refused with the right message, no account; filled in → account created with only that role. |
| 13 | Login honours `?returnUrl=` (local paths only; `//other-site` is ignored). "Onthou my" works: ticked → `localStorage` + 7-day cookies; unticked → `sessionStorage` + session cookies (ends when the browser closes). | Browser: lands on `/jagter/bookings`; `//example.com` → `/profile`; storage checked both ways. |
| 14 | Passwords are now **salted PBKDF2-SHA256** (600 000 iterations, `Services/PasswordHasher.cs` in WebAPI and MobileAPI). Old SHA-256/plain rows still work and are **upgraded automatically on the next successful login** (web or app). The dead plain-text `Users_db.LoginAsync` is removed. The privacy policy now describes this accurately. | New accounts stored as `pbkdf2-sha256$600000$…`, every hash different; app login works; an old SHA-256 account is upgraded on login (integration test); privacy page checked. |

**Tests:** WebAPI **473/473** (new: `PasswordHasherTests`, SHA-256 upgrade integration test, package-delete response tests), MobileAPI **69/69**. Browser retest **31/33** checks passed. The two "failures" were my checks (uppercased button text; the AP card heading is "(Adverteerder)"), confirmed correct from the screenshots and the successful registration.

### New finding while fixing #9: payment status is trusted from the client (High, not fixed)

- A hunter may set their own booking to **Paid** via `POST api/bookings/setPaymentStatus/{id}` (the owner is allowed).
- The payment page's return handler (`?status=success`) marks a booking paid. With the #9 fixes this now requires the signed-in owner of an Approved booking, but it is still a URL anyone can open for their own booking.
- `PayFastController` (ITN) **skips signature validation** (comment: "We will skip strict signature validation in this demo").

The proper fix: only the verified PayFast ITN (signature + source IP + amount check) marks a booking Paid, and the client-side "success" only shows status. The local simulator would then need a Development-only server endpoint, because ITN can't reach localhost. Not done yet; it changes how payments work and needs your decision.

**Also seen during the retest (not bugs in the app):**
- The sign-in rate limiter returns **429** when many logins come from one machine within a minute (the test runs). The Admin pages also hit 429 when loaded in quick succession.
- A `499` on `/messages` was the browser leaving the page while a "mark as read" call was in flight.

## How it was tested

- **Services:** the real WebApp, WebAPI and the local `Mikpunt` LocalDB. WebAPI request logging was
  switched on, so failed API calls per page were captured. Outgoing email went to the log only.
- **Browser:** Playwright with the installed Microsoft Edge. For every page it checked:
  - the Blazor error banner, browser console errors and page exceptions;
  - failed WebAPI calls made while the page loaded;
  - headings shown twice;
  - visible merge markers or exception text;
  - whether the page refused the wrong users;
  - plus a screenshot.
- **Accounts:** one per role (Jagter, Boer, PJ, PH, VG, AP), registered, verified and signed in
  through the real API. The admin test account got its Admin role with SQL, since Admin can't be
  self-registered.
- **Static review:** a script found every block of 15+ lines that appears twice in the same file.
  The code was also searched for leftover merge markers and "Known bug" notes.

## Fixed during the audit

| Issue | Where | Fix |
|---|---|---|
| PJ portal showed the stat cards, quick links and recent bookings **twice** | `WebApp/Components/Pages/PjDashboard.razor` | Removed the identical second copy (189 lines). **Not yet committed.** |

## Findings

Severity: **High** = wrong data, money or a security gap; **Medium** = visible malfunction;
**Low** = edge case or inconsistency. "Seen" = reproduced in the running app; "Code" = confirmed by
reading the code.

### High

| # | Issue | Where | How confirmed |
|---|---|---|---|
| 1 | **FIXED** **Any signed-in user can make themselves a farmer.** Creating a farm as a Jagter succeeds, and the account silently gets the **FARMER** role. This bypasses the admin approval the Boer role needs at registration. | `FarmerController.CreateFarm` (uses the caller as owner with no role check), `Farmer_db.CreateFarm` | Seen: Jagter test account created farm 109 and now has HUNTER + FARMER |
| 2 | **FIXED** **Any signed-in user can create a package, and it is public immediately.** A Jagter's package was saved with **no PJ** (`pPjId` NULL) and appeared on the public marketplace right away. It bypassed the document verification that real PJ packages need (theirs stay hidden until an admin verifies). | `PackageController.CreatePacket` (no role check; uses the caller's user id as PJ id), `Package_db` visibility filter | Seen: anonymous `getPacket/106` → 200 and listed in `getPacketsPaginated` |
| 3 | **FIXED** **Ads: every edit, payment, view and click succeeds but is reported as an error.** The AP gets "Advertensie … nie gevind of magtiging geweier" (ad not found or access denied) after a successful edit or payment, and the WebApp logs a 404 for every ad shown. Cause: the setup batch in `Ads_db.EnsureColumnsAsync` tries to index `adStatus`, which is `NVARCHAR(MAX)`, so it fails on every call. The failure is swallowed and the "done" flag is never set, so the batch reruns each time and leaves `SET NOCOUNT ON` on the connection. Every row count after that is -1. | `WebAPI/Database/Ads_db.cs` (lines ~33–77 and the `> 0` checks at ~283, 313, 608, 679, 752) | Seen: payment and edit saved in the DB, API answered 404; `CREATE INDEX IX_ads_status_expiry` on `NVARCHAR(MAX)` |
| 4 | **FIXED** **Farmers never see their bookings.** The Boer dashboard and Boer bookings page call the Admin-only `api/admin/bookings`, get 403, and show 0 bookings and R0 revenue. | `BoerDashboard.razor`, `BoerBookings.razor`, `MikpuntApiService.GetBookingsAsync` | Seen: 403 on both pages for the Boer account |
| 5 | **FIXED** **The public directories only ever show four demo accounts.** `/browse` and `/pj/browse` look up each profile's subscription through an owner-only endpoint (one 403 per profile). The WebApp then makes one up: users 1, 2, 4 and 5 are hardcoded as active, everyone else as unpaid. So paying users never appear to others, and the demo users always do, all with an "Adverteerder" (advertiser) badge. | `MikpuntApiService.GetRoleSubscriptionAsync` (~line 3193), `ProfileController.GetRoleSubscription` | Seen: 403s for every profile, for every role |
| 6 | **FIXED** **Booking directly from a farm page never reaches the server.** It only exists in that browser tab (gone on refresh, never seen by the farmer), yet the user is sent to payment as if it worked. | `FarmDetail.razor`, `MikpuntApiService.SaveBookingAsync` | Code ("Known bug" note, still true) |

### Medium

| # | Issue | Where | How confirmed |
|---|---|---|---|
| 7 | **FIXED** **Farm pages always fall back to stale cached data.** The WebApp asks for `api/farmer/{id}`, but the API route is `api/farmer/getFarm/{id}`, so every request is a 404. | `MikpuntApiService` (~line 427), `/farm/{id}`, `/boer/edit-farm/{id}` | Seen: 404 for an existing farm |
| 8 | **FIXED** A leftover git merge marker **`=======`** is visible on the page under the packages | `PacketsMarketplace.razor:131` (`/packets`, `/marketplace`) | Seen for every role |
| 9 | **FIXED** The payment page opens while signed out, with no item and **R 0,00**. A developer button **"Simuleer onmiddellike suksesvolle betaling"** (fake a successful payment) is shown to all users. | `Payment.razor` | Seen |
| 10 | **FIXED** The `&` in titles shows as **`&amp;`**, e.g. "Spanbou &amp; Werwing". It's encoded twice inside a C# string. | `BrowseTeamRoles.razor:35`, `PhDashboard.razor:495` | Seen on `/pj/browse` |
| 11 | **FIXED** Saving or deleting a package **always reports success**, even when the server rejects it | `MikpuntApiService.SavePackageAsync` (~752), `DeletePackageAsync` (~866) | Code |
| 12 | **FIXED** Register: choosing only Boer or only AP skips their required fields; the role is saved with empty details | `Register.razor:571` | Code |
| 13 | **FIXED** Login ignores `?returnUrl=` (always goes to `/profile`); "Onthou my" (remember me) does nothing | `Login.razor` | Code |
| 14 | **FIXED** The privacy policy claims BCrypt/Argon2 password hashing; the code uses **unsalted SHA-256** | `Privacy.razor`, `User_db.HashPassword` | Code |

### Low

| # | Issue | Where | How confirmed |
|---|---|---|---|
| 15 | `/messages` and `/jagter/bookings` open without signing in, or for other roles, showing empty screens instead of "please sign in" (no data leaks) | `Messages.razor`, `JagterBookings.razor` | Seen |
| 16 | `/pj/browse` lists one person once per role (three cards for someone who is Boer, PH and VG) | `BrowseTeamRoles.razor` | Seen |
| 17 | Several pages fall back to **hardcoded user ids** (1, 2 or 5) if the signed-in user is missing, instead of failing | `PhBookings` (~443, ~474), `VgDashboard` (~488, ~531), `PjBookings` (~1874), `CreateFarm` (~190), `AdminDashboard` (~1205) | Code |
| 18 | A hunter cancelling a booking sets it to "Rejected", so the details show an empty "rejected because…" box | `JagterBookings.razor` | Code |
| 19 | Delete-user's document step uses a non-existent column (`udUserId`); harmless because another step covers it | `Profile_db.DeleteUser` | Code |
| 20 | `SetRoleStatus` is a stub that always returns false | `Profile_db.SetRoleStatus` | Code |
| 21 | Test-data quality: farms named "12" or "2312231" with text like "dfggfddfg" on the public directory | `/searchfarms` (data) | Seen |

### Code quality (not visible to users)

- The same SQL `SELECT` and row-mapping is copied 3–5 times in `Bookings_db.cs`, `Package_db.cs`,
  `User_db.cs`, `Team_db.cs` and `MikpuntApiService.cs` (~lines 581–740, 905–1040). A change to one
  copy is easy to miss in the others.
- Dead code: an unused "all receipts" feature in `ApDashboard.razor`; an unused duplicate
  `BookingsResponse` in `WebAPI/Models/Booking.cs`.
- The WebApp creates its `HttpClient` by hand, so its outgoing API calls aren't logged. Failed
  calls are invisible unless the WebAPI's request logging is switched on.

## What works (verified)

- Registration, email verification and sign-in for all six self-service roles; unverified accounts
  are refused (fixed earlier in `26fec93`).
- All role pages refuse signed-out visitors and other roles (except #15). An AP can't open or
  edit another AP's ad.
- Documents: upload, admin sees pending, admin approves; packages stay hidden until the farmer's
  and PJ's documents are verified (#2 is the bypass).
- Booking chain: request → PJ sees it → PJ assigns PH → PH accepts → Approved → paid; paying
  twice is detected; outsiders can't read or change the booking.
- Team invite: PJ invites, PH sees and accepts, PH appears on the PJ's team.
- Chat: direct chat, send, the other person reads it, outsiders are refused.
- Multi-hunter bookings: 48/48 API checks and 16/16 browser checks (earlier run).

## Not covered

- The Jagbestuur app on a phone (Outo-skoot field test, hunt tracking, app UI).
- PayFast's real sandbox callback (ITN); payment was set through the same endpoint the WebApp's
  test button uses.
- Ratings (need a Completed hunt), permits / e-520 submission, the AI helper's LLM answers, and
  admin actions beyond document verification.
- Load and performance; each page loaded in about 2.5 s on this machine, the first one ~4 s.

## Test data

- Removed: all test bookings, farms, packages, ads, payments, team invites and chats, and every test account
  except four (two per run).
- **Left behind on purpose:** the Boer and PJ test accounts of each run (`boer.*@audit.test`, `pj.*@audit.test`,
  six in total) plus their uploaded test documents. `documentAuditLog` is immutable by design (delete-blocking triggers) and
  references those documents. Both accounts are **suspended** so they can't sign in.
- Scripts and screenshots are in the session scratchpad, not in the repo.
