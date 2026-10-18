"""Phase 9: post-trip team ratings on the website, moderation, disputes, admin resolution."""
from st import *

state = json.load(open(os.path.join(HERE, "staging_state.json")))
U = state["users"]
W = lambda k: bearer(U[k]["token"])
uid = lambda k: U[k]["userId"]
ADMIN = admin_headers()
A = "ratings"
past, main = state["bookings"]["past"], state["bookings"]["main"]

r = requests.get(f"{WEB}/api/rating/booking/{past}/team", headers=W("vg"))
team = r.json() if r.ok else {}
check(A, "VG sees hunter + PH to rate", r.ok and team.get("canRate") and sorted(m["roleCode"] for m in team["members"]) == ["HUNTER", "PH"], team)
r = requests.get(f"{WEB}/api/rating/booking/{main}/team", headers=W("vg"))
check(A, "future trip: not rateable yet", r.ok and r.json().get("canRate") is False, r.text[:150])

def rate(rater, ratee, role, stars, comment=None, booking=past):
    return requests.post(f"{WEB}/api/rating/setRoleRating", headers=W(rater), json={"bookingId": booking, "raterUserId": uid(rater),
                         "rateeUserId": uid(ratee), "rateeRoleCode": role, "stars": stars, "comment": comment})

check(A, "VG rates PH 5", rate("vg", "ph", "PH", 5, "Uitstekende gids").status_code == 200)
check(A, "VG rates hunter 4", rate("vg", "hunter", "HUNTER", 4, "Geduldige jagter").status_code == 200)
check(A, "PH rates VG 5", rate("ph", "vg", "VG", 5, "Pragtige beeldmateriaal").status_code == 200)
check(A, "hunter rates PH 2 (to dispute)", rate("hunter", "ph", "PH", 2, "Was laat").status_code == 200)
r = rate("hunter", "vg", "VG", 3, "Betaal hom kontant buite die app")
check(A, "comment with fee-bypass words saved", r.status_code == 200, r.status_code)
r = requests.get(f"{WEB}/api/admin/flagged-ratings", headers=ADMIN)
check(A, "fee-bypass rating comment flagged for admin", r.ok and "kontant" in r.text.lower(), f"{r.status_code} {r.text[:150]}")
fl = next((f for f in (r.json() if r.ok else []) if "kontant" in json.dumps(f).lower()), None)
if fl:
    r = requests.post(f"{WEB}/api/admin/flagged-ratings/{fl.get('flagId') or fl.get('id')}/moderate", headers=ADMIN, json={"action": "ActionTaken", "reason": "E2E"})
    check(A, "admin moderates flagged rating comment", r.status_code == 200, f"{r.status_code} {r.text[:150]}")

check(A, "stars out of range refused", rate("vg", "ph", "PH", 6).status_code == 400)
check(A, "can't rate yourself", rate("vg", "vg", "VG", 5).status_code == 400)
check(A, "outsider (hunter2) can't rate", rate("hunter2", "ph", "PH", 1).status_code == 403)
check(A, "wrong role context refused", rate("vg", "ph", "VG", 5).status_code == 400)
r = requests.post(f"{WEB}/api/rating/setRoleRating", headers=W("vg"), json={"bookingId": past, "raterUserId": uid("hunter"), "rateeUserId": uid("ph"), "rateeRoleCode": "PH", "stars": 1})
check(A, "can't rate on someone else's behalf", r.status_code == 403, r.status_code)

stats = requests.get(f"{WEB}/api/rating/profile-stats/{uid('ph')}").json()
check(A, "PH profile: 2 ratings, average 3.5", stats.get("ratingCount") == 2 and abs(float(stats["averageStars"]) - 3.5) < 0.01, stats)
check(A, "PH completed-trip count includes the finished trip", stats.get("completedTrips", 0) >= 1, stats)
recent = requests.get(f"{WEB}/api/rating/recent/{uid('ph')}").json()
check(A, "recent reviews list with comments", any("Uitstekende" in (x.get("comment") or "") for x in recent), recent)
low = next((x for x in recent if x.get("stars") == 2), None)

# dispute: ratee only
r = requests.post(f"{WEB}/api/rating/{low['ratingId']}/dispute", headers=W("hunter"), json={"reason": "nie my gradering nie"})
check(A, "only the rated person can dispute", r.status_code == 403, r.status_code)
r = requests.post(f"{WEB}/api/rating/{low['ratingId']}/dispute", headers=W("ph"), json={"reason": ""})
check(A, "dispute needs a reason", r.status_code == 400, r.status_code)
r = requests.post(f"{WEB}/api/rating/{low['ratingId']}/dispute", headers=W("ph"), json={"reason": "Die jagter was self laat by die vergaderpunt."})
check(A, "PH disputes the low rating", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{WEB}/api/admin/rating-disputes", headers=ADMIN, params={"status": "PendingReview"})
disp = next((x for x in (r.json() if r.ok else []) if x.get("ratingId") == low["ratingId"]), None)
check(A, "admin sees the dispute", disp is not None, r.text[:200])
r = requests.get(f"{WEB}/api/admin/ratings/{low['ratingId']}/history", headers=ADMIN)
check(A, "rating edit history for admin", r.ok and len(r.json()) >= 1, f"{r.status_code} {r.text[:150]}")
if disp:
    r = requests.post(f"{WEB}/api/admin/rating-disputes/{disp['disputeId']}/resolve", headers=ADMIN, json={"status": "Upheld", "notes": "E2E"})
    check(A, "admin upholds the dispute", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
    stats = requests.get(f"{WEB}/api/rating/profile-stats/{uid('ph')}").json()
    check(A, "upheld rating no longer counts (avg 5.0, 1 rating)", stats.get("ratingCount") == 1 and abs(float(stats["averageStars"]) - 5) < 0.01, stats)
    r = requests.post(f"{WEB}/api/rating/{low['ratingId']}/dispute", headers=W("ph"), json={"reason": "weer"})
    check(A, "resolved dispute can't be reopened", r.status_code == 400, r.status_code)

# the website relay to MobileAPI for VG media (needs InternalApi__Key)
r = requests.get(f"{WEB}/api/vg/bookings/{past}/media", headers=W("vg"))
check("vg-media", "website VG media panel reachable", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
save_results("t9_ratings")
