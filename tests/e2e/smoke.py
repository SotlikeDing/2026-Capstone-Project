"""Post-deploy smoke test: is the deployment up, secure and serving data? Runs in CI after every deploy.

Needs no credentials. With E2E_ADMIN_EMAIL / E2E_ADMIN_PASSWORD set it also signs in as admin
and checks the database health. Exit code 1 when anything fails.
"""
import os, sys, time

import requests

from st import APP, MOBILE, WEB, WEB_PLACEHOLDER, MOBILE_PLACEHOLDER, ADMIN_EMAIL, ADMIN_PASSWORD, bearer, check, forge, save_results

WAIT = int(os.environ.get("E2E_SMOKE_WAIT", "300"))


def wait_until_up():
    """compose up returns before the apps are ready (WebAPI runs its migrations first)."""
    deadline = time.time() + WAIT
    while True:
        try:
            if requests.get(f"{WEB}/health", timeout=5).status_code == 200 and \
               requests.get(f"{APP}/", timeout=5).status_code == 200 and \
               requests.post(f"{MOBILE}/api/auth/login", json={}, timeout=5).status_code < 500:
                return True
        except requests.RequestException:
            pass
        if time.time() > deadline:
            return False
        time.sleep(5)


A = "smoke"
check(A, f"all three services answer within {WAIT}s", wait_until_up(), "WebAPI /health, WebApp / or MobileAPI login not ready")

r = requests.get(f"{WEB}/health", timeout=10)
check(A, "WebAPI health (database reachable)", r.status_code == 200 and "Healthy" in r.text, f"{r.status_code} {r.text[:200]}")

r = requests.get(f"{APP}/", timeout=15)
check(A, "website home page renders", r.status_code == 200 and "MikPunt" in r.text, r.status_code)
for header in ["Content-Security-Policy", "X-Frame-Options", "X-Content-Type-Options", "Referrer-Policy"]:
    check(A, f"website sends {header}", header in r.headers, sorted(r.headers))

# Public data still loads (these crashed on one bad row before).
for path in ["/api/Farmer/getAllFarms", "/api/Ads/getAds", "/api/Settings/public"]:
    r = requests.get(WEB + path, timeout=20)
    check(A, f"GET {path}", r.status_code == 200, f"{r.status_code} {r.text[:200]}")

# Authentication is enforced and can't be faked.
r = requests.post(f"{WEB}/api/user/signin", json={"email": "smoke-nobody@staging-e2e.test", "password": "Wrong!Password1"}, timeout=20)
check(A, "website sign-in refuses a wrong password", r.status_code in (400, 401), r.status_code)
r = requests.post(f"{MOBILE}/api/auth/login", json={"email": "smoke-nobody@staging-e2e.test", "password": "Wrong!Password1"}, timeout=20)
check(A, "app sign-in refuses a wrong password", r.status_code in (400, 401, 429), r.status_code)
r = requests.get(f"{WEB}/api/Chats", timeout=20)
check(A, "protected endpoint needs a token", r.status_code == 401, r.status_code)
fake = forge(WEB_PLACEHOLDER, "MikPuntAPI", "MikPuntClients", {"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier": "1",
                                                                "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "Admin"})
r = requests.get(f"{WEB}/api/admin/ping", headers=bearer(fake), timeout=20)
check(A, "token signed with the public placeholder key is refused (WebAPI)", r.status_code == 401, r.status_code)
fake = forge(MOBILE_PLACEHOLDER, "MobileApi", "JagbestuurApp", {"sub": "1", "role": "Admin"})
r = requests.get(f"{MOBILE}/api/profile/rowland-ward-preference", headers=bearer(fake), timeout=20)
check(A, "token signed with the public placeholder key is refused (MobileAPI)", r.status_code == 401, r.status_code)

if ADMIN_EMAIL and ADMIN_PASSWORD:
    r = requests.post(f"{WEB}/api/user/signin", json={"email": ADMIN_EMAIL, "password": ADMIN_PASSWORD}, timeout=20)
    check(A, "admin can sign in", r.status_code == 200, r.status_code)
    if r.ok:
        r = requests.get(f"{WEB}/api/admin/health", headers=bearer(r.json()["accessToken"]), timeout=20)
        check(A, "admin health report", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
else:
    print("(E2E_ADMIN_EMAIL/E2E_ADMIN_PASSWORD not set - admin checks skipped)")

save_results("smoke")
sys.exit(0 if all(ok for _, _, ok, _ in __import__("st").RESULTS) else 1)
