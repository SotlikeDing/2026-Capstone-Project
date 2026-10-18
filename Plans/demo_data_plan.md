# Plan: demo data for the 13-minute usability scenario

*For `Bruikbaarheidstudie_Demo_Scenario_13_Minute.md` ("one hunt from start to finish"). Written 2026-10-13.*

## 0. Two things to decide first

1. **The script describes the 12 Oct findings, and most of them are fixed now.** Payment no longer says "Ongeldige terugkeer-adres", booking dates are validated, SAPS 520 works, the web VG media panel works, story cards are Afrikaans with SA time, "Voltooide jagte" counts, and admin self-suspension is refused.
   - **Recommendation:** present it as **"what we found → what we fixed"**. Show the old bugs from screenshots, which the operator checklist already plans for, and show the fixed flow live. Then update the closing figures to the new numbers: **78 % easy, 0 blocked** (see `bruikbaarheidstudie.md`).
   - The data below supports the **fixed** system, so every segment can be shown live.
2. **Currency:** pricing is shown in `$` (decision D9). For a South African demo, use ZAR amounts and fix the symbol first, or explain it.

## 1. Principles

- **The story is "frozen" at different stages.** The demo can't wait for approvals, payments or video rendering, as the operator checklist says. So Johan's hunt exists **several times**, each copy at a later stage. Each segment uses the copy at its own stage, and one booking is left open for doing live.
- **More than the minimum.** Lists, dashboards, the marketplace, the trophy room and ratings must look like a platform in use: several pages, a mix of statuses and history over months. Not one lonely row.
- **Created through the system where it matters.** Accounts, documents, media and the story go through the real APIs, so password hashes, roles, audit rows and stored files are all genuine. Only history that can't be made "in the past" through the API goes in by SQL: past booking dates, ratings timestamps, chat timestamps, routes.
- **Repeatable and resettable.**
  - One command builds everything; it's idempotent, keyed on a marker e-mail domain.
  - A reset command brings the live parts back to their starting point before every rehearsal.
  - The reset never deletes from the append-only audit tables (`ratingHistory`, `documentAuditLog`, `adminAuditLog`). It creates fresh "live" rows instead.
- **Looks real:**
  - no "E2E", "Toets" or `test.com` anywhere visible;
  - South African names and places;
  - real species, realistic 2026 prices;
  - dates relative to the demo day.

## 2. Tooling (what gets built)

| File | Purpose |
|---|---|
| `tools/demo/seed_demo.py` | Builds the whole data set: `--demo-date 2026-10-20` (default: today). Uses the WebAPI/MobileAPI plus a few SQL statements. Same environment variables as `tests/e2e` (`E2E_ADMIN_*`, `E2E_SQL_*`, URLs), plus `DEMO_PASSWORD` for the cast accounts (never committed). |
| `tools/demo/reset_demo.py` | Before each rehearsal: puts the live objects back (see §5). Takes about 1 minute. |
| `tools/demo/check_demo.py` | Pre-flight check: prints ✓/✗ per segment (e.g. "Johan has a Paid booking with Thabo and Vusi accepted", "recap Ready", "pending ad exists"). Run it 30 minutes before the demo. |
| `tools/demo/assets/` | Photos and short clips the team owns or that are free to use: farm, lodge, animals, hunt moments, trophy. These go through the upload APIs so the files really exist on the server. |
| `tools/demo/cast.json` | Names, roles, profile texts and which segments each person appears in. Readable by the presenter too. |

All demo accounts use **`@demo.mikpunt.co.za`**. Cleanup and reset only touch data owned by those accounts.

## 3. The cast (scenario people)

| Person | Role(s) | Profile data to seed | Used in |
|---|---|---|---|
| **Johan van Wyk** | Hunter (main) | Profile and bio; country ZA; 2 permits in the wallet (Limpopo, Mpumalanga); 1 approved SAPS 520 + 1 pending; 4 earlier completed hunts so "Voltooide jagte" isn't 0 | App, booking, permits, hunt, trophy, rating |
| **Mia Botha** | Hunter (companion) | Profile; linked as a companion on Johan's booking | Extra hunters, chat |
| **Pieter Nel** | Outfitter (PJ) | Business "Nel Safaris (Edms) Bpk"; all PJ documents **approved**; team = Thabo, Vusi + 2 others; 3 packages | Documents, package, team, staff assignment |
| **Thabo Mokoena** | PH | 12 years' experience, dangerous-game rate; documents approved, **including one rejected-then-replaced** (shows the rejection reason and the replacement); rating 4.7 from 9 ratings | Documents, staff acceptance, trophy approval |
| **Vusi Dlamini** | VG | Company "Vusi Media", drone licence; VG package "Volledige jagdag-video"; regions Limpopo, North West | Media upload, story |
| **Anna Pretorius** | Farmer | Business "Kameeldoring Wildplaas"; 1 main farm + 1 second farm | Farm, game, ratings |
| **Karoo Optics** (advertiser) | AP | 1 **active paid** ad + 1 **unpaid** ad waiting for payment | Ad rule "live only after payment" |
| **Admin** | Admin | The existing admin account | Moderation, disputes, audit |
| **Lerato Khumalo** | PJ (new) | Documents **pending**; 1 package saved but not visible | Shows T5 ("not visible until approved") |

