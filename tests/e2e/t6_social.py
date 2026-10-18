"""Phase 6: chats + moderation + notifications, PJ team invites, permits/SAPS 520, weapons, stories, public info."""
from datetime import date, timedelta
from st import *

state = json.load(open(os.path.join(HERE, "staging_state.json")))
U = state["users"]
H = lambda k: bearer(U[k]["token"])
uid = lambda k: U[k]["userId"]
ur = lambda k: U[k]["roles"][0]["userRoleId"]
ADMIN = admin_headers()
main = state["bookings"]["main"]

# ------------------------------------------------------------------ chats
A = "chat"
r = requests.post(f"{WEB}/api/Chats/direct/{uid('pj')}", headers=H("hunter"), json={"senderId": uid("hunter")})
chat = (r.json() or {}).get("chatId") or (r.json() or {}).get("id") if r.ok else None
check(A, "hunter opens a direct chat with the PJ", r.status_code == 200 and chat, f"{r.status_code} {r.text[:200]}")
r2 = requests.post(f"{WEB}/api/Chats/direct/{uid('pj')}", headers=H("hunter"), json={"senderId": uid("hunter")})
chat2 = (r2.json() or {}).get("chatId") or (r2.json() or {}).get("id") if r2.ok else None
check(A, "re-opening returns the same chat (no duplicates)", chat2 == chat, f"{chat} vs {chat2}")
r = requests.post(f"{WEB}/api/Chats/{chat}/messages", headers=H("hunter"), json={"senderId": uid("hunter"), "content": "Goeiedag! Is die lodge beskikbaar?"})
check(A, "hunter sends a message", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/Chats/{chat}/messages", headers=H("pj"), json={"senderId": uid("pj"), "content": "Ja, sien jou daar."})
check(A, "PJ replies", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/Chats/{chat}/messages", headers=H("pj"))
msgs = r.json() if r.ok else []
msgs = msgs.get("messages", msgs) if isinstance(msgs, dict) else msgs
check(A, "both messages in the thread", r.ok and len(msgs) >= 2, f"{r.status_code} {str(msgs)[:200]}")
r = requests.get(f"{WEB}/api/Chats/{chat}/messages", headers=H("hunter2"))
check(A, "outsider can't read the chat (403/404)", r.status_code in (403, 404), r.status_code)
r = requests.post(f"{WEB}/api/Chats/{chat}/messages", headers=H("hunter2"), json={"senderId": uid("hunter2"), "content": "inbreker"})
check(A, "outsider can't post into the chat (403/404)", r.status_code in (403, 404), r.status_code)
r = requests.post(f"{WEB}/api/Chats/{chat}/messages", headers=H("hunter"), json={"senderId": uid("pj"), "content": "gespoof"})
spoof = sql(f"SELECT TOP 1 cmSenderId FROM chatMessages WHERE cmContent = N'gespoof' ORDER BY 1 DESC") if r.ok else ""
check(A, "can't send as someone else (senderId spoof)", not r.ok or spoof == str(uid("hunter")), f"{r.status_code} sender stored as {spoof}")
r = requests.post(f"{WEB}/api/Chats/{chat}/messages", headers=H("hunter"), json={"senderId": uid("hunter"), "content": "Kan ek kontant betaal via whatsapp?"})
check(A, "fee-bypass message still delivered", r.status_code == 200, r.status_code)
r = requests.get(f"{WEB}/api/admin/flagged-messages", headers=ADMIN)
check(A, "fee-bypass message flagged for admin", r.ok and "whatsapp" in r.text.lower(), f"{r.status_code} {r.text[:200]}")
flag = next((f for f in (r.json() if r.ok else []) if "whatsapp" in json.dumps(f).lower()), None)
if flag:
    fid = flag.get("flagId") or flag.get("id")
    r = requests.post(f"{WEB}/api/admin/flagged-messages/{fid}/moderate", headers=ADMIN, json={"action": "Dismiss", "reason": "E2E"})
    check(A, "admin moderates flagged message", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/Chats", headers=H("pj"))
check(A, "PJ's chat list contains the chat", r.ok and str(chat) in r.text, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/Chats/notifications", headers=H("pj"))
check(A, "unread notification status", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
last = max((m.get("messageId") or m.get("id") or 0) for m in msgs) if msgs else 0
r = requests.post(f"{WEB}/api/Chats/{chat}/read", headers=H("pj"), json={"chatId": chat, "userId": uid("pj"), "lastReadMessageId": last})
check(A, "mark chat read", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
own = next((m for m in msgs if (m.get("senderId") or m.get("cmSenderId")) == uid("hunter")), None)
if own:
    mid = own.get("messageId") or own.get("id")
    r = requests.delete(f"{WEB}/api/Chats/deleteMessage/{mid}/{uid('hunter')}", headers=H("pj"))
    check(A, "can't delete someone else's message", r.status_code in (403, 404), r.status_code)
    r = requests.delete(f"{WEB}/api/Chats/deleteMessage/{mid}/{uid('hunter')}", headers=H("hunter"))
    check(A, "delete own message", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/Chats/createChat", headers=H("pj"), json={"title": "E2E Span", "senderId": uid("pj"), "message": "Welkom by die span!", "chatParticipants": [{"userId": uid("ph")}, {"userId": uid("vg")}]})
check(A, "PJ creates a group chat", r.status_code == 200, f"{r.status_code} {r.text[:200]}")

# ------------------------------------------------------------------ PJ team invites
A = "team"
r = requests.get(f"{WEB}/api/Team/browse", headers=H("pj"), params={"roles": "PH,VG,Boer"})
check(A, "PJ browses PH/VG/farmers to recruit", r.ok and ("E2EPh" in r.text or str(uid("ph")) in r.text), f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/Team/sendInvite", headers=H("hunter"), json={"pjUserId": uid("pj"), "memberUserRoleId": ur("ph"), "targetUserId": uid("ph")})
check(A, "non-PJ can't send invites as the PJ (403)", r.status_code == 403, r.status_code)
r = requests.post(f"{WEB}/api/Team/sendInvite", headers=H("pj"), json={"pjUserId": uid("pj"), "memberUserRoleId": ur("ph"), "targetUserId": uid("ph"),
                                                                       "expeditionName": "E2E Seisoen", "terms": "R4500/dag"})
check(A, "PJ invites PH", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/Team/getInvitesForUser/{uid('ph')}", headers=H("ph"))
inv = next((i for i in (r.json() if r.ok else []) if i.get("pjUserId") == uid("pj") and i.get("status") == "Pending"), None)
check(A, "PH sees the invite", inv is not None, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/Team/getInvitesForUser/{uid('ph')}", headers=H("hunter"))
check(A, "others can't read the PH's invites", r.status_code == 403 or "E2E Seisoen" not in r.text, r.status_code)
r = requests.get(f"{WEB}/api/Team/getSentInvites/{uid('pj')}", headers=H("pj"))
check(A, "PJ sees sent invites (with expedition name)", r.ok and "E2E Seisoen" in r.text, f"{r.status_code} {r.text[:200]}")
iid = inv and (inv.get("inviteId") or inv.get("id"))
if iid:
    r = requests.post(f"{WEB}/api/Team/respondInvite/{iid}", headers=H("hunter"), params={"accept": "true"})
    check(A, "someone else can't accept the PH's invite", r.status_code in (403, 404), r.status_code)
    r = requests.post(f"{WEB}/api/Team/respondInvite/{iid}", headers=H("ph"), params={"accept": "true"})
    check(A, "PH accepts the invite", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
check(A, "invite carries expedition name + terms", inv is not None and inv.get("expeditionName") == "E2E Seisoen" and inv.get("terms") == "R4500/dag", inv)
r = requests.get(f"{WEB}/api/Team/getTeamMembers/{uid('pj')}", headers=H("pj"))
members = r.json() if r.ok else []
team_row = next((m for m in members if str(uid("ph")) in json.dumps(m) or "E2EPh" in json.dumps(m)), None)
check(A, "PH is now on the PJ's team", team_row is not None, f"{r.status_code} {r.text[:250]}")
r = requests.get(f"{WEB}/api/Team/notifications/{uid('ph')}", headers=H("ph"))
check(A, "PH got a notification", r.ok and len(r.json()) >= 1, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{WEB}/api/Team/notifications/unreadCount/{uid('ph')}", headers=H("ph"))
check(A, "unread notification count", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{WEB}/api/Team/notifications/{uid('ph')}", headers=H("hunter"))
check(A, "can't read someone else's notifications (403)", r.status_code == 403, r.status_code)
r = requests.get(f"{WEB}/api/Team/removalReasons")
check(A, "removal reasons list", r.ok and len(r.json()) >= 1, f"{r.status_code} {r.text[:150]}")
if team_row:
    tid = team_row.get("teamId") or team_row.get("id")
    r = requests.get(f"{WEB}/api/Team/checkRemovalConflicts/{tid}", headers=H("pj"))
    check(A, "removal conflict check (PH is on an upcoming booking)", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
    r = requests.post(f"{WEB}/api/Team/removeMember", headers=H("hunter"), json={"teamId": tid, "reasonCode": "Other", "pjUserId": uid("pj")})
    check(A, "non-PJ can't remove team members (403)", r.status_code in (403, 404), r.status_code)

# ------------------------------------------------------------------ permits, SAPS 520, weapons
A = "permits"
r = requests.post(f"{WEB}/api/huntingpermit/newApplication", headers=H("hunter"), params={"userRoleId": ur("hunter")},
                  json={"farmId": state["farmId"], "startDate": str(date.today() + timedelta(days=30)), "endDate": str(date.today() + timedelta(days=33)), "species": "Koedoe", "quantity": 2})
check(A, "hunter applies for a hunting permit", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/huntingpermit/getApplicationsByUserRole/{ur('hunter')}", headers=H("hunter"))
apps = r.json() if r.ok else []
check(A, "hunter sees own permit application", r.ok and len(apps) >= 1, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/huntingpermit/getApplicationsByUserRole/{ur('hunter')}", headers=H("hunter2"))
check(A, "others can't read your permit applications (403)", r.status_code == 403, r.status_code)
if apps:
    pid = apps[0].get("id") or apps[0].get("applicationId") or apps[0].get("hpId")
    r = requests.post(f"{WEB}/api/huntingpermit/setApproval/{pid}", headers=ADMIN, json="Approved")
    check(A, "admin approves permit", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
saps = {"userId": uid("hunter"), "surname": "Toets", "fullNames": "E2E Hunter", "permitPassportNum": "P1234567", "countryOfOrigin": "Namibia",
        "firearms": [{"type": "Rifle", "action": "Bolt", "calibre": ".308", "make": "Tikka", "model": "T3x", "frameOrReceiverSerialNumber": "A1", "barrelSerialNumber": "B1"}],
        "ammunition": [{"type": ".308", "quantity": 60}], "fromDate": str(date.today() + timedelta(days=29)), "toDate": str(date.today() + timedelta(days=34))}
r = requests.post(f"{WEB}/api/huntingpermit/submitSaps520", headers=H("hunter"), json=saps)
check(A, "hunter submits SAPS 520", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/huntingpermit/submitSaps520", headers=H("hunter"), json=saps)
check(A, "second SAPS 520 for another trip also accepted", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/huntingpermit/newApplication", headers=H("hunter"), params={"userRoleId": ur("hunter")}, json={"farmId": state["farmId"], "startDate": str(date.today() + timedelta(days=60)), "endDate": str(date.today() + timedelta(days=62)), "species": "Rooibok", "quantity": 1})
check(A, "second hunting-permit application accepted", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/huntingpermit/submitSaps520", headers=H("hunter"), json={**saps, "userId": uid("hunter2")})
check(A, "can't submit SAPS 520 for someone else (403)", r.status_code == 403, r.status_code)
r = requests.post(f"{WEB}/api/huntingpermit/submitSaps520", headers=H("hunter"), json={"userId": uid("hunter"), "rubbish": True})
check(A, "SAPS 520 with no ID/passport/firearms is refused", r.status_code == 400, f"{r.status_code} {r.text[:150]}")

r = requests.post(f"{WEB}/api/Weapon/addWeapon", headers=H("hunter"), json={"udUserRole": ur("hunter"), "udName": "E2E Tikka .308", "udStatus": "Pending"})
check(A, "hunter registers a weapon", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
wid = r.json() if r.ok and isinstance(r.json(), int) else (r.json() or {}).get("id") if r.ok else None
r = requests.post(f"{WEB}/api/Weapon/addWeapon", headers=H("hunter2"), json={"udUserRole": ur("hunter"), "udName": "Ingeplant"})
check(A, "can't add a weapon to someone else's profile (403)", r.status_code == 403, r.status_code)
if wid:
    r = requests.get(f"{WEB}/api/Weapon/getWeapon/{wid}", headers=H("hunter2"))
    check(A, "others can't read your weapon (403/404)", r.status_code in (403, 404), r.status_code)
    r = requests.delete(f"{WEB}/api/Weapon/deleteWeapon/{wid}", headers=H("hunter2"))
    check(A, "others can't delete your weapon (403/404)", r.status_code in (403, 404), r.status_code)
r = requests.post(f"{WEB}/api/Permit/createUserPermit", headers=H("hunter"), json={"udUserRole": ur("hunter"), "udName": "E2E Jaglisensie"})
check(A, "hunter adds a personal permit/licence", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/Permit/getUserPermits/{ur('hunter')}", headers=H("hunter"))
check(A, "hunter lists own permits", r.ok and "E2E Jaglisensie" in r.text, f"{r.status_code} {r.text[:200]}")
r = requests.get(f"{WEB}/api/Permit/getUserPermits/{ur('hunter')}", headers=H("hunter2"))
check(A, "others can't list your permits (403)", r.status_code == 403, r.status_code)
r = requests.get(f"{WEB}/api/Permits/{uid('hunter')}", headers=H("hunter"))
check(A, "permits wallet endpoint", r.status_code == 200, f"{r.status_code} {r.text[:150]}")

# ------------------------------------------------------------------ stories
A = "story"
r = requests.post(f"{WEB}/api/Story/createStory", headers=H("hunter"), json={"sdBookingId": main, "sdExpeditionNum": 1})
sid = (r.json() if r.ok and isinstance(r.json(), int) else (r.json() or {}).get("id")) if r.ok else None
check(A, "hunter creates a story for own booking", r.status_code == 200, f"{r.status_code} {r.text[:200]}")
r = requests.post(f"{WEB}/api/Story/createStory", headers=H("hunter2"), json={"sdBookingId": state["bookings"]["rejected"], "sdExpeditionNum": 1})
check(A, "can't create a story on someone else's booking (403)", r.status_code == 403, r.status_code)
if sid:
    r = requests.post(f"{WEB}/api/Story/editStory/{sid}", headers=H("pj"), json={"sdExpeditionNum": 9})
    check(A, "booking participant (PJ) may edit the story", r.status_code == 200, r.status_code)
    r = requests.post(f"{WEB}/api/Story/editStory/{sid}", headers=H("hunter2"), json={"sdExpeditionNum": 9})
    check(A, "outsider can't edit the story (403)", r.status_code == 403, r.status_code)

# ------------------------------------------------------------------ public info & misc
A = "public"
for path in ["/api/Extra/getTaxidermy", "/api/Extra/getSlaughterhouses", "/api/Extra/getDangerousAnimals", "/api/Extra/provinces",
             "/api/Landing/getContact", "/api/Landing/getPackages", "/api/Landing/getLeaderboard", "/api/Settings/public",
             "/api/user/getAvailableRoles", f"/api/recruit/getPerson/{ur('ph')}"]:
    r = requests.get(WEB + path, headers=H("hunter"))
    check(A, f"GET {path}", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.get(f"{WEB}/api/Extra/provinces")
check(A, "9 provinces listed", r.ok and len(r.json()) == 9, f"{len(r.json()) if r.ok else r.status_code} provinces")
r = requests.post(f"{WEB}/api/Contact", json={"name": "E2E", "email": "contact@staging-e2e.test", "subject": "E2E toets", "message": "Toetsboodskap van die staging E2E-lopie."})
check(A, "contact form accepted", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
r = requests.post(f"{WEB}/api/Contact", json={"name": "", "email": "nie-n-epos", "subject": "", "message": ""})
check(A, "contact form validates input", r.status_code == 400, f"{r.status_code} {r.text[:150]}")

json.dump(state, open(os.path.join(HERE, "staging_state.json"), "w"), indent=1)
save_results("t6_social")
