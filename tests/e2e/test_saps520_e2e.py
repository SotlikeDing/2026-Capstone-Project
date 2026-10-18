import os
import sys
import time
from playwright.sync_api import sync_playwright

def test_saps520_flow():
    os.makedirs('Skermskote', exist_ok=True)
    with sync_playwright() as p:
        browser = p.chromium.launch(channel="msedge", headless=True)
        context = browser.new_context()
        page = context.new_page()

        print("[TEST] Step 1: Navigating to sign in page...")
        page.goto("http://localhost:5147/teken-in", timeout=20000)
        page.wait_for_load_state("networkidle")

        print("[TEST] Step 2: Signing in as Johan van Wyk (Jagter)...")
        # Fill in email and password
        page.fill('input[type="email"], input[name*="email" i], input[placeholder*="epos" i]', 'johan.vanwyk@demo.mikpunt.co.za')
        page.fill('input[type="password"]', 'Jag-g7rdgfrj4h-2026!')
        page.click('button[type="submit"]')

        # Wait for redirect after sign in
        page.wait_for_timeout(3500)

        print("[TEST] Step 3: Navigating to /jagter/e520...")
        page.goto("http://localhost:5147/jagter/e520", timeout=20000)
        page.wait_for_timeout(3000)

        content = page.content()
        assert "SAPS 520" in content, "SAPS 520 not found on page"
        print("[TEST] SAPS 520 page loaded successfully!")

        # Step 4: Verify required firearm validation when submitting without firearms
        print("[TEST] Step 4: Testing validation when submitting without firearms...")
        submit_btn = page.locator('button:has-text("Stoor"), button:has-text("Dien In")').last
        submit_btn.click()
        page.wait_for_timeout(1000)

        content = page.content()
        assert "Validasiefout" in content or "Nie ingedien nie" in content, f"Expected validation error, got: {content[:400]}"
        print("[TEST] Validation check verified successfully: Firearms required before submission.")

        # Step 5: Add firearm and ammunition
        print("[TEST] Step 5: Adding a firearm and ammunition line item...")
        add_gun_btn = page.locator('button:has-text("+ Voeg Vuurwapen By")')
        add_gun_btn.click()
        page.wait_for_timeout(600)

        page.locator('input[placeholder*="Sako"]').first.fill('Sako')
        page.locator('input[placeholder*="Bavarian"]').first.fill('85 Bavarian')
        page.locator('input[placeholder*=".30-06"]').first.fill('.30-06 Springfield')
        page.locator('input[placeholder*="Raamreeksnommer"]').first.fill('SK-99120')
        page.locator('input[placeholder*="Loopreeksnommer"]').first.fill('BL-8821')

        add_ammo_btn = page.locator('button:has-text("+ Voeg Ammunisie By")')
        add_ammo_btn.click()
        page.wait_for_timeout(600)

        page.locator('input[placeholder*="Springfield"]').first.fill('.30-06 Springfield 180gr')

        # Fill residential address
        page.locator('input[placeholder*="Oak Avenue"]').first.fill('124 Oak Avenue, Dallas, TX')
        page.locator('input[placeholder*="75201"]').first.fill('75201')

        # Step 6: Submit form
        print("[TEST] Step 6: Submitting complete form...")
        submit_btn.click()
        page.wait_for_timeout(1000)

        # Modal should appear
        modal_content = page.content()
        assert "Aanspreeklikheidsverklaring" in modal_content, "Confirmation modal did not appear"
        print("[TEST] Responsibility confirmation modal displayed!")

        # Click confirm button
        confirm_btn = page.locator('button:has-text("Ek Bevestig")')
        confirm_btn.click()
        page.wait_for_timeout(4000)

        # Step 7: Check success banner
        final_content = page.content()
        assert "SAPS 520 Vorm Suksesvol Ingedien by SAPS" in final_content, "Success title not found"
        assert "Sentrale Vuurwapenregister" in final_content, "SAPS CFR mention not found"
        print("[TEST] Afrikaans submission confirmation message successfully displayed!")

        # Take screenshot of the success banner
        screenshot_path = os.path.abspath('Skermskote/e520_success_verified.png')
        page.screenshot(path=screenshot_path, full_page=True)
        print(f"[TEST] Screenshot saved to: {screenshot_path}")

        browser.close()
        print("[TEST] ALL END-TO-END CHECKS PASSED 100%!")

if __name__ == "__main__":
    test_saps520_flow()
