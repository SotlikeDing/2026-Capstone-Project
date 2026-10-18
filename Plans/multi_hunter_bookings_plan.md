# Implementation Plan: Multi-Hunter Bookings (Main Hunter + Linked Companions)

> **Status (2026-10-08): implemented** on `main_dev_helgard`, with these rules and differences from the plan below:
> * **Rules:** only the main hunter (`bookHunterId`, who pays) - or an Admin - adds hunters; only once the booking is
>   **confirmed** (`Approved` or `Paid`); at most **`bookHuntGuests - 1`** added hunters (the count includes the main hunter);
>   only **Hunter accounts** (an existing account without the Hunter role is refused; observers are never added). The main
>   hunter can remove hunters and an added hunter can leave, until the hunt is `Completed`.
> * Table is **`bookingCompanions`** (`db/AddBookingCompanions.sql`), not `bookingParticipants` - that name was already
>   taken by the ratings code (`GetBookingParticipants`, `GetBookingParticipantUserIds`). Statuses `Pending` / `Linked`.
>   Concurrent adds are serialised by an `UPDLOCK` on the booking row (proven with 10 parallel adds).
> * Invites link on email verification **and** whenever the hunter's trips are listed (web and app), so a user who adds the
>   Hunter role later is linked too. Linking always requires a verified email.
> * WebAPI: `BookingCompanions_db` + `BookingCompanionsController` (`GET/POST api/bookings/{id}/hunters`,
>   `DELETE api/bookings/{id}/hunters/{companionId}`, `GET api/bookings/companionBookings`). Added hunters never get the
>   money-bearing `getSpecificBooking`; `CompanionBookingResponse` has no price/payment fields.
> * WebApp: `BookingHuntersPanel` in the booking details on `/jagter/bookings`, plus a "Reise Waarby Ek Aansluit" section;
>   `/register?email=` pre-fills the invited email.
> * Mobile (user's choice: manage in **both** web and app): MobileAPI has the same endpoints (`MobileBookingCompanions_db`,
>   generated from the WebAPI class) and its own `EmailService` (same `EmailSettings`); `api/bookings/user/{userRoleId}`
>   now also returns joined trips with `isMainHunter`, `canManageHunters`, `maxHunters`, `hunterCount`. The app's bookings
>   screen shows "Saam met ...", and a Jagters sheet to add/remove/leave. Paid/Completed bookings now show as "Goedgekeur"
>   in the app (they showed "Wag Nog" before).
> * Tests: WebAPI `BookingCompanionsIntegrationTests` (LocalDB) + `BookingCompanionsControllerTests`; MobileAPI
>   `BookingCompanionsMobileTests` (LocalDB); Flutter `test/booking_hunters_test.dart`.

## 1. Context & Goal

**Scenario**: One hunter books a trip and brings along other hunters — some already
registered on MikPunt, some not. All of them need to be linked to the same booking:
one main hunter (the one who paid) and the rest as additional/companion hunters. Every
additional hunter needs mobile-app access to Hunt Tracking and the Leaderboard, but
none of them should see financial details (price, payment status, voucher amounts) —
and everyone stays the same "Hunter" role; there is no new role to invent.

**Current state** (confirmed by reading the code, not assumed):
- `bookings.bookHunterId` is a single scalar FK to `userRoles(userRoleId)` —
  structurally exactly one hunter per booking today
  (`db/Complete_create.sql:254-266`).
- `bookHuntGuests`/`bookNonHuntGuests` are plain `BIGINT` headcount columns with no FK
  and no link to any person record — "additional hunters" today are just two integers
  (confirmed in `Bookings_db.cs`, `src/WebApp/Models/Models.cs`, and
  `src/jagbestuur_app/lib/models/booking.dart:10-11`).
- **Hunt Tracking (`src/jagbestuur_app/lib/screens/map_screen.dart`) and the
  Leaderboard (`src/MobileAPI/MobileAPI/Controllers/PublicTrophiesController.cs`) are
  already completely unscoped** — any authenticated Hunter-role account can use both
  today, with zero booking-awareness or gating. This means "give companions mobile
  access to Hunt Tracking / Leaderboard" does **not** require building any new
  tracking or ranking plumbing — it only requires that a companion has a real, linked
  Hunter account. The actual work here is entirely about **linking people to a
  booking** and **keeping money out of a companion's view**.
- **Money only exists on the WebApp/WebAPI side today.**
  `MobileBookingResponse` (`src/MobileAPI/MobileAPI/Models/MobileBookingResponse.cs`)
  has no price fields at all — so the mobile side needs zero financial redaction;
  only the WebApp's booking DTOs (`TotalAmount`, `PaymentReference`,
  `PriceBreakdownJson`, etc. in `src/WebAPI/WebAPI/Models/Booking.cs`) need to stay
  unreachable by a companion.

