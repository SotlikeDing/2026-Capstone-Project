"""Phase 7: admin console (every admin endpoint) + AI helper. Every change is reverted."""
from st import *

state = json.load(open(os.path.join(HERE, "staging_state.json")))
U = state["users"]
H = lambda k: bearer(U[k]["token"])
uid = lambda k: U[k]["userId"]
ADMIN = admin_headers()
ADMIN_ID = web_login(*require_admin())[1]["userId"]
A = "admin"
main = state["bookings"]["main"]

for path in ["ping", "health", "audit-logs", "feature-flags", "role-verifications", "document-verifications", "bookings",
             "flagged-messages", "flagged-ratings", "rating-disputes", "settings"]:
    r = requests.get(f"{WEB}/api/admin/{path}", headers=ADMIN)
    check(A, f"GET /api/admin/{path}", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{WEB}/api/admin/health", headers=ADMIN)
print("    health:", r.text[:300])
r = requests.get(f"{WEB}/api/admin/audit-logs/export", headers=ADMIN)
check(A, "audit log CSV export", r.ok and len(r.content) > 0, f"{r.status_code} {r.headers.get('content-type')}")
r = requests.post(f"{WEB}/api/admin/audit-logs", headers=ADMIN, json={"adminUserId": ADMIN_ID, "adminUserName": "E2E", "action": "E2E_TEST", "targetEntityType": "Test", "targetEntityId": "1", "reason": "E2E"})
check(A, "write an audit log entry", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{WEB}/api/admin/audit-logs", headers=ADMIN, params={"action": "E2E_TEST"})
check(A, "filter audit logs by action", r.ok and "E2E_TEST" in r.text, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/admin/audit-logs", headers=ADMIN, json={"adminUserId": 1, "adminUserName": "Iemand anders", "action": "E2E_SPOOF", "targetEntityType": "Test", "targetEntityId": "1", "reason": "x"})
spoof = requests.get(f"{WEB}/api/admin/audit-logs", headers=ADMIN, params={"action": "E2E_SPOOF"}).text
check(A, "audit log records the real admin, not a body-supplied one", "Iemand anders" not in spoof, spoof[:200])

# feature flag toggle round-trip
flags = requests.get(f"{WEB}/api/admin/feature-flags", headers=ADMIN).json()
if flags:
    f0 = flags[0]
    key = f0.get("key") or f0.get("flagKey"); was = bool(f0.get("isEnabled"))
    r = requests.post(f"{WEB}/api/admin/feature-flags/toggle", headers=ADMIN, json={"key": key, "isEnabled": not was, "reason": "E2E"})
    check(A, f"toggle feature flag '{key}'", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
    now = next((f for f in requests.get(f"{WEB}/api/admin/feature-flags", headers=ADMIN).json() if (f.get("key") or f.get("flagKey")) == key), {})
    check(A, "flag value changed", bool(now.get("isEnabled")) == (not was), now)
    requests.post(f"{WEB}/api/admin/feature-flags/toggle", headers=ADMIN, json={"key": key, "isEnabled": was, "reason": "E2E revert"})

# settings update round-trip + validation
settings = requests.get(f"{WEB}/api/admin/settings", headers=ADMIN).json()
s0 = next((s for s in settings if s["key"] == "RatingRateLimitPermitLimit"), None)
if s0:
    r = requests.post(f"{WEB}/api/admin/settings/update", headers=ADMIN, json={"key": s0["key"], "value": "11", "reason": "E2E"})
    check(A, "update a system setting", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
    r = requests.post(f"{WEB}/api/admin/settings/update", headers=ADMIN, json={"key": s0["key"], "value": "not-a-number", "reason": "E2E"})
    check(A, "invalid setting value refused", r.status_code == 400, f"{r.status_code} {r.text[:150]}")
    requests.post(f"{WEB}/api/admin/settings/update", headers=ADMIN, json={"key": s0["key"], "value": s0["value"], "reason": "E2E revert"})
r = requests.get(f"{WEB}/api/Settings/public")
check(A, "public settings don't expose private ones", r.ok and "RateLimit" not in r.text, r.text[:200])

# suspend / reinstate
_, mj = mobile_login(U["ap"]["email"], state["password"])          # an app session from before the suspension
app_token = (mj or {}).get("accessToken")
r = requests.post(f"{WEB}/api/admin/users/{uid('ap')}/status", headers=ADMIN, json={"status": "Suspended", "reason": "E2E"})
check(A, "suspend a user", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{WEB}/api/user/session", headers=H("ap"))
check(A, "suspended user's website session is refused (session check)", r.status_code == 401, r.status_code)
if app_token:
    r = requests.get(f"{MOBILE}/api/mobile-access/me", headers=bearer(app_token))
    check(A, "suspended user's app session is refused too", r.status_code == 401, f"{r.status_code} {r.text[:120]}")
    r = requests.post(f"{MOBILE}/api/auth/refresh", json={"refreshToken": mj.get("refreshToken"), "accessToken": app_token})
    check(A, "suspended user's app can't refresh its token", r.status_code == 401, r.status_code)
r, _ = web_login(U["ap"]["email"], state["password"])
check(A, "suspended user can't sign in", r.status_code in (401, 403), f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{WEB}/api/Chats", headers=H("ap"))   # a protected endpoint (getProfile is public)
check(A, "suspended user's existing token stops working", r.status_code in (401, 403), f"{r.status_code} - token issued before suspension still accepted")
r = requests.post(f"{WEB}/api/admin/users/{uid('ap')}/status", headers=ADMIN, json={"status": "Active", "reason": "E2E revert"})
check(A, "reinstate the user", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r, j = web_login(U["ap"]["email"], state["password"])
check(A, "reinstated user can sign in again", r.status_code == 200, r.status_code)
users = requests.get(f"{WEB}/api/user/getAllUsers", headers=ADMIN).json().get("data", [])
ap_row = next((x for x in users if x.get("userId") == uid("ap")), {})
check(A, "reactivated account is 'Active' (not 'Approved') and not suspended", ap_row.get("accountStatus") == "Active" and ap_row.get("isSuspended") is False, ap_row)
r = requests.post(f"{WEB}/api/admin/users/{uid('ap')}/status", headers=ADMIN, json={"status": "Approved", "reason": "E2E old button"})
check(A, "old 'Approved' value is stored as Active", r.status_code == 200, r.status_code)
if j: U["ap"]["token"] = j["accessToken"]
time.sleep(11)   # admin endpoints allow 30 requests per 10 s
r = requests.post(f"{WEB}/api/admin/users/{ADMIN_ID}/status", headers=ADMIN, json={"status": "Suspended", "reason": "E2E self"})
check(A, "admin can't suspend themselves", r.status_code == 400, f"{r.status_code} {r.text[:150]}")
if r.ok:
    requests.post(f"{WEB}/api/admin/users/{ADMIN_ID}/status", headers=ADMIN, json={"status": "Active", "reason": "E2E revert"})

# feature flags take effect (each is switched back straight away)
def set_flag(key, on):
    time.sleep(1)
    r = requests.post(f"{WEB}/api/admin/feature-flags/toggle", headers=ADMIN, json={"key": key, "isEnabled": on, "reason": "E2E"})
    check(A, f"switch flag {key} {'AAN' if on else 'AF'}", r.status_code == 200, f"{r.status_code} {r.text[:100]}")
    return r

time.sleep(11)   # admin endpoints allow 30 requests per 10 s

set_flag("Enable_New_User_Registrations", False)
try:
    r = requests.post(f"{WEB}/api/user/register", json={"first_name": "Flag", "surname": "Toets", "email": f"flag-{int(time.time())}@staging-e2e.test",
                      "password": state["password"], "phone": "0821234567", "primary_role_code": "Jagter", "selected_roles": ["Jagter"], "terms_accepted": True})
    check(A, "registrations flag OFF: new accounts refused (403)", r.status_code == 403, f"{r.status_code} {r.text[:150]}")
    st = requests.get(f"{WEB}/api/settings/status").json()
    check(A, "site status reports registrations closed", st.get("registrationsOpen") is False, st)
finally:
    set_flag("Enable_New_User_Registrations", True)

set_flag("Enable_Maintenance_Mode", True)
try:
    r = requests.get(f"{WEB}/api/packets/getPackets")
    check(A, "maintenance ON: visitors get 503 with a message", r.status_code == 503 and "onderhoud" in r.text, f"{r.status_code} {r.text[:120]}")
    r = requests.get(f"{WEB}/api/Chats", headers=H("pj"))
    check(A, "maintenance ON: signed-in non-admins get 503", r.status_code == 503, r.status_code)
    r = requests.get(f"{WEB}/api/admin/ping", headers=ADMIN)
    check(A, "maintenance ON: admins keep working", r.status_code == 200, r.status_code)
    r, _ = web_login(ADMIN_EMAIL, ADMIN_PASSWORD)
    check(A, "maintenance ON: sign-in stays open (admins must get in)", r.status_code == 200, r.status_code)
    check(A, "site status reports maintenance", requests.get(f"{WEB}/api/settings/status").json().get("maintenance") is True)
finally:
    set_flag("Enable_Maintenance_Mode", False)
time.sleep(16)   # the MobileAPI reads the flag at most every 15 s

h = requests.get(f"{WEB}/api/admin/health", headers=ADMIN).json()
check(A, "health reports the MobileAPI", h.get("mobileApiHealthy") is True, h)
check(A, "health reports app sessions and flags", isinstance(h.get("appSessions"), int) and h.get("maintenanceMode") is False and h.get("registrationsOpen") is True, h)

# role verifications
r = requests.get(f"{WEB}/api/admin/role-verifications", headers=ADMIN)
rv = r.json() if r.ok else []
print(f"    role verifications pending: {len(rv)}")
mine = next((x for x in rv if "staging-e2e" in json.dumps(x)), None)
if mine:
    vid = mine.get("id") or mine.get("verificationId")
    r = requests.post(f"{WEB}/api/admin/role-verifications/{vid}/decision", headers=ADMIN, json={"approve": True, "reason": "E2E"})
    check(A, "approve a role verification", r.status_code == 200, f"{r.status_code} {r.text[:150]}")

# booking correction (pause first: the admin endpoints allow 30 requests per 10 s, and the calls above use most of that)
time.sleep(11)
r = requests.post(f"{WEB}/api/admin/bookings/{main}/correct", headers=ADMIN, json={"correctedTotalPrice": 51000, "correctedHunterCount": 2, "correctedObserverCount": 1, "reason": "E2E korreksie"})
check(A, "admin corrects a booking", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{WEB}/api/bookings/getAuditLogs/{main}", headers=ADMIN)
check(A, "correction appears in booking audit log", r.ok and "korreksie" in r.text.lower(), f"{r.status_code} {r.text[:200]}")

# admin-only listings
for path in ["/api/user/getAllUsers", "/api/Story/getStories", "/api/Weapon/getWeapons", "/api/Permit/getPermit",
             "/api/huntingpermit/getApplication", "/api/bookings/getBookingRequest"]:
    r = requests.get(WEB + path, headers=ADMIN)
    check(A, f"GET {path}", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{WEB}/api/user/getAllUsers", headers=ADMIN)
check(A, "user list doesn't include password hashes", r.ok and "pbkdf2" not in r.text and "password" not in r.text.lower(), r.text[:200])

# ------------------------------------------------------------------ AI helper
A = "ai"
r = requests.get(f"{WEB}/api/AiHelper/status", headers=H("pj"))
check(A, "AI status", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
print("    ai status:", r.text[:250])
t = time.time()
r = requests.post(f"{WEB}/api/AiHelper/optimize", headers=H("pj"), timeout=200, json={
    "roleCode": "PJ", "province": "Limpopo", "currency": "ZAR", "goal": "balanced", "month": 6,
    "draft": {"title": "Koedoe safari", "description": "4 dae koedoe en rooibok", "price": 45000, "durationDays": 4, "species": ["Koedoe", "Rooibok"], "services": ["Lodge"]}})
adv = r.json() if r.ok else {}
check(A, f"package price advice ({time.time() - t:.0f}s)", r.ok, f"{r.status_code} {r.text[:200]}")
print("    advice:", json.dumps(adv)[:400])
aid = adv.get("adviceId")
if aid:
    r = requests.post(f"{WEB}/api/AiHelper/feedback", headers=H("pj"), json={"adviceId": aid, "helpful": True, "comment": "E2E"})
    check(A, "advice feedback", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/AiHelper/team-matches", headers=H("pj"), timeout=200, json={"roleCode": "PJ", "province": "Limpopo", "wantRoles": ["PH", "VG"], "take": 5})
check(A, "team matches", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
check(A, "team matches include the approved E2E PH/VG", "E2E" in r.text, r.text[:250])
r = requests.post(f"{WEB}/api/AiHelper/optimize", headers=H("pj"), json={"roleCode": "PJ"})
check(A, "empty draft handled gracefully (no 500)", r.status_code < 500, f"{r.status_code} {r.text[:150]}")
for path in ["admin/observations", "admin/advice-log"]:
    r = requests.get(f"{WEB}/api/AiHelper/{path}", headers=ADMIN)
    check(A, f"GET {path}", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/AiHelper/admin/dry-run", headers=ADMIN, timeout=200, json={"roleCode": "PH", "province": "Limpopo", "currency": "ZAR", "draft": {"price": 4500, "yearsExperience": 10}})
check(A, "admin dry-run", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/AiHelper/admin/reload-models", headers=ADMIN)
check(A, "admin reload models", r.status_code == 200, f"{r.status_code} {r.text[:150]}")

json.dump(state, open(os.path.join(HERE, "staging_state.json"), "w"), indent=1)
save_results("t7_admin")
