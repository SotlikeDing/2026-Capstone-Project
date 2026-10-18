"""Which hosted LLM writes the best advice? Runs the SAME production prompts through several OpenAI-compatible providers and
scores every answer with the quality gates in advice_common.check_advice, plus a price-verdict check.

Prompts come from the running WebAPI's admin dry-run endpoint (realistic random requests for every role), so the comparison uses
exactly what production sends. Keys are read from environment variables, never from the command line:
  GEMINI_API_KEY  GROQ_API_KEY  OPENROUTER_API_KEY  MISTRAL_API_KEY  GITHUB_TOKEN

  python compare_providers.py --api https://localhost:7038 --insecure --providers Gemini Groq --count 12
  python compare_providers.py ... --providers Gemini Groq=llama-3.1-8b-instant      # PROVIDER=MODEL overrides the default model

Reported per provider (higher is better except latency):
  json_ok      answer parsed and every schema field is present
  clean        passes all quality gates (no invented numbers, no duplicate tips, Afrikaans, enough items)
  verdict_ok   the headline states the price verdict (above / below / within) the numbers imply
  no_invented  no number outside the context (the "the LLM never invents prices" rule)
  avg_s        mean answer time; errors = failed calls (rate limits show up here on free tiers)
Free tiers have per-minute limits: keep --count small and use --pause. Judge by the trend, not one run.
"""
from __future__ import annotations

import argparse
import json
import os
import time
import urllib.error
import urllib.request

from advice_common import Api, check_advice, random_request

# Mirrors LlmPresets in src/WebAPI/WebAPI/Services/Ai/LlmClients.cs - keep the two in step.
PRESETS = {
    "Gemini": ("https://generativelanguage.googleapis.com/v1beta/openai/", "gemini-flash-latest", "json_schema", "GEMINI_API_KEY", {"reasoning_effort": "low"}),
    "Groq": ("https://api.groq.com/openai/v1/", "openai/gpt-oss-120b", "json_object", "GROQ_API_KEY", {"reasoning_effort": "low"}),
    "OpenRouter": ("https://openrouter.ai/api/v1/", "meta-llama/llama-3.3-70b-instruct:free", "json_object", "OPENROUTER_API_KEY", {}),
    "Mistral": ("https://api.mistral.ai/v1/", "mistral-small-latest", "json_object", "MISTRAL_API_KEY", {}),
    "GitHub": ("https://models.github.ai/inference/", "openai/gpt-4.1-mini", "json_schema", "GITHUB_TOKEN", {}),
}

VERDICT_WORDS = {
    "above": ["bo ", "bo-", "hoër", "duur", "bokant", "oor die", "oor u", "oor jou"],
    "below": ["onder", "laer", "goedkoop", "te laag", "laag"],
    "within": ["in lyn", "binne", "gepas", "markverwant", "marktoepaslik", "tussen", "billik"],
}


def extract_json(text: str) -> dict:
    start, end = text.find("{"), text.rfind("}")
    if start < 0 or end <= start:
        raise ValueError("no JSON object in the answer")
    return json.loads(text[start:end + 1])


