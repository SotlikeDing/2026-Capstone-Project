"""Phase 5: booking lifecycle - request, PJ staff assignment, PH/VG accept/decline, reject, payment, companions, lists, IDOR."""
from datetime import date, timedelta
from st import *

state = json.load(open(os.path.join(HERE, "staging_state.json")))
U = state["users"]
H = lambda k: bearer(U[k]["token"])
uid = lambda k: U[k]["userId"]
A = "booking"
ADMIN = admin_headers()
pkg = state["packageId"]
d = lambda n: (date.today() + timedelta(days=n)).isoformat()


def create(start, end, note, hunter="hunter", species=("Koedoe",)):
    r = requests.post(f"{WEB}/api/bookings/create", headers=H(hunter), json={
        "pakketId": pkg, "hunterId": uid(hunter), "startDate": start, "endDate": end, "huntGuests": 2, "nonHuntGuests": 1,
        "firearmOption": "Rental", "specialNotes": note, "wantsVideographer": True, "bookedSpecies": list(species)})
    return r, (r.json().get("id") if r.ok else None)


def booking(bid, who="admin"):
    hdr = ADMIN if who == "admin" else H(who)
    r = requests.get(f"{WEB}/api/bookings/getSpecificBooking/{bid}", headers=hdr)
    return r.json() if r.ok else {}


