# Plan: from strong project to the top team

*Written 2026-10-13, based on the staging tests, the fixes made so far, and the review of the project as a third-year capstone.*

## 0. The honest starting point

Nobody can guarantee first place, because that depends on the other teams and the examiners. What we can do is make sure that on every criterion an examiner is likely to check, our project is the strongest answer they see.

**What we have already (keep and show it off):**
- Six roles plus admin, two APIs, a website and a mobile app, and about 279 endpoints.
- It is deployed for real: Docker, GitLab CI, a staging server, automatic migrations.
- Integrations: PayFast, a private YouTube channel, an LLM helper, gunshot detection and generated hunt stories.
- Built around a real domain: SAPS 520, PH/outfitter workflows, everything in Afrikaans.
- The latest staging test: 973 of 974 checks passed.

**What will cost us marks if we don't fix it:**
1. No HTTPS on the server, so a release build of the app can't connect (D1).
2. The data science part has no evaluation (no dataset description, metrics or baseline).
3. Automated tests are thin and don't run in CI.
4. The database and migration process is ad hoc.
5. Process discipline: ~50 uncommitted files, and production once depended on a personal login.
6. Payments never complete automatically on staging (D2).

Top teams usually lose to each other on **evidence**, not features. Our plan is therefore: **stop adding features; prove, polish and present what we have.**

---

## 1. Rules for the rest of the project

| Rule | Why |
|---|---|
| **Feature freeze** from the end of week 1. After that, only fixes, evaluation, polish and documentation. | Every new feature adds bugs and leaves less time for proof. |
| Everything is committed and pushed **daily**. No working tree with 50 changed files. | An examiner may ask to see the git history, and work that's lost can't be graded. |
| Every change to `main_dev` goes through a **merge request** with at least one reviewer. | It shows a professional process, and the history is part of the evidence. |
| CI must be green before merging (build + tests). | See workstream B. |
| Every bug found gets an issue in GitLab, even if we fix it ourselves the same day. | The issue tracker becomes evidence of a quality process. |
| Staging is the single source of truth for the demo. Nobody hand-edits the server without writing it down in `docs/ops.md`. | Prevents "it worked yesterday." |

---

## 2. Workstreams

Each workstream has an **owner**, a **result that counts as done**, and **evidence for the report**. Fill in the owners at the first meeting.

### A. Close the security and stability gaps — owner: ___

Already done (commits e1815e3, 84af406): forged tokens, refresh, logout revocation, suspension, verification codes, error leaks, self-suspend.

Still to do:
- [ ] **HTTPS** on the reverse proxy (10.0.0.47), with a certificate for `*.mikpunt.ddns.net` from Let's Encrypt or Caddy (decision D1). Then redirect HTTP to HTTPS and set HSTS.
- [ ] Turn off cleartext HTTP in the Flutter release build (`usesCleartextTraffic=false`, ATS on iOS) once HTTPS works.
- [ ] Rate-limit sign-in, register, forgot-password and verify-email on both APIs.
- [ ] Rotate all secrets that ever sat in git history: SMTP, LLM keys, and the database passwords used before `mikpunt_app`.
- [ ] Add security headers (CSP, X-Frame-Options, Referrer-Policy) on the webapp.
- [ ] Run OWASP ZAP baseline against staging, fix the high and medium findings, and keep the report.
- [ ] Clean up the decisions D4–D9 in `Plans/beslissings_nodig.md`.

**Done when:** a ZAP baseline run has no high findings, the site and app run on HTTPS only, and a release APK works against staging.
**Evidence for the report:** the ZAP report before and after, plus a short "threat model and fixes" table. That table shows maturity: we found our own holes and closed them.

### B. Tests that run by themselves — owner: ___

- [ ] Move the end-to-end suite (currently ~974 checks in the staging scripts) into the repo as `tests/e2e/`. Make it configurable by environment variables, with no passwords in the code.
- [ ] Add a CI job: `dotnet test` on every push, and the e2e suite after every deploy to staging (smoke subset on every deploy, full suite nightly).
- [ ] Add unit tests for the core business rules:
  - booking states and their transitions;
  - booking date validation;
  - cancellation rules;
  - price calculation;
  - rating eligibility;
  - SAPS 520 validation.
- [ ] Integration tests for the database layer against a throwaway SQL Server container in CI.
- [ ] Flutter: `flutter test` for models and services, plus 2–3 widget tests (sign-in, booking list, recap).
- [ ] Report the coverage number in CI. Aim for ~60% on the WebAPI business logic. The number itself matters less than showing it rising.
- [ ] Add a test-data cleanup script that respects the append-only audit tables (D4).

**Done when:** every merge request shows green CI with unit and e2e results.
**Evidence:** a screenshot of the CI pipeline, a table of test counts by area, and the coverage graph.

### C. Database and data quality — owner: ___

- [ ] Number the migration scripts and record which ones have run in a `schemaVersions` table. A failed migration must **stop** the start-up, not just log it.
- [ ] Remove the duplicates (document types, seed users; D5) with one migration, and add unique constraints so it can't happen again.
- [ ] Make the seed data idempotent.
- [ ] Add an ER diagram (generated from the database) to the docs.
- [ ] Schedule nightly backups of the `Mikpunt` database, and test one restore. Write down how long it took.

