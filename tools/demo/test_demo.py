from __future__ import annotations

import datetime as dt
import unittest

try:
    from .demo_common import all_accounts, load_cast, route_payload
except ImportError:  # Direct script execution from tools/demo.
    from demo_common import all_accounts, load_cast, route_payload  # type: ignore


class DemoFixtureTests(unittest.TestCase):
    def test_account_and_role_counts_match_plan(self) -> None:
        accounts = all_accounts(load_cast())
        counts: dict[str, int] = {}
        for account in accounts:
            self.assertTrue(account["email"].endswith("@demo.mikpunt.co.za"))
            for role in account["roles"]:
                counts[role] = counts.get(role, 0) + 1
        self.assertEqual(45, len(accounts))
        self.assertEqual(
            {"HUNTER": 25, "PJ": 5, "PH": 6, "VG": 4, "FARMER": 5, "AP": 3},
            counts,
        )

    def test_route_has_three_shot_pins_and_four_hours(self) -> None:
        route = route_payload(dt.date(2026, 10, 20))
        self.assertEqual(3, len(route["pins"]))
        self.assertTrue(all(pin["type"] == "Shot" for pin in route["pins"]))
        self.assertEqual(14_400, route["durationSeconds"])
        self.assertGreaterEqual(route["distanceMeters"], 6_500)
        self.assertLessEqual(route["distanceMeters"], 7_500)


if __name__ == "__main__":
    unittest.main()
