"""Phase 8: the mobile app API - role logins, bookings, chats, trophies, Rowland Ward, trophy submissions,
farm ratings, GPS route, media (photo + VG video), recap generation with the server's real ffmpeg, YouTube, team ratings."""
import math
from datetime import date, datetime, timedelta, timezone
from st import *

state = json.load(open(os.path.join(HERE, "staging_state.json")))
U = state["users"]
PW = state["password"]
W = lambda k: bearer(U[k]["token"])
uid = lambda k: U[k]["userId"]
ur = lambda k: U[k]["roles"][0]["userRoleId"]
ADMIN_WEB = admin_headers()
MEDIA = os.path.join(os.environ["TEMP"], "mikpunt-e2e", "media")
A = "mobile"

# ---------------------------------------------------------------- a finished trip (web flow, then dates moved into the past)
d = lambda n: (date.today() + timedelta(days=n)).isoformat()
r = requests.post(f"{WEB}/api/bookings/create", headers=W("hunter"), json={"pakketId": state["packageId"], "hunterId": uid("hunter"),
                  "startDate": d(20), "endDate": d(22), "huntGuests": 1, "wantsVideographer": True, "specialNotes": "E2E afgehandelde jag"})
past = r.json()["id"]
requests.post(f"{WEB}/api/bookings/approveWithStaff/{past}", headers=W("pj"), json={"phUserId": uid("ph"), "vgUserId": uid("vg"), "confirmedTotalPrice": 30000})
requests.post(f"{WEB}/api/bookings/staffRespond/{past}", headers=W("ph"), json={"role": "PH", "accept": True})
requests.post(f"{WEB}/api/bookings/staffRespond/{past}", headers=W("vg"), json={"role": "VG", "accept": True})
time.sleep(1)
requests.post(f"{WEB}/api/bookings/setPaymentStatus/{past}", headers=ADMIN_WEB, json="Paid")
sql(f"UPDATE bookings SET bookStartDate = CAST(DATEADD(day,-5,GETDATE()) AS date), bookEndDate = CAST(DATEADD(day,-3,GETDATE()) AS date) WHERE bookingId = {past}")
state["bookings"]["past"] = past
start_day = datetime.combine(date.today() - timedelta(days=4), datetime.min.time(), tzinfo=timezone.utc)
iso = lambda t: t.strftime("%Y-%m-%dT%H:%M:%SZ")
print(f"    finished booking {past}: {sql(f'SELECT bookStatus, bookStartDate, bookEndDate FROM bookings WHERE bookingId={past}')}")

# ---------------------------------------------------------------- logins + capabilities
M = {}
for k in ("hunter", "ph", "vg", "farmer", "hunter2"):
    r, j = mobile_login(U[k]["email"], PW, ur(k))
    if check(A, f"{k} signs in to the app", r.status_code == 200 and j and j.get("accessToken"), f"{r.status_code} {r.text[:150]}"):
        M[k] = bearer(j["accessToken"])
        U[k]["mobile_refresh"] = j.get("refreshToken")
r, _ = mobile_login(U["hunter"]["email"], "Wrong!Pass1", ur("hunter"))
check(A, "wrong password refused", r.status_code == 401, r.status_code)
# The app no longer chooses a role: the server takes it from the database and ignores one sent along.
r, j = mobile_login(U["hunter"]["email"], PW, ur("ph"))
c = json.loads(base64.urlsafe_b64decode(j["accessToken"].split(".")[1] + "==")) if r.ok and j and j.get("accessToken") else {}
check(A, "a role sent by the app is ignored: the token carries the user's own role", c.get("userRoleId") == str(ur("hunter")), f"{r.status_code} {c}")
if r.ok and j:   # every app sign-in replaces the user's earlier session, so continue with this one
    M["hunter"] = bearer(j["accessToken"]); U["hunter"]["mobile_refresh"] = j.get("refreshToken")