**Evidence:** the ER diagram, the migration table, and "backup restored in X minutes."

### D. Make the data science part real — owner: ___ (data science members)

This is where we can **beat** the other teams. Most capstones show a model; few show a properly evaluated one.

**D1. Gunshot detection (Outo-skoot)**
- [ ] Build a labelled dataset:
  - shots: own recordings at a range, plus public sets (e.g. the gunshot clips in UrbanSound8K or ESC-50);
  - negatives: hunting-environment sounds (wind, voices, vehicles, birds, branches, door slams).
- [ ] Split by **recording session**, not by clip, so the test set isn't polluted.
- [ ] Report precision, recall, F1, a confusion matrix, the ROC/PR curve, and the chosen threshold with the reasoning behind it.
- [ ] Compare against a baseline (e.g. a simple energy/peak detector).
- [ ] Measure on at least 2 real phones (one Android, one iPhone): latency, battery per hour, false alarms per hour.
- [ ] Do an error analysis: which sounds cause false alarms, and what we did about them.

**D2. AI helper (package price advice and team matching)**
- [ ] Describe exactly what the model is: features, how it learns from feedback, and what the LLM does versus the statistics.
- [ ] Evaluate the price advice against a held-out set, using MAE/MAPE compared with simple baselines (province median price; median for the species mix).
- [ ] Evaluate team matching with precision@k against hand-labelled "good matches" from domain experts (PHs or outfitters we can ask).
- [ ] Use the advice feedback (`AiHelper/feedback`) as an online metric: % useful over time.
- [ ] Be clear about limitations: we trained on synthetic data first. Show how the system learns as real data arrives (a learning curve as rows are added).
- [ ] Bias and fairness: does the matching favour certain provinces or new users? Show one simple analysis.

**D3. Hunt stories (recap)**
- [ ] Small user evaluation: 5–10 people each score generated stories (accuracy, usefulness, would share it) on a 1–5 scale.
- [ ] Describe how the story is built: GPS route, gunshot timeline, VG media and SAST time.

**D4. Analytics on the platform's own data (cheap but impressive)**
- [ ] An admin "insights" page or notebook showing:
  - bookings per province and month;
  - conversion from search → booking → payment;
  - popular species;
  - average rating per role.
- [ ] Optional: a simple demand forecast per province or season, with an honest backtest.

**Done when:** each model has its own section with data, method, metrics, baseline, error analysis and limitations.
**Evidence:** a `notebooks/` folder of reproducible Jupyter notebooks (with fixed seeds), plus a "Model card" for each model.

### E. Operations and observability — owner: ___

- [ ] Structured logs, gathered in one place (Seq, Grafana Loki or just `docker logs` + a script).
- [ ] Health endpoints for all 3 services, shown on the Glance dashboard, with an alert when one is down.
- [ ] A simple metrics page: requests per minute, errors, and p95 response time.
- [ ] Load test with k6 or Locust (e.g. 100 concurrent users on search + booking). Report p95 and the error rate, fix the slowest endpoint, and measure again.
- [ ] Payments: set up the PayFast sandbox properly with a public notify URL (D2), so a test payment completes the whole flow from booking to "paid."
- [ ] Reconnect YouTube (D3) and test the full upload → story flow on staging.
- [ ] Commit the compose file the server actually runs, with no secrets in it (D7).

**Evidence:** the load-test graph before and after, a screenshot of the dashboard, and an architecture diagram showing the deployment.

### F. User experience and real users — owner: ___

- [ ] Run the usability study (`Plans/bruikbaarheidstudie.md`) with **at least 5 real people**, ideally real hunters, a farmer and a PH. Use tasks with a timer, success rates and SUS scores.
- [ ] Fix the top 5 problems they find, then measure again with new people. **A before-and-after SUS score is gold in a report.**
- [ ] Accessibility: run Lighthouse on the main pages and aim for 90+ accessibility. Check contrast and labels on form inputs.
- [ ] Mobile and responsive: test the website at 375px wide.
- [ ] Consistency: one language register, the same Afrikaans terms everywhere, and good empty-state messages ("Jy het nog geen besprekings nie" plus an action button).
- [ ] Fix the marketplace pagination oddity (1 package spread over 4 pages).
- [ ] Store-quality app polish: icon, splash screen, and a signed release APK installable via a QR code at the demo.

**Evidence:** the SUS before and after, a task-success table, quotes from real users, and the Lighthouse scores.

### G. Documentation and report — owner: ___ (everyone writes their own part)

Suggested report structure:
1. Problem and users (with real interview or survey results).
2. Requirements, and how each one was met (a traceability table: requirement → feature → test).
3. Architecture (C4 diagrams: context, containers, components), with the reasoning for the key choices:
   - why there are two APIs;
   - why videos go to YouTube;
   - why Blazor Server.
