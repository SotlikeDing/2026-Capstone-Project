"""Phase 4: required documents per role, upload/replace/delete, admin verification, gating of listings."""
from st import *

state = json.load(open(os.path.join(HERE, "staging_state.json")))
U = state["users"]
H = lambda k: bearer(U[k]["token"])
ur = lambda k: U[k]["roles"][0]["userRoleId"]
A = "documents"
ADMIN = admin_headers()
PDF = "data:application/pdf;base64," + base64.b64encode(b"%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n").decode()
ROLE_CODES = {"pj": "PJ", "ph": "PH", "vg": "VG", "farmer": "FARMER", "hunter": "HUNTER", "ap": "AP"}

# Visibility before approval
before_people = {code: requests.get(f"{WEB}/api/recruit/getPeople/{code}", headers=H("pj")).text for code in ("PH", "VG")}

uploaded = {}
for k, code in ROLE_CODES.items():
    r = requests.get(f"{WEB}/api/documents/requirements/{code}")
    reqs = r.json() if r.ok else []
    check(A, f"requirements for {code}", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
    for d in reqs:
        dt = d.get("documentTypeId") or d.get("dtId") or d.get("id")
        r = requests.post(f"{WEB}/api/documents/{ur(k)}/upload", headers=H(k), json={"documentTypeId": dt, "fileName": f"{k}-{dt}.pdf", "fileDataUrl": PDF})
        ok = check(A, f"{k} uploads '{d.get('name') or d.get('documentTypeName') or dt}'", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
        if ok:
            uploaded.setdefault(k, []).append(r.json().get("id"))
    print(f"   {code}: {len(reqs)} required document types")

k0 = next(iter(uploaded), None)
if k0:
    dt_any = requests.get(f"{WEB}/api/documents/requirements/{ROLE_CODES[k0]}").json()[0]
    dt_any = dt_any.get("documentTypeId") or dt_any.get("dtId") or dt_any.get("id")
    r = requests.post(f"{WEB}/api/documents/{ur(k0)}/upload", headers=H("hunter2"), json={"documentTypeId": dt_any, "fileName": "x.pdf", "fileDataUrl": PDF})
    check(A, "can't upload documents for someone else (403)", r.status_code == 403, r.status_code)
    r = requests.post(f"{WEB}/api/documents/{ur(k0)}/upload", headers=H(k0), json={"documentTypeId": dt_any, "fileName": "evil.exe", "fileDataUrl": "data:application/x-msdownload;base64,TVqQ"})
    check(A, "non PDF/PNG/JPEG upload refused (400)", r.status_code == 400, f"{r.status_code} {r.text[:120]}")
    r = requests.get(f"{WEB}/api/documents/{ur(k0)}", headers=H("hunter2"))
    check(A, "can't list someone else's documents (403)", r.status_code == 403, r.status_code)
    r = requests.get(f"{WEB}/api/documents/{ur(k0)}", headers=H(k0))
    check(A, "owner lists own documents", r.ok and len(r.json()) >= 1, f"{r.status_code} {r.text[:150]}")
    r = requests.get(f"{WEB}/api/documents/{ur(k0)}/summary", params={"roleCode": ROLE_CODES[k0]})
    check(A, "document summary (public) works", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
    check(A, "public summary doesn't leak file contents", "base64" not in r.text, r.text[:200])

# Admin queue: reject one, owner replaces, then approve everything
r = requests.get(f"{WEB}/api/admin/document-verifications", headers=ADMIN, params={"status": "Pending"})
queue = r.json() if r.ok else []
check(A, "admin sees pending documents", r.ok and len(queue) >= sum(len(v) for v in uploaded.values()), f"{r.status_code} {len(queue)}")
mine = [d for d in queue if (d.get("documentId") or d.get("id")) in {i for v in uploaded.values() for i in v}]
r = requests.post(f"{WEB}/api/admin/document-verifications/{(mine or [{}])[0].get('documentId') or (mine or [{}])[0].get('id')}/decision", headers=H("pj"), json={"approve": True})
check(A, "non-admin can't approve documents (403)", r.status_code == 403, r.status_code)
if mine:
    first = mine[0].get("documentId") or mine[0].get("id")
    r = requests.post(f"{WEB}/api/admin/document-verifications/{first}/decision", headers=ADMIN, json={"approve": False, "reason": "Onleesbaar"})
    check(A, "admin rejects a document with a reason", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
    owner = next(k for k, v in uploaded.items() if first in v)
    r = requests.put(f"{WEB}/api/documents/{first}/replace", headers=H(owner), json={"fileName": "nuut.pdf", "fileDataUrl": PDF})
    check(A, "owner replaces the rejected document", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
    st = sql(f"SELECT TOP 1 udVerificationStatus FROM userDocumentation WHERE udId = {first}") or sql(f"SELECT TOP 1 * FROM userDocumentation WHERE 1=0")
    for d in mine:
        did = d.get("documentId") or d.get("id")
        r = requests.post(f"{WEB}/api/admin/document-verifications/{did}/decision", headers=ADMIN, json={"approve": True})
        check(A, f"admin approves document {did}", r.status_code == 200, f"{r.status_code} {r.text[:150]}")
    r = requests.get(f"{WEB}/api/documents/{ur(owner)}/audit", headers=H(owner))
    check(A, "audit trail records upload/reject/replace/approve", r.ok and len(r.json()) >= 3, f"{r.status_code} {r.text[:200]}")

# Gating now lifted?
r = requests.get(f"{WEB}/api/packets/getPacket/{state['packageId']}")
check(A, "package visible once PJ documents approved", r.status_code == 200, f"{r.status_code}")
for code, key in (("PH", "ph"), ("VG", "vg")):
    r = requests.get(f"{WEB}/api/recruit/getPeople/{code}", headers=H("pj"))
    check(A, f"approved {code} appears in recruit list", r.ok and str(U[key]["userId"]) in r.text or f"E2E{key.capitalize()}" in r.text,
          f"{r.status_code} {r.text[:200]}")

save_results("t4_documents")
json.dump(state, open(os.path.join(HERE, "staging_state.json"), "w"), indent=1)