caps = {k: requests.get(f"{MOBILE}/api/mobile-access/me", headers=M[k]).json() for k in M}
check(A, "hunter capabilities", "canSubmitTrophy" in caps["hunter"]["capabilities"] and "canUploadBookingVideo" not in caps["hunter"]["capabilities"], caps["hunter"])
check(A, "PH capabilities", "canReviewTrophies" in caps["ph"]["capabilities"] and "canSelectRoute" in caps["ph"]["capabilities"], caps["ph"])
check(A, "VG capabilities include video upload", "canUploadBookingVideo" in caps["vg"]["capabilities"], caps["vg"])
check(A, "farmer is restricted in the app", caps["farmer"]["isSupported"] is False and not caps["farmer"]["capabilities"], caps["farmer"])
r = requests.post(f"{MOBILE}/api/auth/refresh", json={"refreshToken": U["hunter"]["mobile_refresh"], "accessToken": M["hunter"]["Authorization"][7:]})
if r.ok and r.json().get("accessToken"):
    c = json.loads(base64.urlsafe_b64decode(r.json()["accessToken"].split(".")[1] + "=="))
    check(A, "app token refresh keeps the same user + role", (c.get("nameid") or c.get(NAMEID)) == str(uid("hunter")) and c.get("userRoleId") == str(ur("hunter")), c)
else:
    check(A, "app token refresh", False, f"{r.status_code} {r.text[:150]}")

