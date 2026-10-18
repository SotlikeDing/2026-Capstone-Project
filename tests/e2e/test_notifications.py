from playwright.sync_api import sync_playwright

def test_notifications():
    with sync_playwright() as p:
        browser = p.chromium.launch(channel='msedge', headless=True)
        page = browser.new_page()

        print("[TEST] Step 1: Navigating to /teken-in...")
        page.goto('http://localhost:5147/teken-in')
        page.wait_for_load_state('networkidle')

        print("[TEST] Step 2: Logging in as Johan...")
        page.fill('input[type="email"], input[name*="email" i], input[placeholder*="epos" i]', 'johan.vanwyk@demo.mikpunt.co.za')
        page.fill('input[type="password"]', 'Jag-g7rdgfrj4h-2026!')
        page.click('button[type="submit"]')
        page.wait_for_timeout(3500)

        print("[TEST] Step 3: Navigating to /notifications...")
        page.goto('http://localhost:5147/notifications')
        page.wait_for_timeout(3000)

        content = page.content()
        assert 'Kennisgewingsentrum' in content, 'Header not found on /notifications'
        print("[TEST] /notifications successfully rendered Kennisgewingsentrum!")

        print("[TEST] Step 4: Navigating to /kennisgewings...")
        page.goto('http://localhost:5147/kennisgewings')
        page.wait_for_timeout(3000)

        content2 = page.content()
        assert 'Kennisgewingsentrum' in content2, 'Header not found on /kennisgewings'
        print("[TEST] /kennisgewings successfully rendered Kennisgewingsentrum!")

        page.screenshot(path='Skermskote/notifications_page_verified.png', full_page=True)
        print("[TEST] Screenshot saved to Skermskote/notifications_page_verified.png")

        browser.close()
        print("[TEST] Notifications test completed with 100% SUCCESS!")

if __name__ == '__main__':
    test_notifications()