**Chosen approach for unregistered companions**: invite + auto-claim. The main hunter
adds someone by name + email even if they've never signed up. A placeholder row is
created immediately (so the trip roster is complete right away); when that email later
verifies during normal registration, it auto-links to the booking — no extra step for
the companion or the main hunter, and no separate invite-token/claim-link system to
build or lose.

## 2. Data Model

New migration `db/AddBookingParticipants.sql` (idempotent, same pattern as this
session's `AddBookingStaffAcceptance.sql`), registered in
`DatabaseBootstrapper.MigrationScriptOrder` and `WebAPI.csproj`'s embedded resources:

```sql
CREATE TABLE bookingParticipants (
    bookingParticipantId INT IDENTITY(1,1) PRIMARY KEY,
    bookingId INT NOT NULL FOREIGN KEY REFERENCES bookings(bookingId),
    userRoleId INT NULL FOREIGN KEY REFERENCES userRoles(userRoleId), -- set once linked
    inviteName NVARCHAR(200) NULL,
    inviteEmail NVARCHAR(255) NULL,
    status NVARCHAR(20) NOT NULL DEFAULT 'PendingRegistration', -- PendingRegistration | Linked
    addedByUserRoleId INT NOT NULL,
    addedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    claimedAt DATETIME2 NULL
);

CREATE UNIQUE INDEX UQ_bookingParticipants_LinkedUser
    ON bookingParticipants(bookingId, userRoleId) WHERE userRoleId IS NOT NULL;

CREATE UNIQUE INDEX UQ_bookingParticipants_PendingEmail
    ON bookingParticipants(bookingId, inviteEmail) WHERE inviteEmail IS NOT NULL;
```

The **main/paying hunter stays exactly `bookings.bookHunterId`** — untouched, fully
backward compatible with every existing booking, query, and report. This new table
only ever holds *additional* hunters. A row is either:
- `Linked` — a real account, `userRoleId` set, or
- `PendingRegistration` — a placeholder, `inviteName`/`inviteEmail` set,
  `userRoleId` NULL.

The two filtered unique indexes prevent double-adding the same account or
double-inviting the same email to one booking — the same structural-backstop pattern
already used this session for `UQ_bookingStaffAssignments_ActiveRole`.

## 3. Backend: Linking & Management (WebAPI)

**`src/WebAPI/WebAPI/Database/BookingParticipants_db.cs`** (new)
- `AddParticipant(bookingId, addedByUserRoleId, name, email)` — looks up whether
  `email` already belongs to a Hunter-role account (same JIT-lookup shape already used
  for PH/VG resolution in `Bookings_db.ApproveBookingWithStaff`). If found, inserts a
  `Linked` row immediately; otherwise inserts `PendingRegistration`. Runs in a
  transaction and treats a duplicate add (caught by the filtered unique index) as an
  idempotent no-op rather than surfacing a raw constraint-violation exception — the
  same idempotency discipline established this session in `Bookings_db.SetPaymentStatus`
  and `Profile_db.ProcessSubscriptionPayment`.
- `GetParticipants(bookingId)` — the main hunter (from `bookings`/`userRoles`/`users`)
  plus every `bookingParticipants` row, each flagged Linked/PendingRegistration.
- `RemoveParticipant(id, callerUserRoleId)` — only the row's `addedByUserRoleId` (or
  an Admin) may remove it.
- `ClaimPendingInvitesForVerifiedEmail(email, newHunterUserRoleId)` — finds every
  `PendingRegistration` row matching `email` (case-insensitive) across all bookings
  and flips it to `Linked` (`userRoleId` set, `claimedAt = SYSUTCDATETIME()`).
  Idempotent — safe to call more than once for the same email.

**`src/WebAPI/WebAPI/Controllers/BookingParticipantsController.cs`** (new)
`[Authorize]`; `POST add`, `GET {bookingId}`, `DELETE {id}`. Uses the same
caller-identity extraction already established for `BookingsController.StaffRespond`
(`User.FindFirstValue(ClaimTypes.NameIdentifier)`), and validates the caller is the
booking's main hunter (or Admin) before allowing add/remove.

**Claim hook — `UserController.cs` (`VerifyEmail`, ~lines 475-490)**: after
`_userDb.VerifyEmail(email)` succeeds, call
`_bookingParticipantsDb.ClaimPendingInvitesForVerifiedEmail(email, hunterUserRoleId)`
(a small helper query resolves the just-verified user's Hunter-role `userRoleId`, if
any). Claiming is deliberately tied to a **verified** email, not raw registration —
`uIsEmailVerified` starts `false` at signup (`User_db.cs:98`) and only flips true via
this endpoint — so a companion slot can't be claimed by someone who merely typed a
similar or unverified address at registration time.