## 4. Scenario data per segment

| Time | Segment | Data that must exist before the demo | Done live |
|---|---|---|---|
| 1:25 | Registration, documents | Cast accounts verified. Pieter/Thabo/Vusi documents approved; Thabo's history shows one rejection with its reason. Lerato's documents pending in the admin queue. | Sign in; show the document list |
| 2:30 | Farm, package, ad | **Kameeldoring Wildplaas** (Limpopo, Waterberg): description, 2 400 ha, bushveld, game fence, lodge (6 rooms, R1 450 pppn, catering). Game with quota and price: kudu, impala, blue wildebeest, warthog, gemsbok, waterbuck, eland. Pieter's package **"Waterberg Koedoe-safari, 4 dae"** live. Lerato's package hidden. Karoo Optics: 1 active ad, 1 waiting for payment. | Anna adds one more species; Pieter opens the package editor |
| 3:35 | Search, booking, payment | Marketplace with **≥ 12 visible packages** (see §6), so paging and filters make sense. Johan's **live booking**: package open, future dates free. | Johan books live; payment goes to PayFast (sandbox) or the simulator (D2) |
| 5:00 | Team, chat | Booking **B-Team** (future dates, Paid): Thabo and Vusi **accepted**, Mia linked as a companion. Group chat with ~15 messages over 3 days, including one flagged message ("betaal kontant by die hek"). A pending team invite from Pieter to another PH **with expedition name and terms**. | Pieter shows the staff statuses; open the chat |
| 6:20 | Permits, SAPS 520 | Johan: 2 wallet permits, 1 approved SAPS 520 and 1 pending (2 rifles, .308 and .375 H&H). | Submit a SAPS 520 live (validation shows) |
| 7:15 | Hunt, media, story | Booking **B-Hunt** (hunted 3 days ago, Paid, now finished): Johan's GPS route on the farm (≈7 km, 4 h) with 3 shot pins. Media: 8 hunter photos with GPS/time, 4 PH photos, 2 VG clips (≤ 30 s). Story **Ready** (rendered beforehand, 20–25 s). | Vusi opens the media panel; play the story |
| 9:05 | Trophy, rating | On B-Hunt: one trophy submission **approved**, now in the trophy room (kudu, 132 cm, Rowland Ward evaluated). A second one in **"correction requested"**. Ratings partly given (Thabo→Johan done, Johan→Thabo still open). | Johan resubmits or Thabo approves; Johan rates Thabo and Vusi |
| 10:10 | Admin | Dashboard numbers from the volume data (§6). Moderation queue: 2 flagged chat messages, 1 flagged rating comment, 1 **open rating dispute**, Lerato's pending documents. | Approve a document, resolve the dispute; show the audit log |

Booking copies for Johan, all with the same package, PJ, PH and VG:

| Code | Dates | Status | Purpose |
|---|---|---|---|
| B-Live | free future dates | — (made live) | 3:35 booking demo |
| B-Team | +21 to +25 days | Paid, staff accepted, Mia linked | 5:00 team and chat |
| B-Hunt | −5 to −2 days | Paid → finished | 7:15 media/story, 9:05 trophy/ratings |
| History ×4 | months ago | finished, rated | "Voltooide jagte: 5", rating history, trophies |

## 5. Reset before each rehearsal (`reset_demo.py`)

The live steps change data. The reset brings the start state back without deleting audit rows:
- **B-Live:** cancel any booking made during the previous rehearsal (status Cancelled, so it no longer blocks the dates). The package stays open.
- **B-Hunt:** the second trophy goes back to "correction requested". Johan's ratings for Thabo and Vusi are removed again; these aren't audit rows, but if `ratingHistory` blocks it, the reset creates a fresh B-Hunt copy instead.
- **Moderation:** re-create the open dispute and the pending documents for Lerato if the last rehearsal resolved them.
- **Anna:** remove the species she added live.
- **Documents:** delete nothing; re-upload Lerato's documents as pending.
- **Cleanup:** sign every cast account out (revoke tokens) so the demo starts from the sign-in screen.

## 6. Background data ("more than the minimum")

