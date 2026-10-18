"""Shared helpers for the end-to-end suites. Every address and credential comes from the environment.

Required for the full suite (see README.md):
  E2E_ADMIN_EMAIL, E2E_ADMIN_PASSWORD   an existing admin account on the target
  E2E_SQL_SERVER, E2E_SQL_USER, E2E_SQL_PASSWORD, E2E_SQL_DATABASE
                                        a SQL login for the few steps the API can't do
                                        (mark the new test accounts' e-mail verified, move a
                                        booking into the past so it counts as finished)
Optional:
  E2E_WEB_URL / E2E_MOBILE_URL / E2E_APP_URL   default: staging through the proxy names (the
                                               containers refuse direct IP:port requests)
  E2E_RUN                                      suffix for this run's test accounts (default: a timestamp)
  E2E_OUT                                      where state, results and screenshots go (default: ./out)
"""
import base64, hashlib, hmac, json, os, shutil, subprocess, time
import requests
import urllib3

WEB = os.environ.get("E2E_WEB_URL", "http://webapi.mikpunt.ddns.net").rstrip("/")
MOBILE = os.environ.get("E2E_MOBILE_URL", "http://mobileapi.mikpunt.ddns.net").rstrip("/")
APP = os.environ.get("E2E_APP_URL", "http://webapp.mikpunt.ddns.net").rstrip("/")
ADMIN_EMAIL = os.environ.get("E2E_ADMIN_EMAIL", "")
ADMIN_PASSWORD = os.environ.get("E2E_ADMIN_PASSWORD", "")

# Outputs (state shared between suites, results, screenshots) live outside the source folder.
HERE = os.environ.get("E2E_OUT") or os.path.join(os.path.dirname(os.path.abspath(__file__)), "out")
os.makedirs(HERE, exist_ok=True)

NAMEID = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
ROLE = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
# The public placeholder keys from the repo's appsettings: a deployment must refuse tokens signed with them.
WEB_PLACEHOLDER = "DEV-ONLY-PLACEHOLDER-DO-NOT-USE-IN-PRODUCTION-set-a-real-one-via-dotnet-user-secrets-set-Jwt-Key"
MOBILE_PLACEHOLDER = "CHANGE-THIS-TO-A-LONG-RANDOM-SECRET-AT-LEAST-32-CHARS"

RESULTS = []          # (area, name, ok, detail)

# The target is reached over a VPN that sometimes drops for a minute. Retry when a connection can't be
# made (nothing was sent, so even a POST is safe to repeat); never retry after a request went out.
_session = requests.Session()
_adapter = requests.adapters.HTTPAdapter(max_retries=urllib3.util.Retry(total=6, connect=6, read=0, status=0, other=0,
                                                                         backoff_factor=5, allowed_methods=None,
                                                                         status_forcelist=(), respect_retry_after_header=False))
_session.mount("http://", _adapter)
_session.mount("https://", _adapter)
requests.api.request = lambda method, url, **kwargs: _session.request(method, url, **kwargs)


def check(area, name, ok, detail=""):
    RESULTS.append((area, name, bool(ok), "" if ok else str(detail)[:400]))
    print(("PASS " if ok else "FAIL ") + f"[{area}] {name}" + ("" if ok else f"  -> {str(detail)[:400]}"), flush=True)
    return bool(ok)


def require_admin():
    if not (ADMIN_EMAIL and ADMIN_PASSWORD):
        raise SystemExit("Set E2E_ADMIN_EMAIL and E2E_ADMIN_PASSWORD (an admin account on the target).")
    return ADMIN_EMAIL, ADMIN_PASSWORD


def b64(d):
    return base64.urlsafe_b64encode(d).rstrip(b"=").decode()


def forge(key, iss, aud, claims, minutes=60):
    now = int(time.time())
    head = b64(json.dumps({"alg": "HS256", "typ": "JWT"}).encode()) + "." + \
        b64(json.dumps({**claims, "iss": iss, "aud": aud, "nbf": now - 5, "iat": now, "exp": now + minutes * 60}).encode())
    return head + "." + b64(hmac.new(key.encode(), head.encode(), hashlib.sha256).digest())


def bearer(token):
    return {"Authorization": f"Bearer {token}"}


def web_login(email, password):
    r = requests.post(f"{WEB}/api/user/signin", json={"email": email, "password": password}, timeout=30)
    return r, (r.json() if r.ok else None)


def admin_headers():
    r, j = web_login(*require_admin())
    if not j:
        raise SystemExit(f"Admin sign-in failed: {r.status_code} {r.text[:200]}")
    return bearer(j["accessToken"])


def mobile_login(email, password, user_role_id=None):
    body = {"email": email, "password": password}
    if user_role_id:
        body["userRoleId"] = user_role_id
    r = requests.post(f"{MOBILE}/api/auth/login", json=body, timeout=30)
    return r, (r.json() if r.headers.get("content-type", "").startswith("application/json") else None)


def sql(query):
    """Run a query and return its rows as 'a|b' lines. Uses sqlcmd when installed, else pymssql (CI)."""
    server = os.environ.get("E2E_SQL_SERVER", "")
    user = os.environ.get("E2E_SQL_USER", "")
    password = os.environ.get("E2E_SQL_PASSWORD", "")
    database = os.environ.get("E2E_SQL_DATABASE", "Mikpunt")
    if not (server and user and password):
        raise SystemExit("Set E2E_SQL_SERVER, E2E_SQL_USER and E2E_SQL_PASSWORD for the direct-database steps.")
    if shutil.which("sqlcmd"):
        out = subprocess.run(["sqlcmd", "-S", server, "-U", user, "-d", database, "-N", "-C", "-W", "-h", "-1", "-s", "|",
                              "-Q", "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; " + query],
                             capture_output=True, text=True, env={**os.environ, "SQLCMDPASSWORD": password})
        return out.stdout.strip()
    import pymssql  # noqa: PLC0415 - only needed where sqlcmd isn't installed
    host, _, port = server.partition(",")
    with pymssql.connect(server=host, port=int(port or 1433), user=user, password=password, database=database,
                         autocommit=True, tds_version="7.4") as conn:
        cur = conn.cursor()
        cur.execute("SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; " + query)
        rows = []
        while True:
            if cur.description:
                rows += ["|".join("" if v is None else str(v) for v in row) for row in cur.fetchall()]
            if not cur.nextset():
                break
        return "\n".join(rows)


def save_results(name):
    json.dump(RESULTS, open(os.path.join(HERE, f"results_{name}.json"), "w", encoding="utf-8"), indent=1)
    failed = [r for r in RESULTS if not r[2]]
    print(f"\n=== {name}: {len(RESULTS) - len(failed)}/{len(RESULTS)} passed ===")