# ---------------------------------------------------------------- bookings in the app
for k in ("hunter", "ph", "vg"):
    r = requests.get(f"{MOBILE}/api/bookings", headers=M[k])
    check(A, f"{k} sees the finished trip in the app", r.ok and str(past) in r.text, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{MOBILE}/api/bookings", headers=M["hunter2"])
check(A, "unrelated hunter doesn't see the trip", r.ok and f'"bookingId":{past}' not in r.text.replace(" ", ""), r.text[:150])
r = requests.get(f"{MOBILE}/api/bookings/user/{ur('hunter')}", headers=M["hunter2"])
check(A, "can't list another user's bookings by userRoleId", r.status_code in (403, 404) or str(past) not in r.text, f"{r.status_code}")
r = requests.get(f"{MOBILE}/api/bookings/{state['bookings']['main']}/hunters", headers=M["hunter2"])
check(A, "added hunter sees roster in app", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{MOBILE}/api/permits/user/{ur('hunter')}", headers=M["hunter"])
check(A, "permit status in app", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{MOBILE}/api/permits/user/{ur('hunter')}", headers=M["hunter2"])
check(A, "can't read someone else's permits in app", r.status_code in (403, 404), r.status_code)

# ---------------------------------------------------------------- chats in the app
r = requests.get(f"{MOBILE}/api/chats/user/{uid('hunter')}/chats", headers=M["hunter"])
chats = r.json() if r.ok else []
check(A, "hunter's chats in app (shared with website)", r.ok and len(chats) >= 1, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{MOBILE}/api/chats/user/{uid('hunter')}/chats", headers=M["hunter2"])
check(A, "can't read another user's chat list", r.status_code in (403, 404), r.status_code)
if chats:
    cid = chats[0].get("chatId") or chats[0].get("id")
    r = requests.post(f"{MOBILE}/api/chats/{cid}/messages", headers=M["hunter"], json={"senderId": uid("hunter"), "content": "Vanaf die app"})
    check(A, "send message from app", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
    r = requests.get(f"{WEB}/api/Chats/{cid}/messages", headers=W("pj"))
    check(A, "app message visible on website", r.ok and "Vanaf die app" in r.text, r.status_code)
r = requests.get(f"{MOBILE}/api/chats/getNotificationStatus/{uid('hunter')}", headers=M["hunter"])
check(A, "chat notification status", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{MOBILE}/api/users/by-email", headers=M["hunter"], params={"email": U["pj"]["email"]})
check(A, "find user by email (new chat)", r.status_code == 200, f"{r.status_code} {r.text[:150]}")

# ---------------------------------------------------------------- Rowland Ward + trophies
A = "trophy"
for path in ["categories", "classes", "search?query=kudu", "match?input=Koedoe"]:
    r = requests.get(f"{MOBILE}/api/rowland-ward/{path}", headers=M["hunter"])
    check(A, f"rowland-ward {path}", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{MOBILE}/api/rowland-ward/classes/southern_greater_kudu/measurement-requirements", headers=M["hunter"])
req = r.json() if r.ok else {}
check(A, "kudu measurement requirements", r.ok, f"{r.status_code} {r.text[:150]}")
print("    kudu requirements:", json.dumps(req)[:300])
r = requests.get(f"{MOBILE}/api/profile/rowland-ward-preference", headers=M["hunter"])
check(A, "read Rowland Ward preference", r.status_code == 200, f"{r.status_code} {r.text[:120]}")
r = requests.put(f"{MOBILE}/api/profile/rowland-ward-preference", headers=M["hunter"], json={"rowlandWardEnabled": True})
check(A, "enable Rowland Ward", r.status_code in (200, 204), f"{r.status_code} {r.text[:120]}")
for path in ["", "/species", "/firearm-types", "/leaderboard", "/legacy-species"]:
    r = requests.get(f"{MOBILE}/api/public-trophies{path}")
    check(A, f"public-trophies{path or ' (gallery)'}", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
firearm = (requests.get(f"{MOBILE}/api/public-trophies/firearm-types").json() or ["Rifle"])[0]
firearm = firearm if isinstance(firearm, str) else (firearm.get("code") or firearm.get("name") or "Rifle")

with open(os.path.join(MEDIA, "dawn.jpg"), "rb") as fh:
    r = requests.post(f"{MOBILE}/api/trophies/photos", headers=M["hunter"], files={"photo": ("kudu.jpg", fh, "image/jpeg")})
photo_path = (r.json() or {}).get("photoPath") or (r.json() or {}).get("path") if r.ok else None
check(A, "upload trophy photo", r.status_code in (200, 201) and photo_path, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{MOBILE}/api/trophies/photos", headers=M["hunter"], files={"photo": ("evil.jpg", b"not an image", "image/jpeg")})
check(A, "fake image refused", r.status_code == 400, f"{r.status_code} {r.text[:120]}")
trophy = {"species": "Koedoe", "photoPath": photo_path, "antlerSizeCm": 132.5, "carcassWeightKg": 210, "shotDistanceMeters": 180, "firearmType": firearm,
          "harvestDate": iso(start_day + timedelta(hours=6)), "locationText": "E2E Toetsplaas Bosveld", "notes": "E2E trofee", "isPublic": True}
r = requests.post(f"{MOBILE}/api/trophies", headers=M["hunter"], json=trophy)
check(A, "trophies can't be created directly - only via PH approval (409)", r.status_code == 409, f"{r.status_code} {r.text[:150]}")

# ---------------------------------------------------------------- trophy submissions (hunter -> PH review)
sub = {"bookingId": past, "species": "Koedoe", "trophyPhotoPath": photo_path, "antlerSizeCm": 132.5, "carcassWeightKg": 210,
       "harvestDate": iso(start_day + timedelta(hours=6)), "locationText": "E2E Toetsplaas", "shotDistanceMeters": 180,
       "firearmType": firearm, "notes": "E2E indiening"}
r = requests.post(f"{MOBILE}/api/trophy-submissions", headers=M["hunter"], json=sub)
sid = (r.json() or {}).get("submissionId") or (r.json() or {}).get("id") if r.ok else None
check(A, "hunter submits trophy for PH approval", r.status_code in (200, 201, 202) and sid, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{MOBILE}/api/trophy-submissions", headers=M["hunter2"], json=sub)
check(A, "hunter not on the trip can't submit", r.status_code in (403, 404, 409), f"{r.status_code} {r.text[:120]}")
r = requests.get(f"{MOBILE}/api/trophy-submissions", headers=M["ph"])
check(A, "PH sees the submission", r.ok and str(sid) in r.text, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{MOBILE}/api/trophy-submissions/{sid}/photo", headers=M["ph"])
check(A, "PH sees the submission photo", r.status_code == 200, r.status_code)
r = requests.post(f"{MOBILE}/api/trophy-submissions/{sid}/review", headers=M["hunter"], json={"action": "Approve", "actionKey": str(__import__('uuid').uuid4())})
check(A, "hunter can't approve own submission", r.status_code == 403, r.status_code)
r = requests.post(f"{MOBILE}/api/trophy-submissions/{sid}/review", headers=M["ph"], json={"action": "RequestCorrection", "reason": "Foto te donker", "actionKey": str(__import__('uuid').uuid4())})
check(A, "PH requests a correction", r.status_code in (200, 204), f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{MOBILE}/api/trophy-submissions/{sid}/resubmit", headers=M["hunter"], json={"notes": "Beter foto", "trophyPhotoPath": photo_path})
check(A, "hunter resubmits", r.status_code in (200, 204), f"{r.status_code} {r.text[:150]}")
key = str(__import__('uuid').uuid4())
r = requests.post(f"{MOBILE}/api/trophy-submissions/{sid}/review", headers=M["ph"], json={"action": "Approve", "actionKey": key})
check(A, "PH approves", r.status_code in (200, 204), f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{MOBILE}/api/trophy-submissions/{sid}/review", headers=M["ph"], json={"action": "Approve", "actionKey": key})
check(A, "double-tap approve is idempotent (no duplicate trophy)", r.status_code in (200, 204, 409), f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{MOBILE}/api/trophy-submissions/{sid}", headers=M["hunter"])
check(A, "submission shows Approved", r.ok and "approved" in r.text.lower(), f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{MOBILE}/api/trophies", headers=M["hunter"])
mine = r.json() if r.ok else []
check(A, "approved submission becomes a trophy in the trophy room", len(mine) == 1, r.text[:200])
if mine:
    t = mine[0]
    r = requests.get(MOBILE + t["photoPath"])
    check(A, "private trophy photo hidden from the public", r.status_code == 404, r.status_code)
    r = requests.get(MOBILE + t["photoPath"], headers=M["hunter"])
    check(A, "owner can view own trophy photo", r.status_code == 200, r.status_code)
    r = requests.put(f"{MOBILE}/api/trophies/{t['id']}", headers=M["hunter"], json={"species": t["species"], "photoPath": t["photoPath"],
                     "firearmType": t.get("firearmType") or firearm, "harvestDate": t["harvestDate"], "locationText": "Bygewerk", "isPublic": True})
    check(A, "owner makes the trophy public", r.status_code in (200, 204), f"{r.status_code} {r.text[:150]}")
    check(A, "public trophy photo visible to everyone", requests.get(MOBILE + t["photoPath"]).status_code == 200)
    check(A, "public trophy page", requests.get(f"{MOBILE}/api/public-trophies/{t['id']}").status_code == 200)
    r = requests.delete(f"{MOBILE}/api/trophies/{t['id']}", headers=M["hunter2"])
    check(A, "other user can't delete my trophy", r.status_code in (403, 404), r.status_code)

# ---------------------------------------------------------------- farm rating
r = requests.post(f"{MOBILE}/api/farm-ratings/bookings/{past}", headers=M["hunter"], json={"stars": 4, "description": "Mooi plaas, goeie lodge."})
check(A, "hunter rates the farm after the trip", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{MOBILE}/api/farm-ratings/bookings/{state['bookings']['main']}", headers=M["hunter"], json={"stars": 5})
check(A, "can't rate the farm before the trip", r.status_code == 400, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{MOBILE}/api/farm-ratings/farms/{state['farmId']}/summary", headers=M["hunter"])
check(A, "farm rating summary", r.ok and "4" in r.text, f"{r.status_code} {r.text[:150]}")

# ---------------------------------------------------------------- route, media, recap (real ffmpeg on the server)
A = "story"
t0 = start_day + timedelta(hours=4)
pts = [(-24.3 + i * 0.0011, 28.1 + math.sin(i / 4) * 0.0013, t0 + timedelta(minutes=8 * i)) for i in range(25)]
def hav(a, b):
    la1, lo1, la2, lo2 = map(math.radians, (a[0], a[1], b[0], b[1]))
    h = math.sin((la2 - la1) / 2) ** 2 + math.cos(la1) * math.cos(la2) * math.sin((lo2 - lo1) / 2) ** 2
    return 6371000 * 2 * math.atan2(math.sqrt(h), math.sqrt(1 - h))
route = {"sourceHuntSessionId": "staging-e2e-1", "distanceMeters": sum(hav(pts[i], pts[i + 1]) for i in range(24)), "durationSeconds": 24 * 480,
         "points": [{"latitude": p[0], "longitude": p[1], "timestamp": iso(p[2])} for p in pts],
         "pins": [{"id": "shot-1", "type": "Shot", "latitude": pts[18][0], "longitude": pts[18][1], "timestamp": iso(pts[18][2])}]}
r = requests.post(f"{MOBILE}/api/bookings/{past}/routes", headers=M["hunter"], json=route)
rid = (r.json() or {}).get("routeId") if r.ok else None
check(A, "hunter submits GPS route", r.status_code == 201, f"{r.status_code} {r.text[:200]}")
r = requests.put(f"{MOBILE}/api/bookings/{past}/routes/{rid}/selection", headers=M["hunter"])
check(A, "hunter can't select the route (PH only)", r.status_code == 403, r.status_code)
r = requests.put(f"{MOBILE}/api/bookings/{past}/routes/{rid}/selection", headers=M["ph"])
check(A, "PH selects route", r.ok, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{MOBILE}/api/bookings/{past}/routes/selected", headers=M["vg"])
check(A, "VG sees selected route", r.status_code == 200, f"{r.status_code} {r.text[:100]}")

def up(k, field, name, mime, minutes, path="", extra=None):
    with open(os.path.join(MEDIA, name), "rb") as fh:
        return requests.post(f"{MOBILE}/api/bookings/{past}/media{path}", headers=M[k], files={field: (name, fh, mime)},
                             data={"originalCaptureTimestamp": iso(t0 + timedelta(minutes=minutes)), **(extra or {})}, timeout=120)
r = up("hunter", "photo", "dawn.jpg", "image/jpeg", 20, extra={"caption": "Eerste lig"})
check(A, "hunter uploads photo", r.status_code == 201, f"{r.status_code} {r.text[:150]}")
r = up("vg", "video", "stalk.mp4", "video/mp4", 60, "/videos", {"caption": "Die bekruip"})
check(A, "VG uploads landscape clip (server ffprobe)", r.status_code == 201 and r.json().get("sourceType") == "Video", f"{r.status_code} {r.text[:150]}")
r = up("vg", "video", "shot.mov", "video/quicktime", 140, "/videos", {"caption": "Die skoot!"})
check(A, "VG uploads portrait HEVC .mov", r.status_code == 201, f"{r.status_code} {r.text[:150]}")
r = up("ph", "photo", "vg_photo.jpg", "image/jpeg", 170, extra={"caption": "Die span"})
check(A, "PH uploads photo", r.status_code == 201, f"{r.status_code} {r.text[:150]}")
r = up("vg", "video", "long.mp4", "video/mp4", 100, "/videos")
check(A, "35 s clip refused", r.status_code == 400 and "30" in r.text, f"{r.status_code} {r.text[:150]}")
r = up("hunter", "video", "stalk.mp4", "video/mp4", 100, "/videos")
check(A, "hunter can't upload video", r.status_code == 403, r.status_code)
r = up("hunter2", "photo", "dawn.jpg", "image/jpeg", 100)
check(A, "hunter not on trip can't upload", r.status_code == 403, r.status_code)
media = requests.get(f"{MOBILE}/api/bookings/{past}/media", headers=M["ph"]).json()
check(A, "PH sees 4 media items", len(media) == 4, len(media))
ph_photo = next(m for m in media if m["uploaderRole"] == "PH")
r = requests.post(f"{MOBILE}/api/bookings/{past}/media/{ph_photo['mediaId']}/moderation", headers=M["vg"], json={"inclusionStatus": "Excluded", "reason": "x"})
check(A, "VG can't moderate media", r.status_code == 403, r.status_code)
r = requests.post(f"{MOBILE}/api/bookings/{past}/media/{ph_photo['mediaId']}/moderation", headers=M["ph"], json={"inclusionStatus": "Excluded", "reason": "Onskerp"})
check(A, "PH excludes a photo from the story", r.status_code == 204, f"{r.status_code} {r.text[:150]}")

r = requests.post(f"{MOBILE}/api/bookings/{past}/recaps", headers=M["vg"], json={"title": "x"})
check(A, "VG can't generate the story", r.status_code == 403, r.status_code)
r = requests.post(f"{MOBILE}/api/bookings/{past}/recaps", headers=M["ph"], json={"title": "E2E Koedoe Staging"})
check(A, "PH requests story", r.status_code in (200, 202), f"{r.status_code} {r.text[:200]}")
recap = None
for _ in range(80):
    lst = requests.get(f"{MOBILE}/api/bookings/{past}/recaps", headers=M["hunter"]).json()
    recap = lst[0] if lst else None
    if recap and recap["status"] in ("Completed", "Failed"):
        break
    time.sleep(3)
check(A, "story renders on the server (Completed)", recap and recap["status"] == "Completed", recap)
if recap and recap["status"] == "Completed":
    r = requests.get(f"{MOBILE}/api/recaps/{recap['recapId']}/content", headers=M["hunter"])
    open(os.path.join(HERE, "staging_recap.mp4"), "wb").write(r.content)
    check(A, f"story downloads ({len(r.content)//1024} KB, {recap.get('durationSeconds')} s)", r.ok and len(r.content) > 50_000, r.status_code)
    # 2 + 25*0.1 + hunter photo 2.8 + clip 0.8+6 + clip 0.8+4 + summary 3 = 21.9 (PH photo excluded)
    check(A, "story length matches (PH photo excluded, 2 clips in)", recap.get("durationSeconds") == 22, recap.get("durationSeconds"))
    r = requests.get(f"{MOBILE}/api/recaps/{recap['recapId']}/content", headers=M["hunter2"])
    check(A, "outsider can't download the story", r.status_code in (403, 404), r.status_code)
    r = requests.post(f"{MOBILE}/api/bookings/{past}/recaps/{recap['recapId']}/publish", headers=M["ph"], json={"privacyStatus": "private", "idempotencyKey": str(__import__('uuid').uuid4())})
    print(f"    YouTube publish -> {r.status_code} {r.text[:200]}")
    state["recapId"] = recap["recapId"]
r = requests.get(f"{MOBILE}/api/admin/youtube/oauth/start", headers=bearer(mobile_login(*require_admin())[1]['accessToken']))
print(f"    YouTube OAuth start -> {r.status_code} {r.text[:150]}")

# ---------------------------------------------------------------- team ratings via the app (relayed to WebAPI)
r = requests.get(f"{MOBILE}/api/bookings/{past}/team-ratings", headers=M["hunter"])
check("ratings", "app team ratings reachable", r.status_code == 200, f"{r.status_code} {r.text[:150]}")

json.dump(state, open(os.path.join(HERE, "staging_state.json"), "w"), indent=1)
save_results("t8_mobile")