Enough volume that every list, dashboard and statistic looks lived-in. All rows belong to `@demo.mikpunt.co.za` accounts.

| Area | Amount | Detail |
|---|---|---|
| Users | ~45 | 25 hunters (incl. 4 foreign: Germany, US, Denmark, Spain), 5 PJs, 6 PHs, 4 VGs, 5 farmers, 3 advertisers; 2 suspended (for the admin view) |
| Farms | 8 | Across 6 provinces (Limpopo ×2, North West, Eastern Cape, Free State, Northern Cape, KwaZulu-Natal, Mpumalanga); 3–8 species each with quotas and prices; lodges with different prices; photos |
| Packages | 16 | **12 visible**, 2 hidden (pending documents), 2 expired. Prices ~R18 000 to R185 000; 2–10 days; plains game, cull hunts, bow hunting, 1 dangerous game (buffalo). Enough for 2+ marketplace pages and price/province filters. |
| VG packages | 4 | Half day / full day / drone add-on |
| Bookings | ~45 | Spread over the last 8 months and next 3 months, in **every** status: Pending, PendingStaffAcceptance, Approved, Paid, Completed, Rejected (with reason), Cancelled. Drives the dashboard figures, booking lists and income figures. |
| Ratings | ~70 | Mostly 4–5★ with Afrikaans/English comments, some 2–3★; farm ratings for 5 farms. Gives realistic averages on profiles. |
| Trophies | ~20 | ~12 public in the trophy room / leaderboard; mixed species; several Rowland Ward evaluated |
| Chats | ~12 conversations, ~150 messages | Direct and group chats; 2 flagged |
| Ads | 8 | 6 active paid (lodges, optics, taxidermy, outfitters' gear, game capture), 1 unpaid, 1 expired; views and clicks so the advertiser dashboard has numbers |
| Notifications | ~120 | Unread/read mix per cast member |
| Permits / SAPS 520 | ~15 | Varied provinces and expiry dates (one expiring soon) |
| AI helper | — | The extra packages and bookings give the price advice and team matching real market observations, so the AI demo gives a sensible answer |

## 7. Data quality rules

- **One status vocabulary.** Farms currently use both `ACTIVE` and `Aktief`. Pick the one the code expects and fix the old rows; seed only that one.
- **Consistent money:** ZAR, consistent with D9. Package price = what the booking shows.
- **Dates relative to `--demo-date`,** so the demo still looks right if it moves a week.
- **Coordinates inside South Africa** that match the farm's province. Routes stay on the farm.
- **Photos have EXIF time and GPS** matching the hunt, so the media matches the route.
- **No real people's personal data.** Fictional ID/passport numbers in a format that fails any real check.

## 8. Steps and effort

| # | Step | Effort | Depends on |
|---|---|---|---|
| 1 | Decisions §0 (narrative fixed vs. old; currency) | — | You |
| 2 | Collect assets: ~40 photos, 3 short clips, 1 trophy photo (team's own photos or free-licence) | 1–2 h (team) | — |
| 3 | `cast.json` + background generator (names, farms, packages, prices) | 2 h | 1 |
| 4 | `seed_demo.py`: accounts and documents via API, history via SQL, media via upload API, story via the recap API | 4–6 h | 2, 3 |
| 5 | `reset_demo.py` + `check_demo.py` | 2 h | 4 |
| 6 | Clean the existing visible test data (old E2E farms/packages) so they don't appear next to the demo data (D4: hide, not delete) | 30 min | — |
| 7 | Take a **backup**, run the seed on staging, run `check_demo.py` and the e2e smoke test | 30 min | 4–6 |
| 8 | Two rehearsals with `reset_demo.py` in between, timed against the 13 minutes | 1 h (team) | 7 |
| 9 | Screenshots of the old bugs for the "before" moments | 30 min | 1 |

Total ≈ 1.5 days of building, plus the team's asset collection and rehearsals.

## 9. Risks

| Risk | Mitigation |
|---|---|
| VPN or staging down on the day | Recorded backup video of the full demo; `check_demo.py` 30 minutes before |
| Story rendering or YouTube unavailable | Story rendered in advance (B-Hunt); YouTube isn't needed (D3) |
| Live payment needs PayFast's callback (D2) | Turn on the payment simulator for staging, or show the checkout up to PayFast and continue with B-Team |
| Live steps leave data behind | `reset_demo.py` between rehearsals |
| Release APK can't connect (D1) | Demo on a debug build, or fix HTTPS first |
| The rate limits (5 wrong passwords, 3 reset mails) bite during rehearsal | Use the right passwords; the reset revokes sessions but doesn't touch the limits; wait 15 minutes if it happens |
