"""Phase 2: profile, role registration, role detail CRUD, PH rates, subscriptions, IDOR checks."""
from st import *

state = json.load(open(os.path.join(HERE, "staging_state.json")))
U = state["users"]
H = lambda k: bearer(U[k]["token"])
uid = lambda k: U[k]["userId"]
ur = lambda k: U[k]["roles"][0]["userRoleId"]
A = "profile"

# --- own profile
r = requests.put(f"{WEB}/api/profile/updateProfile/{uid('hunter')}", headers=H("hunter"),
                 json={"name": "E2EHunter", "surname": "Toets", "phone": "0820000001", "idPassportNum": "9001015009087"})
check(A, "update own profile", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/profile/getProfile/{uid('hunter')}", headers=H("hunter"))
p = r.json() if r.ok else {}
check(A, "own profile reflects update", p.get("phone") == "0820000001" and p.get("idPassportNum") == "9001015009087", p)

# --- PII exposure: anonymous and other users reading someone's profile
r = requests.get(f"{WEB}/api/profile/getProfile/{uid('hunter')}")
anon = r.json() if r.ok else {}
check(A, "anonymous profile read hides ID/passport number", not anon.get("idPassportNum"), {k: anon.get(k) for k in ("email", "phone", "idPassportNum")})
check(A, "anonymous profile read hides email + phone", not anon.get("email") and not anon.get("phone"), {k: anon.get(k) for k in ("email", "phone")})
r = requests.get(f"{WEB}/api/profile/getProfile/{uid('hunter')}", headers=H("hunter2"))
other = r.json() if r.ok else {}
check(A, "another user can't read your ID/passport number", not other.get("idPassportNum"), other.get("idPassportNum"))

# --- IDOR on writes
r = requests.put(f"{WEB}/api/profile/updateProfile/{uid('hunter')}", headers=H("hunter2"), json={"name": "Hacked"})
check(A, "can't update someone else's profile (403)", r.status_code == 403, r.status_code)
r = requests.post(f"{WEB}/api/profile/addRole/{uid('hunter')}", headers=H("hunter2"), json={"roleCode": "PH"})
check(A, "can't add a role to someone else (403)", r.status_code == 403, r.status_code)
r = requests.delete(f"{WEB}/api/profile/deleteUser/{uid('hunter')}", headers=H("hunter2"))
check(A, "can't delete someone else's account (403)", r.status_code == 403, r.status_code)
r = requests.post(f"{WEB}/api/profile/addRole/{uid('hunter2')}", headers=H("hunter2"), json={"roleCode": "ADMIN"})
check(A, "can't grant yourself ADMIN (403)", r.status_code == 403, f"{r.status_code} {r.text[:150]}")

# --- role detail registration (creates hunter/farmer/ph/pj/vg rows)
reqs = {
    "hunter": ("registerHunter", {"country": "South Africa", "username": "e2ehunter", "idPassportNumber": "9001015009087", "bio": "Toetsjagter"}),
    "hunter2": ("registerHunter", {"country": "Namibia", "username": "e2ehunter2", "idPassportNumber": "8801015009088", "bio": "Tweede jagter"}),
    "farmer": ("registerFarmer", {"businessName": "E2E Toetsplaas", "businessRegistrationNumber": "2026/000001/07", "region": "Limpopo", "bio": "Toets"}),
    "ph": ("registerPh", {"provincialLicenses": "LP-PH-001", "hasDangerousGameEndorsement": True, "dangerousGameDetails": "Buffel",
                          "associationName": "PHASA", "associationMembershipNumber": "123", "firstAidCertification": "Level 2",
                          "firstAidExpiryDate": "2027-12-31", "firearmCompetencyLicenses": "C-123", "yearsOfExperience": 12, "bio": "Toets PH"}),
    "pj": ("registerPj", {"businessName": "E2E Safaris", "businessRegistrationNumber": "2026/000002/07", "taxPinVat": "4000000000",
                          "physicalAddress": "1 Toetsstraat, Polokwane", "provincialLicenses": "LP-PJ-1", "yearsOfExperience": 8, "bio": "Toets PJ"}),
    "vg": ("registerVg", {"hasDrone": True, "sacaaRegistrationNumber": "SACAA-1", "rplLicenseNumber": "RPL-1", "rocCertificateNumber": "ROC-1",
                          "droneLicence": "D-1", "companyName": "E2E Films", "companyRegistrationNumber": "2026/000003/07", "yearsOfExperience": 6}),
}
for k, (ep, body) in reqs.items():
    r = requests.post(f"{WEB}/api/profile/{ep}/{uid(k)}", headers=H(k), json=body)
    check(A, f"{ep} for {k}", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/profile/registerAp/{uid('ap')}", headers=H("ap"))
check(A, "registerAp", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/profile/registerPh/{uid('ph')}", headers=H("hunter"), json=reqs["ph"][1])
check(A, "can't register a role for someone else (403)", r.status_code == 403, r.status_code)

# --- read + update each role detail
for k, kind in [("hunter", "Hunter"), ("farmer", "Farmer"), ("ph", "Ph"), ("pj", "Pj"), ("vg", "Vg")]:
    r = requests.get(f"{WEB}/api/profile/get{kind}/{ur(k)}", headers=H(k))
    check(A, f"get{kind} own", r.status_code == 200 and r.text not in ("", "null"), f"{r.status_code} {r.text[:200]}")
    body = dict(reqs[k][1]) if k in reqs else {}
    body["bio"] = f"Bygewerk {kind}"
    r = requests.put(f"{WEB}/api/profile/update{kind}/{ur(k)}", headers=H(k), json=body)
    check(A, f"update{kind} own", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
    r = requests.put(f"{WEB}/api/profile/update{kind}/{ur(k)}", headers=H("hunter2"), json=body)
    check(A, f"update{kind} of someone else refused (403)", r.status_code == 403, r.status_code)

# --- PH daily rates
r = requests.post(f"{WEB}/api/profile/phRates/{uid('ph')}", headers=H("ph"),
                  json={"userId": uid("ph"), "standardDailyRate": 4500, "dangerousGameDailyRate": 7500, "hasDangerousGameAccreditation": True, "currency": "ZAR"})
check(A, "PH saves daily rates", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/profile/phRates/{uid('ph')}")
check(A, "PH rates readable publicly", r.ok and float((r.json() or {}).get("standardDailyRate") or 0) == 4500, r.text[:200])
r = requests.post(f"{WEB}/api/profile/phRates/{uid('ph')}", headers=H("hunter"), json={"userId": uid("ph"), "standardDailyRate": 1})
check(A, "can't change someone else's PH rates (403)", r.status_code == 403, r.status_code)

# --- subscriptions
r = requests.get(f"{WEB}/api/profile/subscription/{uid('ph')}/PH", headers=H("ph"))
check(A, "PH reads subscription", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/profile/subscription/{uid('ph')}/PH", headers=H("ph"),
                  json={"userId": uid("ph"), "roleCode": "PH", "isActive": True, "monthlyFee": 299, "tierName": "Standard Advertiser", "paymentStatus": "Paid", "activeDurationMonths": 12})
sub_saved = r.status_code == 200
check(A, "subscription can't be activated without paying (409)", r.status_code == 409, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/profile/subscription/{uid('ph')}/PH", headers=H("ph"))
s = r.json() if r.ok else {}
check(A, "user can't mark their own subscription Paid without paying", s.get("paymentStatus") != "Paid", s)

# --- add / remove role
r = requests.post(f"{WEB}/api/profile/addRole/{uid('hunter2')}", headers=H("hunter2"), json={"roleCode": "AP"})
check(A, "add AP role to self", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/profile/addRole/{uid('hunter2')}", headers=H("hunter2"), json={"roleCode": "AP"})
check(A, "adding the same role twice refused", r.status_code == 400, f"{r.status_code} {r.text[:200]}")
r = requests.delete(f"{WEB}/api/profile/deleteRoleByCode/{uid('hunter2')}/AP", headers=H("hunter2"))
check(A, "remove AP role from self", r.status_code == 200, f"{r.status_code} {r.text[:200]}")

# --- refresh token (documented bug: re-issues a token for UserId 1)
r = requests.post(f"{WEB}/api/user/refreshToken", json={"email": U["hunter"]["email"], "refreshToken": U["hunter"]["refresh"]})
if r.ok and r.json().get("accessToken"):
    claims = json.loads(base64.urlsafe_b64decode(r.json()["accessToken"].split(".")[1] + "=="))
    check("auth", "refreshed token is for the same user", claims.get("nameid") == str(uid("hunter")), f"refreshed token nameid={claims.get('nameid')} role={claims.get('role')}")
else:
    check("auth", "refresh token works", False, f"{r.status_code} {r.text[:200]}")

# --- logout revokes the token
r, j = web_login(U["hunter2"]["email"], state["password"])
t = j["accessToken"]
requests.post(f"{WEB}/api/user/logout", json={"token": t, "accessToken": t})
r = requests.get(f"{WEB}/api/profile/getHunter/{ur('hunter2')}", headers=bearer(t))
check("auth", "token rejected after logout", r.status_code == 401, r.status_code)

# --- password reset endpoints (error paths; codes go by email)
r = requests.post(f"{WEB}/api/user/forgotPassword", json={"email": "nobody@staging-e2e.test"})
check("auth", "forgot password for unknown email doesn't reveal existence", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/user/resetPassword", json={"email": U["hunter"]["email"], "token": "bogus", "newPassword": "Another!Pass2026"})
check("auth", "reset with bogus token refused", r.status_code >= 400, f"{r.status_code} {r.text[:150]}")

# --- verification code brute force: a fresh unverified account, 6 wrong guesses -> locked (429)
bf = f"bruteforce-{int(time.time())}@staging-e2e.test"
requests.post(f"{WEB}/api/user/register", json={"first_name": "BF", "surname": "Toets", "email": bf, "password": state["password"],
              "phone": "0821234567", "primary_role_code": "Jagter", "selected_roles": ["Jagter"], "terms_accepted": True})
codes = [requests.get(f"{WEB}/api/user/verifyEmail", params={"email": bf, "token": f"{100000 + i}"}).status_code for i in range(7)]
check("auth", "email verification guesses are locked after 5 (429)", 429 in codes, f"statuses {codes}")
sql(f"UPDATE users SET uIsSuspended = 1 WHERE uEmail = '{bf}'")

# --- mail bombing: at most 3 reset mails per address per 15 minutes
victim = f"mailbomb-{int(time.time())}@staging-e2e.test"
codes = [requests.post(f"{WEB}/api/user/forgotPassword", json={"email": victim}).status_code for _ in range(4)]
check("auth", "4th password-reset mail to one address within 15 min is refused (429)", codes[:3] == [200] * 3 and codes[3] == 429, f"statuses {codes}")

# --- the app's sign-in locks an address after 5 wrong passwords (it had no lockout before)
guess = f"applock-{int(time.time())}@staging-e2e.test"
codes = [mobile_login(guess, "Wrong!Password1")[0].status_code for _ in range(6)]
check("auth", "app sign-in locks an address after 5 wrong passwords (429)", codes[:5] == [401] * 5 and codes[5] == 429, f"statuses {codes}")

# --- server errors don't leak SQL/exception details
r = requests.get(f"{WEB}/api/bookings/getAuditLogs/abc", headers=bearer(U["hunter"]["token"]))
check("auth", "malformed request doesn't leak exception details", "Exception" not in r.text and "SqlClient" not in r.text, f"{r.status_code} {r.text[:200]}")

save_results("t2_profile")
json.dump(state, open(os.path.join(HERE, "staging_state.json"), "w"), indent=1)