# ---- request
r, main = create(d(30), d(33), "E2E hoofbespreking")
check(A, "hunter requests a booking on a package", r.status_code == 200 and main, f"{r.status_code} {r.text[:200]}")
b = booking(main)
check(A, "new booking is Pending", b.get("status") == "Pending", b.get("status"))
r, _ = create(d(-10), d(-8), "verlede datums")
check(A, "booking in the past refused", r.status_code == 400, f"{r.status_code} {r.text[:150]}")
r, _ = create(d(40), d(38), "einde voor begin")
check(A, "end before start refused", r.status_code == 400, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/bookings/create", headers=H("hunter"), json={"pakketId": pkg, "hunterId": uid("hunter2"), "startDate": d(50), "endDate": d(52)})
bid_other = r.json().get("id") if r.ok else None
if bid_other:
    owner = sql(f"SELECT u.urUserId FROM bookings b JOIN userRoles u ON u.userRoleId=b.bookHunterId WHERE b.bookingId={bid_other}")
    check(A, "can't book in someone else's name (hunterId ignored)", owner == str(uid("hunter")), f"booking owned by userId {owner}")
else:
    check(A, "can't book in someone else's name", r.status_code in (400, 403), r.status_code)

# ---- who sees what
for who, path in (("hunter", f"getHunterBookings?id={uid('hunter')}"), ("pj", f"getBookings?id={uid('pj')}"), ("farmer", f"getFarmBookings?id={uid('farmer')}")):
    r = requests.get(f"{WEB}/api/bookings/{path}", headers=H(who))
    check(A, f"{who} sees the booking in their list", r.ok and str(main) in r.text, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{WEB}/api/bookings/getSpecificBooking/{main}", headers=H("hunter2"))
check(A, "unrelated user can't read the booking (403/404)", r.status_code in (403, 404), r.status_code)
r = requests.get(f"{WEB}/api/bookings/getHunterBookings", headers=H("hunter2"), params={"id": uid("hunter")})
check(A, "can't list someone else's bookings", r.status_code == 403 or str(main) not in r.text, f"{r.status_code} {r.text[:100]}")

# ---- PJ assigns staff, PH/VG accept
r = requests.post(f"{WEB}/api/bookings/approveWithStaff/{main}", headers=H("hunter"), json={"phUserId": uid("ph"), "vgUserId": uid("vg")})
check(A, "hunter can't assign staff (403)", r.status_code == 403, r.status_code)
r = requests.post(f"{WEB}/api/bookings/approveWithStaff/{main}", headers=H("pj"),
                  json={"phUserId": uid("ph"), "vgUserId": uid("vg"), "confirmedTotalPrice": 52000, "confirmedAccommodationPrice": 7000,
                        "priceBreakdownJson": json.dumps({"package": 45000, "accommodation": 7000})})
check(A, "PJ assigns PH + VG and confirms price", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
b = booking(main)
check(A, "booking waits for staff (PendingStaffAcceptance)", b.get("status") == "PendingStaffAcceptance", b.get("status"))
for who in ("ph", "vg"):
    path = "getPhBookings" if who == "ph" else "getVgBookings"
    r = requests.get(f"{WEB}/api/bookings/{path}", headers=H(who), params={"id": uid(who)})
    check(A, f"{who} sees the assignment", r.ok and str(main) in r.text, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/bookings/staffRespond/{main}", headers=H("hunter2"), json={"role": "PH", "accept": True})
check(A, "unassigned user can't accept for the PH (403)", r.status_code in (403, 404), r.status_code)
r = requests.post(f"{WEB}/api/bookings/staffRespond/{main}", headers=H("ph"), json={"role": "PH", "accept": True})
check(A, "PH accepts", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
check(A, "still waiting for VG after PH accepts", booking(main).get("status") == "PendingStaffAcceptance", booking(main).get("status"))
r = requests.post(f"{WEB}/api/bookings/staffRespond/{main}", headers=H("vg"), json={"role": "VG", "accept": True})
check(A, "VG accepts", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
b = booking(main)
check(A, "booking Approved once all staff accept", b.get("status") == "Approved", b.get("status"))
check(A, "confirmed price stored", float(b.get("totalAmount") or b.get("totalPrice") or 0) == 52000, {k: b.get(k) for k in ("totalAmount", "totalPrice")})

# ---- payment
r = requests.post(f"{WEB}/api/Payments/quote", headers=H("hunter"), json={"kind": "booking", "id": main})
q = r.json() if r.ok else {}
check("payment", "hunter gets a booking quote", r.ok, f"{r.status_code} {r.text[:200]}")
print("    quote:", str(q)[:200])
r = requests.post(f"{WEB}/api/Payments/quote", headers=H("hunter2"), json={"kind": "booking", "id": main})
check("payment", "other user can't get a quote for your booking", r.status_code in (403, 404), f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/Payments/checkout", headers=H("hunter"), json={"kind": "booking", "id": main,
                  "returnUrl": "https://webapp.mikpunt.ddns.net/payment/return", "cancelUrl": "https://webapp.mikpunt.ddns.net/payment/cancel"})
co = r.json() if r.ok else {}
check("payment", "checkout builds a PayFast form", r.ok and co.get("processUrl") and co.get("fields"), f"{r.status_code} {r.text[:250]}")
print("    processUrl:", co.get("processUrl"), "| simulator:", co.get("simulatorAvailable"),
      "| notify_url:", next((f["value"] for f in co.get("fields", []) if f["name"] == "notify_url"), None))
r = requests.post(f"{WEB}/api/Payments/checkout", headers=H("hunter"), json={"kind": "booking", "id": main,
                  "returnUrl": "https://evil.example/steal", "cancelUrl": "https://evil.example/x"})
check("payment", "checkout refuses foreign return URLs (open redirect)", r.status_code == 400, r.status_code)
r = requests.post(f"{WEB}/api/Payments/simulate", headers=H("hunter"), json={"kind": "booking", "id": main})
check("payment", "payment simulator disabled outside Development", r.status_code in (403, 404), f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/PayFast/itn", data={"m_payment_id": f"booking-{main}", "payment_status": "COMPLETE", "amount_gross": "52000.00", "signature": "forged"})
check("payment", "forged PayFast ITN rejected", booking(main).get("status") != "Paid", f"ITN {r.status_code}; booking {booking(main).get('status')}")
r = requests.post(f"{WEB}/api/bookings/setPaymentStatus/{main}", headers=H("hunter"), json="Paid")
check("payment", "hunter can't mark own booking Paid (403)", r.status_code == 403, r.status_code)
r = requests.post(f"{WEB}/api/bookings/setPaymentStatus/{main}", headers=ADMIN, json="Paid")
check("payment", "admin marks booking Paid", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
check("payment", "booking now Paid", booking(main).get("status") == "Paid", booking(main).get("status"))

# ---- companions (extra hunters)
r = requests.post(f"{WEB}/api/bookings/{main}/hunters", headers=H("hunter"), json={"name": "E2EHunter2 Toets", "email": U["hunter2"]["email"]})
check("companions", "main hunter adds a second hunter", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/bookings/{main}/hunters", headers=H("hunter2"))
check("companions", "added hunter sees the roster", r.ok and "E2EHunter" in r.text, f"{r.status_code} {r.text[:200]}")
check("companions", "added hunter doesn't see emails", "@" not in r.text, r.text[:200])
r = requests.get(f"{WEB}/api/bookings/companionBookings", headers=H("hunter2"))
check("companions", "trip appears in added hunter's trips", r.ok and str(main) in r.text, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/bookings/{main}/hunters", headers=H("hunter2"), json={"name": "X", "email": "x@staging-e2e.test"})
check("companions", "added hunter can't add more hunters (403)", r.status_code == 403, r.status_code)
r = requests.post(f"{WEB}/api/bookings/{main}/hunters", headers=H("hunter"), json={"name": "Third", "email": "third@staging-e2e.test"})
check("companions", "can't exceed hunter count (2)", r.status_code in (400, 409), f"{r.status_code} {r.text[:150]}")

# ---- reject + decline paths
r, rej = create(d(60), d(62), "E2E om te weier")
r = requests.post(f"{WEB}/api/bookings/rejectBooking/{rej}", headers=H("pj"), json={"reasonCode": "Unavailable", "reasonDescription": "Volgeboek", "notes": "E2E"})
check(A, "PJ rejects a booking with a reason", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
b = booking(rej)
check(A, "rejected booking shows status + reason", b.get("status") == "Rejected" and "Volgeboek" in json.dumps(b), {k: b.get(k) for k in ("status", "rejectionReason")})
r, dec = create(d(70), d(72), "E2E PH weier")
requests.post(f"{WEB}/api/bookings/approveWithStaff/{dec}", headers=H("pj"), json={"phUserId": uid("ph"), "vgUserId": None})
r = requests.post(f"{WEB}/api/bookings/staffRespond/{dec}", headers=H("ph"), json={"role": "PH", "accept": False, "declineReason": "Siek"})
check(A, "PH declines an assignment", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
b = booking(dec)
check(A, "declined booking goes back to the PJ (not Approved)", b.get("status") != "Approved" and "Siek" in json.dumps(b), {k: b.get(k) for k in ("status", "phAssignmentStatus", "phDeclineReason")})
r = requests.get(f"{WEB}/api/bookings/getAuditLogs/{main}", headers=H("pj"))
check(A, "booking audit log available to PJ", r.ok and len(r.json()) >= 3, f"{r.status_code} {r.text[:200]}")
r = requests.delete(f"{WEB}/api/bookings/deleteBooking/{dec}/{uid('hunter')}", headers=H("hunter2"))
check(A, "other user can't delete the booking (403)", r.status_code in (403, 404), r.status_code)
r = requests.delete(f"{WEB}/api/bookings/deleteBooking/{dec}/{uid('hunter')}", headers=H("hunter"))
check(A, "hunter cancels/deletes own pending booking", r.status_code == 200, f"{r.status_code} {r.text[:200]}")

state["bookings"] = {"main": main, "rejected": rej, "declined": dec}
json.dump(state, open(os.path.join(HERE, "staging_state.json"), "w"), indent=1)
save_results("t5_bookings")
