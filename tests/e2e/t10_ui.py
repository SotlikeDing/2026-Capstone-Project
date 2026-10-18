"""Phase 10: every webapp page as anonymous + each role on staging (headless Chrome)."""
import re, sys
from playwright.sync_api import sync_playwright
from st import *

state = json.load(open(os.path.join(HERE, "staging_state.json")))
U = state["users"]
SHOTS = os.path.join(HERE, "ui_shots"); os.makedirs(SHOTS, exist_ok=True)
PUBLIC = ["/", "/oor-ons", "/kontak", "/privaatheid", "/bepalings", "/rolle", "/teken-in", "/registreer", "/wagwoord-vergeet", "/soek-span",
          "/markplein", "/soek-plase", "/advertensies", "/soek-profiele", f"/plaas/{state['farmId']}", f"/advertensies/{state['adId']}",
          f"/profiel/{U['ph']['userId']}"]
COMMON = ["/profiel", "/boodskappe", "/ki-helper", "/uitnodigings", "/betaling"]
ROLE_PAGES = {
    "hunter": ["/jagter/paneelbord", "/jagter/besprekings", "/jagter/permitte", "/jagter/e520"],
    "farmer": ["/boer/paneelbord", "/boer/besprekings", "/boer/plase", "/boer/nuwe-plaas", f"/boer/wysig-plaas/{state['farmId']}"],
    "pj": ["/pj/paneelbord", "/pj/besprekings", "/pj/pakkette", "/pj/skedule", "/pj/soek-span", "/pj/span"],
    "ph": ["/ph/paneelbord", "/ph/besprekings", "/ph/pakkette"],
    "vg": ["/vg/paneelbord", "/vg/besprekings", "/vg/pakkette"],
    "ap": ["/ap/paneelbord", "/ap/advertensies", "/ap/nuwe-advertensie", f"/ap/wysig-advertensie/{state['adId']}", f"/ap/betaal-advertensie/{state['adId']}"],
    "admin": ["/administrasie"],
}
WEBSOCKET_FALLBACK = []
BAD = re.compile(r"An unhandled error has occurred|Attempting to reconnect|Rejoin failed|Kon nie .* laai nie|Fout:|Exception", re.I)


def login(page, email, pw):
    page.goto(f"{APP}/teken-in", wait_until="load")
    page.fill("input[placeholder='bv. jagter@mikpunt.co.za']", email)
    page.fill("input[type=password]", pw)
    page.click("button[type=submit]")
    page.wait_for_url(lambda u: "/teken-in" not in u, timeout=30000)


def visit(page, who, path, errors):
    errors.clear()
    page.evaluate(f"Blazor.navigateTo('{path}')") if page.url.startswith(APP) and who != "anon" else page.goto(APP + path, wait_until="load")
    page.wait_for_timeout(3000)
    text = page.inner_text("body")
    banner = page.locator("#blazor-error-ui").is_visible() if page.locator("#blazor-error-ui").count() else False
    # The proxy not passing WebSockets makes Blazor fall back to long polling: reported once, separately
    # (see WEBSOCKET_FALLBACK below), so it doesn't hide real page errors.
    ws = [e for e in errors if "WebSocket" in e or "transport 'WebSockets'" in e]
    if ws:
        WEBSOCKET_FALLBACK.append(path)
    msgs = [e for e in errors if "favicon" not in e and e not in ws]
    bad = BAD.findall(text)
    by_design = (who, path) in {("anon", "/soek-span"), ("hunter", "/ki-helper")}
    restricted = "Toegang Beperk" in text or "Teken asseblief eers in" in text
    ok = not banner and not bad and not msgs and (restricted if by_design else not restricted)
    name = f"{who} {path}"
    check("ui", name, ok, f"banner={banner} text={bad[:3]} console={msgs[:2]} restricted={'Toegang Beperk' in text}")
    if not ok:
        page.screenshot(path=os.path.join(SHOTS, re.sub(r'[^a-z0-9]+', '_', name.lower()) + ".png"), full_page=True)


with sync_playwright() as p:
    browser = p.chromium.launch(channel=os.environ.get("E2E_BROWSER_CHANNEL", "chrome") or None, headless=True)
    errs = []
    page = browser.new_page(viewport={"width": 1366, "height": 900})
    page.on("console", lambda m: errs.append(m.text) if m.type == "error" else None)
    page.on("pageerror", lambda e: errs.append(str(e)))
    for path in PUBLIC:
        visit(page, "anon", path, errs)
    page.close()
    accounts = {k: (U[k]["email"], state["password"]) for k in ROLE_PAGES if k != "admin"}
    accounts["admin"] = require_admin()
    for who, pages in ROLE_PAGES.items():
        page = browser.new_page(viewport={"width": 1366, "height": 900})
        errs = []
        page.on("console", lambda m: errs.append(m.text) if m.type == "error" else None)
        page.on("pageerror", lambda e: errs.append(str(e)))
        try:
            login(page, *accounts[who])
            check("ui", f"{who} signs in on the website", True)
        except Exception as e:  # noqa: BLE001
            check("ui", f"{who} signs in on the website", False, str(e)[:200]); page.close(); continue
        for path in pages + COMMON:
            visit(page, who, path, errs)
        page.close()
    browser.close()
check("ui", "the website's live connection uses WebSockets (proxy passes them through)", not WEBSOCKET_FALLBACK,
      f"{len(WEBSOCKET_FALLBACK)} pages fell back to long polling - switch on WebSocket support for webapp.mikpunt.ddns.net in the proxy")
save_results("t10_ui")
