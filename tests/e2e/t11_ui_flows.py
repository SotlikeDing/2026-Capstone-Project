"""Phase 11: data-level UI checks on staging for the flows that matter."""
import re
from playwright.sync_api import sync_playwright, expect
from st import *

state = json.load(open(os.path.join(HERE, "staging_state.json")))
U = state["users"]
SHOTS = os.path.join(HERE, "ui_shots"); os.makedirs(SHOTS, exist_ok=True)
PW = state["password"]


def login(page, email, pw):
    page.goto(f"{APP}/teken-in", wait_until="load")
    page.fill("input[placeholder='bv. jagter@mikpunt.co.za']", email)
    page.fill("input[type=password]", pw)
    page.click("button[type=submit]")
    page.wait_for_url(lambda u: "/teken-in" not in u, timeout=30000)


def go(page, path, wait=3500):
    page.evaluate(f"Blazor.navigateTo('{path}')")
    page.wait_for_timeout(wait)


def shot(page, name):
    page.screenshot(path=os.path.join(SHOTS, name + ".png"), full_page=True)


def t(name, fn):
    try:
        fn(); check("ui-flow", name, True)
    except Exception as e:  # noqa: BLE001
        check("ui-flow", name, False, str(e).splitlines()[0][:250])


with sync_playwright() as p:
    b = p.chromium.launch(channel=os.environ.get("E2E_BROWSER_CHANNEL", "chrome") or None, headless=True)

    pg = b.new_page(viewport={"width": 1366, "height": 900})
    pg.goto(f"{APP}/soek-plase", wait_until="load"); pg.wait_for_timeout(3500)
    shot(pg, "anon_searchfarms")
    t("farm search page lists farms", lambda: expect(pg.get_by_text("E2E Toetsplaas Bosveld").first).to_be_visible(timeout=5000))
    pg.goto(f"{APP}/", wait_until="load"); pg.wait_for_timeout(3500); shot(pg, "anon_home")
    pg.goto(f"{APP}/markplein", wait_until="load"); pg.wait_for_timeout(3500); shot(pg, "anon_marketplace")
    t("marketplace lists the approved package", lambda: expect(pg.get_by_text("E2E Koedoe Safari").first).to_be_visible(timeout=5000))
    pg.goto(f"{APP}/advertensies", wait_until="load"); pg.wait_for_timeout(3500); shot(pg, "anon_searchads")
    t("ads page lists the paid ad", lambda: expect(pg.get_by_text("E2E Advertensie Bygewerk").first).to_be_visible(timeout=5000))
    pg.close()

    # hunter: bookings page, payment page, rating panel on the finished trip
    pg = b.new_page(viewport={"width": 1366, "height": 900})
    login(pg, U["hunter"]["email"], PW)
    go(pg, "/jagter/besprekings"); shot(pg, "hunter_bookings")
    t("hunter bookings list shows the trips", lambda: expect(pg.get_by_text(re.compile("E2E Koedoe Safari")).first).to_be_visible(timeout=5000))
    go(pg, f"/betaling?bookingId={state['bookings']['main']}"); shot(pg, "hunter_payment_before")
    pay = pg.get_by_role("button", name=re.compile("Betaal|PayFast", re.I))
    if pay.count():
        pay.first.click(); pg.wait_for_timeout(4000); shot(pg, "hunter_payment_after")
        t("payment page reaches PayFast (or shows why not)", lambda: (_ for _ in ()).throw(AssertionError(pg.inner_text("body")[:0] + "still on site: " + re.sub(r"\s+", " ", pg.inner_text("body"))[:200])) if "payfast" not in pg.url else None)
    pg.close()

    # VG: media panel on the finished trip (needs InternalApi__Key)
    pg = b.new_page(viewport={"width": 1366, "height": 900})
    login(pg, U["vg"]["email"], PW)
    go(pg, "/vg/besprekings")
    btn = pg.get_by_role("button", name=re.compile("Media"))
    t("VG sees 'Media & Gradering' on the finished trip", lambda: expect(btn.first).to_be_visible(timeout=5000))
    if btn.count():
        btn.first.click(); pg.wait_for_timeout(4000); shot(pg, "vg_media_panel")
        t("VG media panel loads media", lambda: expect(pg.get_by_text("dawn.jpg").first).to_be_visible(timeout=5000))
        t("VG rating panel shows ratings already given", lambda: expect(pg.get_by_text(re.compile("reeds gegradeer")).first).to_be_visible(timeout=5000))
    pg.close()

    # admin dashboard tabs render data
    pg = b.new_page(viewport={"width": 1366, "height": 900})
    login(pg, *require_admin())
    go(pg, "/administrasie", 5000); shot(pg, "admin_dashboard")
    t("admin dashboard renders with stats", lambda: expect(pg.get_by_text(re.compile("geregistreerde gebruikers", re.I)).first).to_be_visible(timeout=15000))
    pg.close()
    b.close()
save_results("t11_ui_flows")
