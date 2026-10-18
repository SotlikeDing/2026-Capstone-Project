"""Phase 3: farms, animals, packages, ads - CRUD, public listing, ownership, ad payment flow."""
from st import *

state = json.load(open(os.path.join(HERE, "staging_state.json")))
U = state["users"]
H = lambda k: bearer(U[k]["token"])
uid = lambda k: U[k]["userId"]
ur = lambda k: U[k]["roles"][0]["userRoleId"]
A = "farm"

addr = {"addressNumber": "12", "addressStreetName": "Bosveldweg", "addressCity": "Vaalwater", "addressRegion": "Limpopo",
        "addressPostalCode": "0530", "addressCountry": "South Africa", "addressLatitude": -24.3, "addressLongitude": 28.1}
farm_body = {"name": "E2E Toetsplaas Bosveld", "location": "Vaalwater, Limpopo", "size": 2500.5, "description": "Toetsplaas vir die E2E staging toetse in die Bosveld.",
             "terrainType": "Bosveld", "fencingType": "Wildheining", "dailyRateHunter": 1800, "amenities": ["Lodge", "Slaghuis"],
             "address": addr, "species": [{"speciesName": "Koedoe", "priceMale": 18000, "priceFemale": 6000, "priceTrophy": 25000, "availableCount": 12},
                                          {"speciesName": "Rooibok", "priceMale": 3500, "priceFemale": 1500, "priceTrophy": 5000, "availableCount": 40}]}

