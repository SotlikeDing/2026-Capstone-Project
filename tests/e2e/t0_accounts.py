"""Phase 0: create one test account per role via the real registration API."""
import secrets
from st import *

PW = "E2e!" + secrets.token_urlsafe(12) + "a1"   # throwaway password for this run's test accounts
RUN = os.environ.get("E2E_RUN") or time.strftime("r%m%d%H%M")
ACCOUNTS = {
    "hunter": ("Jagter", ["Jagter"]),
    "hunter2": ("Jagter", ["Jagter"]),
    "farmer": ("Boer", ["Boer"]),
    "ph": ("PH", ["PH"]),
    "pj": ("PJ", ["PJ"]),
    "vg": ("VG", ["VG"]),
    "ap": ("AP", ["AP"]),
}
state = {"password": PW, "users": {}}

for key, (primary, selected) in ACCOUNTS.items():
    email = f"{key}-{RUN}@staging-e2e.test"
    body = {"first_name": f"E2E{key.capitalize()}", "surname": "Toets", "email": email, "password": PW,
            "phone_country_code": "+27", "phone": "0821234567", "primary_role_code": primary,
            "selected_roles": selected, "terms_accepted": True}
    r = requests.post(f"{WEB}/api/user/register", json=body, timeout=30)
    check("register", f"register {key} ({primary})", r.status_code == 200, f"{r.status_code} {r.text[:300]}")

# Duplicate email, weak password, admin role and missing terms must be refused.
r = requests.post(f"{WEB}/api/user/register", json={**body, "email": f"hunter-{RUN}@staging-e2e.test"}, timeout=30)
check("register", "duplicate email refused", r.status_code == 400, r.text[:200])
r = requests.post(f"{WEB}/api/user/register", json={**body, "email": "weak@staging-e2e.test", "password": "short"}, timeout=30)
check("register", "weak password refused", r.status_code == 400, r.text[:200])
r = requests.post(f"{WEB}/api/user/register", json={**body, "email": "adm@staging-e2e.test", "primary_role_code": "ADMIN"}, timeout=30)
check("register", "self-registering as admin refused", r.status_code == 400, r.text[:200])
r = requests.post(f"{WEB}/api/user/register", json={**body, "email": "noterms@staging-e2e.test", "terms_accepted": False}, timeout=30)
check("register", "terms not accepted refused", r.status_code == 400, r.text[:200])
r = requests.post(f"{WEB}/api/user/register", json={**body, "email": f"HUNTER-{RUN}@STAGING-E2E.TEST"}, timeout=30)
check("register", "duplicate email in different case refused", r.status_code == 400, f"{r.status_code} {r.text[:200]}")

# Unverified login must be refused; wrong verification code refused.
r, _ = web_login(f"hunter-{RUN}@staging-e2e.test", PW)
check("auth", "unverified account cannot sign in", r.status_code in (401, 403), f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/user/verifyEmail", params={"email": f"hunter-{RUN}@staging-e2e.test", "token": "000000"}, timeout=30)
check("auth", "wrong verification code refused", r.status_code >= 400 or '"success":false' in r.text.lower(), f"{r.status_code} {r.text[:200]}")

print(sql("UPDATE users SET uIsEmailVerified = 1 WHERE uEmail LIKE '%-' + '"+RUN+"' + '@staging-e2e.test'; SELECT COUNT(*) FROM users WHERE uEmail LIKE '%@staging-e2e.test'"))

for key in ACCOUNTS:
    email = f"{key}-{RUN}@staging-e2e.test"
    r, j = web_login(email, PW)
    ok = check("auth", f"web sign-in {key}", r.status_code == 200 and j and j.get("accessToken"), f"{r.status_code} {r.text[:200]}")
    if ok:
        state["users"][key] = {"email": email, "userId": j["userId"], "token": j["accessToken"], "refresh": j.get("refreshToken"),
                               "roles": [{"code": x["roleCode"], "userRoleId": x["id"]} for x in j.get("roles", [])]}
        print("   ", key, j["userId"], state["users"][key]["roles"])

r, _ = web_login(f"hunter-{RUN}@staging-e2e.test", "Wrong!Password1")
check("auth", "wrong password refused", r.status_code == 401, r.status_code)
json.dump(state, open(os.path.join(HERE, "staging_state.json"), "w"), indent=1)
save_results("t0_accounts")
