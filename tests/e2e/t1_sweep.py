"""Phase 1: hit all endpoints anonymously and as a low-privilege hunter. Flags 5xx, auth bypasses and role leaks."""
import re
from st import *

inv = json.load(open(os.path.join(HERE, "inventory.json")))
state = json.load(open(os.path.join(HERE, "staging_state.json")))
hunter = state["users"]["hunter"]
web_hunter = bearer(hunter["token"])
r, mj = mobile_login(hunter["email"], state["password"], hunter["roles"][0]["userRoleId"])
check("mobile", "hunter mobile login with selected role", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
mobile_hunter = bearer(mj["accessToken"]) if mj and mj.get("accessToken") else {}

rows = []


def call(api, verb, route, headers, body, form):
    base = WEB if api == "web" else MOBILE
    url = base + re.sub(r"\{[^}]+\}", "999999", route).replace("?", "")
    kw = {"headers": headers, "timeout": 40, "allow_redirects": False}
    if body:
        kw["json"] = {}
    if form:
        kw["files"] = {"file": ("x.txt", b"x", "text/plain")}
    try:
        return requests.request(verb, url, **kw)
    except requests.RequestException as e:
        class R: status_code = 0; text = str(e)
        return R()


for e in inv:
    if e["route"] in ("/api/PayFast/itn",):          # external callback, tested separately
        continue
    anon = call(e["api"], e["verb"], e["route"], {}, e["body"], e["form"])
    hdr = web_hunter if e["api"] == "web" else mobile_hunter
    auth = call(e["api"], e["verb"], e["route"], hdr, e["body"], e["form"])
    rows.append({**e, "anon": anon.status_code, "hunter": auth.status_code,
                 "anon_body": anon.text[:160], "hunter_body": auth.text[:160]})
    name = f"{e['verb']} {e['route']}"
    if e["auth"] == "auth":
        check("sweep-anon", f"{name} requires login", anon.status_code == 401, f"anon got {anon.status_code}: {anon.text[:120]}")
    else:
        check("sweep-anon", f"{name} no server error anonymously", anon.status_code < 500 and anon.status_code != 0, f"anon got {anon.status_code}: {anon.text[:120]}")
    if e["roles"] and any("Admin" in x for x in e["roles"]):
        check("sweep-hunter", f"{name} admin-only", auth.status_code == 403, f"hunter got {auth.status_code}: {auth.text[:120]}")
    else:
        check("sweep-hunter", f"{name} no server error as hunter", auth.status_code < 500 and auth.status_code != 0, f"hunter got {auth.status_code}: {auth.text[:120]}")

json.dump(rows, open(os.path.join(HERE, "sweep_rows.json"), "w"), indent=1)
save_results("t1_sweep")