**Invite email**: reuse the existing `IEmailService.SendEmailAsync` pattern (already
used for `TeamController`'s removal email and `BookingsController`'s voucher email) —
subject "MikPunt - U is uitgenooi vir 'n jagekspedisie", linking to
`/register?email={email}`.

**`src/WebApp/Components/Pages/Register.razor`**: add
`[SupplyParameterFromQuery] Email`, pre-filling the email field so an invited
companion doesn't accidentally verify a different address than the one invited.

## 4. Backend: Keeping Money Out of a Companion's View (WebAPI + WebApp)

- New lean model in `src/WebApp/Models/Models.cs`: `CompanionTripModel` (Id,
  PackageTitle, FarmName, StartDate, EndDate, Status, MainHunterName, PhName, VgName)
  — **no price/payment field exists on the type at all**, so nothing can leak through
  it by accident.
- New `Bookings_db.GetCompanionBookings(int userId)` /
  `BookingsController GET getCompanionBookings?userId=` — returns bookings where the
  caller is a `Linked` `bookingParticipants` row, with a SQL `SELECT` list that
  contains no price columns at all — unreachable by construction, not merely hidden
  in the C# mapping layer.
- **`JagterBookings.razor`**: small, read-only "Reise Waarby Ek Aansluit" (Trips I'm
  Joining) section rendered from this endpoint — no Pay/Voucher buttons, since the
  DTO has nothing to render them from.
- **`PjBookings.razor`**: no changes required for this feature. A PJ-facing companion
  roster could reuse `GetParticipants` later if wanted; not built as part of this plan.

## 5. Mobile Access — the Actual Ask

**`src/MobileAPI/MobileAPI/Database/MobileBookings_db.cs`**: add
`GetBookingsForHunterOrCompanion(int userRoleId)` —
`bookHunterId = @userRoleId` (main hunter) `UNION` bookings reached through
`bookingParticipants WHERE userRoleId = @userRoleId AND status = 'Linked'`
(companion). **Implementation note**: the *existing* `GetHunterBookings` query joins
`hunter h ON b.bookHunterId = h.hunterId`, which does not match `bookHunterId`'s real
FK target (`userRoles` — confirmed in `db/Complete_create.sql:256` and used correctly
elsewhere, e.g. `Bookings_db.CreateBooking`). The new query must join directly on
`userRoleId` and must not repeat that pre-existing mismatch.

**`MobileBookingResponse`**: add `bool IsMainHunter`.

**`MobileBookingsController.GetHunterBookings`**: swap to call the new broadened
query. Same route/signature, so no Flutter contract break — it's already scoped by
the caller's own JWT `userRoleId` claim (`UserHasUserRole`), and the new result set is
a strict superset of today's (own bookings ∪ linked-companion bookings), so this is a
safe, backward-compatible widening.

**`src/jagbestuur_app/lib/screens/bookings_screen.dart`**: when
`booking.isMainHunter == false`, show "Saam met {mainHunterName}" instead of the
guest-count line, so the UI honestly reflects a real linked companion rather than a
headcount.

**No changes needed to `map_screen.dart` (Hunt Tracking) or the Leaderboard
screens/`PublicTrophiesController`** — both already work for any authenticated
Hunter-role account with zero booking scoping, so a linked companion gets full access
to both the moment their account exists. This is the reason the mobile-side change is
small: the feature was never gated to begin with.

## 6. Tests

- `src/WebAPI.Tests/Controllers/BookingParticipantsControllerTests.cs` — add/remove/
  list happy paths, forbidden-if-not-main-hunter, duplicate-add-is-idempotent.
- `src/WebAPI.Tests/BookingParticipantsIntegrationTests.cs` (real LocalDB,
  self-skipping, same shape as `PaymentIdempotencyIntegrationTests` and
  `BookingStaffAcceptanceIntegrationTests` from this session) — full lifecycle:
  invite an unregistered email → register with that email → verify it → confirm
  auto-claim flips the row to `Linked` and `GetBookingsForHunterOrCompanion` now
  includes the booking; ten concurrent duplicate-invite calls for the same email
  produce exactly one pending row.

## 7. Verification

- `dotnet build` on WebAPI, WebApp, WebAPI.Tests, MobileAPI; `dotnet test
  src/WebAPI.Tests` (must stay green).
- Manual pass: main hunter adds a registered companion (instant `Linked`) and an
  unregistered one (`PendingRegistration` + email sent) via a small "Add Companion"
  control on `JagterBookings.razor`; companion registers via the emailed,
  email-prefilled link, verifies their email, and confirms auto-claim; companion's
  mobile login now shows the trip in "My Bookings" (labelled "Saam met ...") and can
  freely use Hunt Tracking + the Leaderboard; confirm the mobile response never
  contains any price field; confirm the companion's own `/jagter/bookings` shows the
  trip under "Reise Waarby Ek Aansluit" with no price/pay UI anywhere.