r = requests.post(f"{WEB}/api/farmer/create", headers=H("hunter"), json=farm_body)
check(A, "non-farmer can't create a farm (403)", r.status_code == 403, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/farmer/create", headers=H("farmer"), json=farm_body)
farm_id = (r.json() or {}).get("farmId") if r.ok else None
check(A, "farmer creates farm with address + species", r.status_code == 200 and farm_id, f"{r.status_code} {r.text[:200]}")
state["farmId"] = farm_id

r = requests.get(f"{WEB}/api/farmer/getFarm/{farm_id}")
f = r.json() if r.ok else {}
check(A, "farm readable publicly with details", r.ok and f.get("name") == farm_body["name"], f"{r.status_code} {str(f)[:200]}")
r = requests.get(f"{WEB}/api/farmer/getAnimals/{farm_id}")
animals = r.json() if r.ok else []
check(A, "farm species saved (2)", r.ok and len(animals) >= 2, f"{r.status_code} {str(animals)[:200]}")
r = requests.get(f"{WEB}/api/farmer/getFarmerFarms", headers=H("farmer"))
check(A, "farmer lists own farms", r.ok and any((x.get("id") or x.get("farmId")) == farm_id for x in r.json()), f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/farmer/getStats", headers=H("farmer"))
check(A, "farmer dashboard stats", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/farmer/getAllFarms", params={"searchTerm": "E2E"})
check(A, "farm search finds the new farm", r.ok and "E2E Toetsplaas" in r.text, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/farmer/all")
check(A, "all farms list", r.status_code == 200, f"{r.status_code} {r.text[:200]}")

r = requests.post(f"{WEB}/api/farmer/update/{farm_id}", headers=H("farmer"), json={"description": "Bygewerkte beskrywing vir die toetsplaas.", "status": "Active", "dailyRateHunter": 2000})
check(A, "farmer updates own farm", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/farmer/update/{farm_id}", headers=H("hunter"), json={"description": "hack"})
check(A, "other user can't update farm (403)", r.status_code == 403, r.status_code)
r = requests.post(f"{WEB}/api/farmer/addAnimal", headers=H("farmer"), json={"farmId": farm_id, "species": "Gemsbok", "count": 7})
check(A, "farmer adds animal", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/farmer/addAnimal", headers=H("hunter"), json={"farmId": farm_id, "species": "Olifant", "count": 1})
check(A, "other user can't add animals (403)", r.status_code == 403, r.status_code)
animals = requests.get(f"{WEB}/api/farmer/getAnimals/{farm_id}").json()
gem = next((a for a in animals if "Gemsbok" in json.dumps(a)), None)
aid = gem and (gem.get("id") or gem.get("availableAnimalId") or gem.get("animalId"))
if aid:
    r = requests.post(f"{WEB}/api/farmer/editAnimal/{aid}", headers=H("hunter"), json={"count": 0})
    check(A, "other user can't edit animals (403)", r.status_code == 403, r.status_code)
    r = requests.post(f"{WEB}/api/farmer/editAnimal/{aid}", headers=H("farmer"), json={"species": "Gemsbok", "count": 9})
    check(A, "farmer edits animal", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/farmer/create", headers=H("farmer"), json={**farm_body, "name": "", "size": -5})
check(A, "farm validation (empty name / negative size) -> 400", r.status_code == 400, f"{r.status_code} {r.text[:150]}")

# ------------------------------------------------------------------ packages (PJ)
A = "package"
pkg = {"pTitle": "E2E Koedoe Safari", "pDescription": "Toetspakket", "pStockPrice": 45000, "pDurationDays": 4, "pMaxParticipants": 4,
       "pIncludedItems": "Lodge, maaltye, PH", "pAvailableFrom": "2026-01-01", "pAvailableTill": "2027-12-31", "pFarmId": farm_id,
       "pMinExpeditionLength": 2, "pMinGuests": 1, "pMaxGuests": 4, "pStatus": "Active"}
r = requests.post(f"{WEB}/api/packets/createPacket", headers=H("hunter"), json=pkg)
check(A, "non-PJ can't create a package (403)", r.status_code == 403, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/packets/createPacket", headers=H("pj"), json=pkg)
pkg_id = None
if r.ok:
    try:
        j = r.json(); pkg_id = j if isinstance(j, int) else (j.get("packageId") or j.get("id") or j.get("PackageId"))
    except Exception:
        pass
check(A, "PJ creates package on the farm", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
if not pkg_id:
    pkg_id = int(sql("SELECT MAX(packageId) FROM packages WHERE pTitle = 'E2E Koedoe Safari'") or 0)
state["packageId"] = pkg_id
r = requests.get(f"{WEB}/api/packets/getPacket/{pkg_id}")
check(A, "package hidden until PJ documents are approved (404)", r.status_code == 404, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/packets/mine", headers=H("pj"))
mine = next((x for x in (r.json() if r.ok else []) if x.get("id") == pkg_id), None)
check(A, "PJ still sees the hidden package in 'my packages', with the reason", mine is not None and mine.get("organiserDocumentsApproved") is False
      and mine.get("isPubliclyVisible") is False, f"{r.status_code} {str(mine)[:200]}")
r = requests.get(f"{WEB}/api/packets/mine", headers=H("hunter"))
check(A, "'my packages' of a non-PJ is empty", r.ok and r.json() == [], f"{r.status_code} {r.text[:150]}")
for path in ["getPackets", "getPacketsPaginated?page=1&pageSize=5"]:
    r = requests.get(f"{WEB}/api/packets/{path}")
    check(A, f"{path} public list", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/packets/getPacketFiltered", json={"pFarmId": farm_id})
check(A, "unapproved package not in farm filter", r.ok and "E2E Koedoe Safari" not in r.text, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/packets/updatePacket/{pkg_id}", headers=H("pj"), json={"pStockPrice": 47500})
check(A, "PJ updates own package", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/packets/updatePacket/{pkg_id}", headers=H("hunter"), json={"pStockPrice": 1})
check(A, "other user can't update package (403)", r.status_code == 403, r.status_code)
r = requests.delete(f"{WEB}/api/packets/deletePacket/{pkg_id}", headers=H("hunter"))
check(A, "other user can't delete package (403)", r.status_code == 403, r.status_code)
r = requests.get(f"{WEB}/api/Landing/getPackages")
check(A, "landing page packages", r.status_code == 200, f"{r.status_code} {r.text[:200]}")

# ------------------------------------------------------------------ ads (AP)
A = "ads"
ad = {"title": "E2E Taxidermie Advertensie", "description": "Kort", "longDescription": "Lank", "type": "Banner", "category": "Taxidermy",
      "price": 500, "contactNumber": "0821112222", "targetUrl": "https://example.test", "status": "Active",
      "paymentRef": "FAKE-PAID", "lastPaymentMonths": 12, "lastPaymentAmount": 6000, "lastPaymentDate": "2026-10-01",
      "expiryDate": "2027-10-01", "address": addr}
r = requests.post(f"{WEB}/api/Ads/createAd", headers=H("hunter"), json=ad)
check(A, "non-advertiser can't create ad (403)", r.status_code == 403, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/Ads/createAd", headers=H("ap"), json=ad)
ad_id = None
if r.ok:
    j = r.json(); ad_id = j if isinstance(j, int) else (j.get("adId") or j.get("id") or j.get("advertisementId"))
check(A, "AP creates ad", r.status_code == 200 and ad_id, f"{r.status_code} {r.text[:200]}")
state["adId"] = ad_id
r = requests.get(f"{WEB}/api/Ads/getAd/{ad_id}")
a = r.json() if r.ok else {}
check(A, "new ad is not live before payment (client-sent Status/payment ignored)",
      a.get("status") not in ("Active", "Aktief") and a.get("paymentRef") != "FAKE-PAID", {k: a.get(k) for k in ("status", "paymentRef", "lastPaymentDate")})
r = requests.post(f"{WEB}/api/Ads/updateAd/{ad_id}", headers=H("ap"), json={"title": "E2E Advertensie Bygewerk", "status": "Active"})
check(A, "AP updates own ad", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
a = requests.get(f"{WEB}/api/Ads/getAd/{ad_id}").json()
check(A, "AP can't self-activate an unpaid ad via update", a.get("status") not in ("Active", "Aktief"), a.get("status"))
r = requests.post(f"{WEB}/api/Ads/updateAd/{ad_id}", headers=H("hunter"), json={"title": "hack"})
check(A, "other user can't update ad (403/404)", r.status_code in (403, 404), r.status_code)
r = requests.post(f"{WEB}/api/Ads/recordPayment/{ad_id}", headers=H("ap"), json={"paymentRef": "X", "months": 1, "amount": 1})
check(A, "AP can't record their own payment (admin only)", r.status_code == 403, r.status_code)
admin = admin_headers()
r = requests.post(f"{WEB}/api/Ads/recordPayment/{ad_id}", headers=admin, json={"paymentRef": "E2E-PAY-1", "months": 3, "amount": 1500})
check(A, "admin records ad payment", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
a = requests.get(f"{WEB}/api/Ads/getAd/{ad_id}").json()
check(A, "ad becomes live (Aktief) after payment", a.get("status") in ("Active", "Aktief"), a.get("status"))
r = requests.get(f"{WEB}/api/Ads/getPayments", headers=H("ap"), params={"adId": ad_id})
check(A, "AP sees own ad payments", r.ok and "E2E-PAY-1" in r.text, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/Ads/getPayments", headers=H("hunter"), params={"adId": ad_id})
check(A, "other user can't see the ad's payments", r.status_code == 403 or "E2E-PAY-1" not in r.text, f"{r.status_code} {r.text[:150]}")
v0 = a.get("views") or 0
requests.post(f"{WEB}/api/Ads/recordImpression/{ad_id}"); requests.post(f"{WEB}/api/Ads/recordClick/{ad_id}")
a = requests.get(f"{WEB}/api/Ads/getAd/{ad_id}").json()
check(A, "impression + click counted", (a.get("views") or 0) >= v0 + 1 and (a.get("clicks") or 0) >= 1, {k: a.get(k) for k in ("views", "clicks")})
r = requests.delete(f"{WEB}/api/Ads/deleteAd/{ad_id}", headers=H("hunter"))
check(A, "other user can't delete ad (403/404)", r.status_code in (403, 404), r.status_code)

json.dump(state, open(os.path.join(HERE, "staging_state.json"), "w"), indent=1)
save_results("t3_market")
