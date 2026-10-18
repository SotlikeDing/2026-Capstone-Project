# Security: threat model, findings and controls

What we protect, what we found by testing our own deployment, and what is in place now. Evidence: the
end-to-end suite (`tests/e2e`), the unit tests in `src/WebAPI.Tests/Security`, and the OWASP ZAP baseline
scans in `docs/security/zap/`.

## Assets and threats

| Asset | Threat | Main controls |
|---|---|---|
| Accounts and sessions | password guessing, token forgery/theft, use after logout or suspension | PBKDF2 hashes, per-account lockout (web) and per-address lockout (app), per-IP limits, private JWT keys (startup refuses placeholders), token blacklist on logout, suspended users rejected on every request |
| Personal data and documents (ID, licences, SAPS 520) | reading another user's data, enumeration | ownership checks on every resource, admin-only listings, documents served only to owner/admin, verification-code store with attempt limits |
| Bookings and payments | price tampering, fake payment confirmation, open redirect after payment | server-side pricing and PayFast signature, ITN validation, return URLs limited to our own origins |
| Audit trail | covering tracks | `ratingHistory`, `documentAuditLog`, `adminAuditLog` are append-only (triggers refuse UPDATE/DELETE); the acting admin is taken from the token, never the request body |
| Availability | floods on sign-up/mail endpoints, AI cost | rate-limit policies (admin, ratings, documents, AI, account endpoints) |
| Infrastructure | leaked config, stack traces | secrets only in the server's `.env`; generic 500 bodies with a trace id outside Development |

## Findings from our own testing (Oct 2026) and their fixes

| # | Finding | Severity | Fix | Verified by |
|---|---|---|---|---|
| 1 | Tokens signed with the **public placeholder key** from `appsettings.json` were accepted | Critical | private keys on the server; both APIs refuse to start with a placeholder | smoke test (forged token → 401) |
| 2 | Token refresh returned **user 1's** session | Critical | refresh looks the user up by the token's e-mail, refuses suspended users | E2E t2 |
| 3 | Logout didn't revoke; suspension didn't end existing sessions | High | blacklist checked on every request; suspension cache invalidated immediately | E2E t7 |
| 4 | E-mail verification codes in memory, guessable without limit | High | SQL store, crypto RNG, hashed, 24 h expiry, locked after 5 wrong guesses | unit tests + E2E t0 |
| 5 | **App sign-in had no lockout** (the web lockout didn't cover it) | High | 5 failed sign-ins lock the address for 15 min; per-IP limit | unit tests + local run (401×5 → 429) |
| 6 | Password-reset / resend-verification could e-mail-bomb an address | Medium | max 3 mails per address per 15 min; per-IP limits on all anonymous account endpoints | unit tests + local run |
| 7 | Controllers returned `ex.Message`/stack traces in 500 responses (SQL table names) | Medium | MVC filter replaces every 500 body outside Development; global handler likewise | unit tests |
| 8 | Request log printed query strings incl. **verification/reset tokens** | Medium | log path only | local run (no tokens in log) |
| 9 | Website sent no CSP / nosniff / referrer policy; could be framed | Medium | security headers + CSP (frame-ancestors none, object-src none, form-action self+PayFast) | smoke test |
| 10 | Admin could suspend themselves (lock-out of the only admin) | Low | refused (400) | E2E t7 |
| 11 | Migration scripts switched to the database named `Mikpunt` regardless of configuration | Medium (integrity) | bootstrapper strips `USE` lines; unit test guards it | unit test |
| 12 | Duplicate users/roles/document types; no unique e-mail | Medium (integrity) | dedupe migration + unique indexes on e-mail, (user, role), document type | restore-copy run |
| 13 | A real SMTP app password is in git history (`.env.example`, Aug 2026) | High | **to do: revoke it in the Google account** (decision list) | - |

## OWASP ZAP baseline (website, passive scan)

| | Before (12-13 Oct) | After (13 Oct, `42f86b50`) |
|---|---|---|
| High-risk failures | 0 | 0 |
| Warnings | 11 | 7 |
| Gone | | missing anti-clickjacking header, X-Content-Type-Options, missing CSP, missing Permissions-Policy |
| Remaining | | informational (comments in Blazor's script, auth/session detection, caching), CSP wildcards for images from `http:`/`https:` (user photos come from the API host), SRI on Google Fonts (not possible: the CSS is generated per browser), Cross-Origin-Embedder-Policy (would block the CDN fonts) |

Reports: `docs/security/zap/before-*.html` and `after-webapp.html`.

## Still open

- **HTTPS** on the reverse proxy (decision D1). Until then traffic over the public names is plain HTTP.
- The website keeps the JWT in a JavaScript-readable cookie/localStorage (needed by the Blazor circuit
  today). Moving to an HttpOnly cookie is the next hardening step once HTTPS is in place.
- MobileAPI allows any CORS origin. Acceptable for a bearer-token API used by a native app; restrict if a
  browser client is ever added.
- `script-src 'unsafe-inline'` in the CSP (inline helpers in `App.razor`). Moving them to a static `.js`
  file allows dropping it.