4. Database design (ER diagram, normalisation, audit tables).
5. Security (threat model, what we found ourselves, what we fixed, the ZAP results).
6. Data science (workstream D: data, models, metrics, baselines, error analysis, model cards, ethics).
7. Testing and quality (test counts, CI, coverage, load test).
8. Deployment and operations.
9. Usability (SUS before and after).
10. Project management (sprints, burndown, who did what, using the git and issue evidence).
11. Reflection and future work. Be honest; examiners reward self-insight.

Also:
- [ ] A `README.md` that gets a new developer up and running in 15 minutes (test it with someone who has never seen the project).
- [ ] API documentation via Swagger/OpenAPI, with descriptions on every endpoint.
- [ ] `docs/ops.md`: deploy, rollback, backup and restore, rotating secrets.
- [ ] Architecture Decision Records (ADRs) for 5–8 key choices.

### H. Demo and presentation — owner: ___

- [ ] Build the demo around **one story** that covers everything:
  1. A farmer lists a farm.
  2. A PJ builds a package with AI price advice and puts a team together.
  3. A hunter books and pays.
  4. The hunt happens on the phone: GPS route, the gunshot is detected.
  5. The VG uploads media.
  6. The story is generated.
  7. Everyone rates each other.
  8. The admin sees everything in the dashboard and the audit log.
- [ ] Prepare demo accounts and data on staging, plus a **reset script** that brings the demo back to its start state in 1 minute.
- [ ] Record a backup video of the full demo in case the network or VPN fails on the day.
- [ ] Practise at least 3 times with a timer, including once in front of people outside the team.
- [ ] Prepare answers to the questions examiners will probably ask:
  - How do you know the model works?
  - What happens if two people book the same dates?
  - How do you protect personal data (POPIA)?
  - What would you do differently?
  - How does it scale?
- [ ] One slide with the **numbers**:
  - ~279 endpoints;
  - X tests, Y% coverage;
  - SUS before → after;
  - gunshot F1;
  - p95 response time;
  - 0 high ZAP findings.

### I. Law and ethics (a quick win most teams forget) — owner: ___

- [ ] POPIA: what personal data we hold, why, for how long, and who can see it. Add a data deletion/export request in the profile.
- [ ] Firearm and hunting rules: SAPS 520 and permits. Make clear the platform helps with the paperwork but doesn't replace the authorities.
- [ ] Ethics of the AI parts: transparency (why this advice?), and that a person always makes the final decision.
- [ ] Gunshot recordings and location: user consent, and where the data is stored.

---

## 3. Timeline (adjust to the real due date)

| Week | Focus | Must be done by the end of the week |
|---|---|---|
| **1** | Stabilise | Everything committed. HTTPS on. D1–D9 decided. Feature freeze. Owners assigned. CI runs unit tests. |
| **2** | Evidence | E2E suite in the repo and CI. Gunshot dataset gathered. Price-advice baseline and evaluation started. ZAP run #1. First usability round (5 people). |
| **3** | Improve | Fix the top usability problems. Fix the ZAP findings. Model evaluation done, with error analysis. Load test plus optimisation. Migrations made safe. |
| **4** | Measure again | Second usability round (SUS after). Load test again. ZAP run #2. Coverage up. Payments complete on staging. |
| **5** | Write and practise | Report complete. Diagrams, model cards, ADRs. Demo reset script. Backup video. Two practice runs. |
| **Final days** | Polish | Third practice run. Final regression on staging, freeze the demo data, and don't deploy anything on the day before. |

If there's less time, the order of priority is:
1. HTTPS + feature freeze + commit discipline.
2. Data science evaluation.
3. Tests in CI.
4. Usability before and after.
5. Demo story + backup video.
6. Everything else.

---

## 4. Checklist: "are we the best team?"

Tick this off the week before the final submission:

- [ ] The full demo story works on staging over HTTPS, with no hand-holding.
- [ ] A backup video of the demo exists.
- [ ] Every model has metrics, a baseline, an error analysis and a model card.
- [ ] CI is green, with unit, integration and e2e tests, and a coverage number.
- [ ] The ZAP report has 0 high findings.
- [ ] We have SUS before and after, with real users.
- [ ] We have a load-test result with p95.
- [ ] The README has been tested by an outsider.
- [ ] The report has the traceability table, C4 diagrams, ER diagram, ADRs, POPIA/ethics, and an honest reflection.
- [ ] Every team member can explain any part of the system (examiners often ask the "wrong" person).
- [ ] The git history and issues show steady, shared work.
- [ ] Nothing is deployed in the last 24 hours before the demo.

---

## 5. What I can do (Claude Code) to speed this up

- Turn the e2e suite into `tests/e2e` and set up the CI jobs.
- Write the unit tests for the business rules.
- Make the migration scripts safe with versioning.
- Do the k6/Locust load tests and the ZAP baseline, then fix the findings.
- Generate the ER and C4 diagrams from the code.
- Build the evaluation notebooks once the data is there, including baselines.
- Write the demo reset script.
- Fill in the Swagger descriptions, and draft the ADRs and the ops document.

Pick a workstream and I'll start on it.