def post(url: str, key: str, body: dict) -> tuple[int, str]:
    req = urllib.request.Request(url, data=json.dumps(body).encode("utf-8"), method="POST",
                                 headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            return r.status, r.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")


def ask(provider: str, model: str, key: str, system: str, user: str, schema: str) -> dict:
    """Same request shape and json_schema -> json_object retry as OpenAiCompatibleLlmClient."""
    base, _, mode, _, extra = PRESETS[provider]
    for attempt_mode in ([mode, "json_object"] if mode == "json_schema" else [mode]):
        sys_text = system if attempt_mode == "json_schema" else (
            system + "\n\nReply with ONE JSON object (no markdown, no text around it) that matches this JSON schema:\n" + schema)
        body = {"model": model, "temperature": 0.3, "max_tokens": 4096,
                "messages": [{"role": "system", "content": sys_text}, {"role": "user", "content": user}],
                "response_format": {"type": "json_schema", "json_schema": {"name": "advice", "schema": json.loads(schema)}}
                if attempt_mode == "json_schema" else {"type": "json_object"}, **extra}
        status, text = post(base + "chat/completions", key, body)
        if status == 400 and attempt_mode == "json_schema":
            continue
        if status != 200:
            raise RuntimeError(f"HTTP {status}: {text[:200]}")
        choice = json.loads(text)["choices"][0]
        if choice.get("finish_reason") == "length":
            raise RuntimeError("answer truncated")
        return extract_json(choice["message"]["content"])
    raise RuntimeError("provider rejected both JSON modes")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--api", required=True, help="the running WebAPI, e.g. https://localhost:7038")
    ap.add_argument("--token", default=os.environ.get("MIKPUNT_ADMIN_TOKEN"), help="Admin JWT (or set MIKPUNT_ADMIN_TOKEN)")
    ap.add_argument("--insecure", action="store_true", help="accept the self-signed localhost certificate")
    ap.add_argument("--providers", nargs="+", required=True, help=f"any of {', '.join(PRESETS)}; PROVIDER=MODEL to override the model")
    ap.add_argument("--count", type=int, default=12, help="prompts per provider")
    ap.add_argument("--pause", type=float, default=2.0, help="seconds between calls (free-tier rate limits)")
    ap.add_argument("--seed", type=int, default=5000)
    args = ap.parse_args()
    if not args.token:
        raise SystemExit("Pass --token (an Admin JWT) or set MIKPUNT_ADMIN_TOKEN.")

    api = Api(args.api, args.token, args.insecure)
    fx = float(api.status()["usdToZarRate"])
    roles = ["PJ", "PH", "VG", "FARMER", "AP"]
    prompts = []
    for i in range(args.count):
        p = api.dry_run(random_request(roles[i % len(roles)], args.seed + i, fx))
        prompts.append((p["systemPrompt"], p["userPrompt"], p["jsonSchema"], ((p.get("numbers") or {}).get("price") or {}).get("position")))
    print(f"{len(prompts)} production prompts\n")
    print(f"{'provider/model':52s} {'json_ok':>8s} {'clean':>7s} {'verdict_ok':>11s} {'no_invented':>12s} {'avg_s':>6s} {'errors':>7s}")

    for spec in args.providers:
        name, _, model_override = spec.partition("=")
        if name not in PRESETS:
            print(f"{spec:52s} unknown provider - choose from {', '.join(PRESETS)}")
            continue
        key = os.environ.get(PRESETS[name][3])
        if not key:
            print(f"{spec:52s} skipped: set {PRESETS[name][3]}")
            continue
        model = model_override or PRESETS[name][1]
        n = json_ok = clean = verdict = verdict_n = no_inv = errors = 0
        secs, last_error = [], ""
        for system, user, schema, position in prompts:
            n += 1
            t0 = time.time()
            try:
                advice = ask(name, model, key, system, user, schema)
            except Exception as e:  # noqa: BLE001 - every failure is a data point here
                errors += 1
                last_error = str(e)[:120]
                time.sleep(args.pause)
                continue
            secs.append(time.time() - t0)
            problems = check_advice(advice, user)
            json_ok += not any(p.startswith("missing") for p in problems)
            clean += not problems
            no_inv += not any(p.startswith("invented") for p in problems)
            if position in VERDICT_WORDS:
                verdict_n += 1
                head = (advice.get("headline") or "").lower() + " "
                verdict += any(w in head for w in VERDICT_WORDS[position])
            time.sleep(args.pause)
        pct = lambda a, b: f"{100 * a / b:5.0f}%" if b else "  n/a"
        avg = f"{sum(secs) / len(secs):6.1f}" if secs else "     -"
        print(f"{name + '/' + model:52s} {pct(json_ok, n):>8s} {pct(clean, n):>7s} {pct(verdict, verdict_n):>11s} {pct(no_inv, n):>12s} {avg} {errors:7d}")
        if last_error:
            print(f"{'':52s} last error: {last_error}")


if __name__ == "__main__":
    main()
