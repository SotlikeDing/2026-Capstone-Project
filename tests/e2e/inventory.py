"""Parse every ASP.NET controller into a JSON endpoint inventory (verb, route, auth, params)."""
import json, os, re, sys

from st import HERE

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "src")
OUT = os.path.join(HERE, "inventory.json")
VERB = re.compile(r'\[Http(Get|Post|Put|Patch|Delete)(?:\(\s*"([^"]*)"[^)]*\)|\(\s*Name\s*=[^)]*\))?')
ROUTE = re.compile(r'\[Route\(\s*"([^"]*)"\s*\)\]')
METHOD = re.compile(r'public\s+(?:async\s+)?(?:Task<[^>]+>|Task|IActionResult|ActionResult<[^>]+>|[\w<>]+)\s+(\w+)\s*\(([^)]*)\)', re.S)


def parse(path, api):
    src = open(path, encoding="utf-8").read()
    endpoints = []
    # split per class
    for cls in re.finditer(r'public\s+(?:sealed\s+)?class\s+(\w+)\s*:\s*ControllerBase', src):
        name = cls.group(1)
        # attributes = everything between the previous class/namespace/using block and this declaration
        lines = src[: cls.start()].splitlines()
        picked = []
        for l in reversed(lines):
            s = l.strip()
            if s.startswith("[") or s.startswith("//") or s == "":
                if s.startswith("["):
                    picked.append(s)
                continue
            break
        attrs = "\n".join(picked)
        start = cls.end()
        nxt = re.search(r'\n(?:\[[^\]]+\]\s*)*public\s+(?:sealed\s+)?class\s+\w+\s*:\s*ControllerBase', src[start:])
        body = src[start: start + nxt.start()] if nxt else src[start:]
        base = (ROUTE.search(attrs).group(1) if ROUTE.search(attrs) else "").replace("[controller]", name.replace("Controller", ""))
        cls_auth = "Authorize" in attrs
        cls_roles = re.findall(r'Roles\s*=\s*"([^"]+)"', attrs)
        # walk attribute blocks preceding public methods
        for m in re.finditer(r'((?:\s*\[[^\]]*(?:\([^)]*\))?[^\]]*\]\s*)+)\s*public\s+(?:async\s+)?[\w<>\[\],\s]+?\s+(\w+)\s*\(([^)]*)\)', body, re.S):
            block, method, params = m.group(1), m.group(2), m.group(3)
            v = VERB.search(block)
            if not v:
                continue
            tmpl = v.group(2) or ""
            r2 = ROUTE.search(block)
            if r2 and not tmpl:
                tmpl = r2.group(1)
            route = tmpl if tmpl.startswith("api/") or tmpl.startswith("/") else (base + ("/" + tmpl if tmpl else ""))
            route = "/" + route.lstrip("/")
            endpoints.append({
                "api": api, "controller": name, "method": method, "verb": v.group(1).upper(), "route": route,
                "auth": ("anon" if "AllowAnonymous" in block else "auth" if ("Authorize" in block or cls_auth) else "none"),
                "roles": re.findall(r'Roles\s*=\s*"([^"]+)"', block) or cls_roles,
                "body": bool(re.search(r'\[FromBody\]', params)),
                "form": bool(re.search(r'IFormFile|\[FromForm\]', params)),
                "params": " ".join(params.split())[:300],
            })
    return endpoints


all_eps = []
for api, folder in [("web", r"WebAPI\WebAPI\Controllers"), ("mobile", r"MobileAPI\MobileAPI\Controllers")]:
    for f in sorted(os.listdir(os.path.join(ROOT, folder))):
        if f.endswith(".cs"):
            all_eps += parse(os.path.join(ROOT, folder, f), api)
json.dump(all_eps, open(OUT, "w"), indent=1)
print(len(all_eps), "endpoints")
by = {}
for e in all_eps:
    by.setdefault((e["api"], e["auth"]), 0)
    by[(e["api"], e["auth"])] += 1
print(by)
if "-v" in sys.argv:
    for e in all_eps:
        print(e["api"], e["verb"], e["route"], e["auth"], e["roles"], "BODY" if e["body"] else "", "FORM" if e["form"] else "")
