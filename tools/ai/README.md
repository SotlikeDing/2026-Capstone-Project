# MikPunt AI Helper - training and learning pipeline

The AI Helper (`/ai-helper` in the WebApp, `api/aihelper/*` in the WebAPI) has two tabs - the **packet maker / price optimiser**
and **Smart Teaming** - built from layers that are trained differently:

| Layer | What it does | Model | Where it runs | Training cost |
|---|---|---|---|---|
| **Numbers** | Price band (P10 / P50 / P90) for a package, PH day rate, VG package, farm daily rate, species fee | 5 gradient-boosted regressors exported to **ONNX** | in-process in the WebAPI | seconds, CPU only |
| **Words** | Afrikaans advice: verdict, titles, description, inclusions, upsells, tips, risks; "why this partner" for teaming | a **hosted LLM API** reached with an API key (Gemini, Groq, ... or Claude) | the provider's cloud | none (prompted) |
| **Team match** | Ranks PH / VG / Boer partners for a PJ (and PJs for the others) | prior weights blended with a **logistic regression learned from real invite outcomes** | in-process in the WebAPI | milliseconds, on every request |

The LLM never produces a price. It is handed the model's numbers as plain-language *findings* and only writes advice around them.

## What the price model was trained on (be honest about this in your demo)

Day 0 there is almost no real platform data (1 package, 8 species prices, 2 VG packages...). So the bootstrap model is trained on
**synthetic data generated from real, published 2026/27 South African outfitter price lists** (Gaspare Spanio Safaris, Big Game
Hunting Adventures, African Sky Hunting: trophy fees for 27 species, all-in daily rates, observer rates, filming rates).
`generate_synthetic.py` tags every formula `[ANCHORED]` (reproduces a published figure) or `[ASSUMED]` (no source).

| Kind | Evidence | Notes |
|---|---|---|
| PJ package, farm daily rate, species fee, VG package | `published` | international market, USD |
| PH day rate | `assumed` | published rates bundle the PH's cost; no standalone PH price is published |
| **Domestic (ZAR, local hunter) segment - all kinds** | `assumed` | No domestic price list could be verified. Only anchor: ~R1,000 per person per night incl. meals. The API marks these answers **low confidence** |
| Female / male tier multipliers, province and season multipliers | `assumed` | listed in `data/market_facts.json` -> `assumed` |

`train_price_model.py` also runs 8 sanity checks against the published figures (kudu fee, 7-day 1x1 package, buffalo, ...).
The platform's own seed data already disagrees with the *domestic* assumption (seed kudu R8,500 vs. assumed ~R17,000) - that is
exactly what the learning loop below is for.

## The learning loop (how the system learns from itself)

```
   real listings on MikPunt ──harvest──▶ aiMarketObservations ─┐
   hand-collected prices (data/real/*.jsonl) ──────────────────┴──▶ train_price_model.py ──▶ new ONNX bundle ──▶ hot-reload
   accepted / declined team invites ──▶ teamInvites, team ─────────▶ TeamMatchService (relearns on every request)
   users' thumbs-up/down on advice ──▶ aiAdviceLog ────────────────▶ admin/advice-log (judge prompt / provider changes)
```

* The WebAPI **harvests** the platform's real listings every 24 h (`AiHelper:Learning:HarvestIntervalHours`) into
  `aiMarketObservations`: PH rates, farm daily rates, per-species prices, package prices. Listings are *asking* prices with
  imperfectly-known currency, so they are down-weighted (0.25-0.5) and range-filtered; schema defaults (R850 farm rate, R2,800 PH rate)
  are skipped as "never set by the user".
* `retrain.py` downloads them, retrains, and hot-reloads the model. **Real weight grows with the data:**
  `real_share = min(0.80, n_real / (n_real + 200))` - 50% at 200 real rows, never above 80%.
* The uncertainty band is recalibrated on held-out rows (split-conformal style); once >= 30 real rows exist for a kind, the band
  comes from the **real** residuals, so it honestly widens if the priors were wrong.
* The trainer prints `prior_error_on_real`: how wrong the bootstrap was on your real rows. That tells you which assumptions to distrust.

```powershell
# one-time setup (Python 3.12; onnx/protobuf are pinned - see requirements.txt)
py -3.12 -m venv C:\Users\<you>\.venvs\mikpunt-ai
C:\Users\<you>\.venvs\mikpunt-ai\Scripts\pip install -r tools\ai\requirements.txt

# get an Admin JWT (24 h): sign in as an admin user
curl -k -X POST https://localhost:7038/api/user/signin -H "Content-Type: application/json" -d "{\"email\":\"...\",\"password\":\"...\"}"
#   -> copy accessToken

# retrain from the platform's real data and install it (WebAPI must be running)
$env:MIKPUNT_ADMIN_TOKEN = "<accessToken>"
C:\Users\<you>\.venvs\mikpunt-ai\Scripts\python tools\ai\retrain.py --api https://localhost:7038 --insecure --promote
```

Schedule that weekly.
* **Local dev:** `--promote` copies the bundle into `src/WebAPI/WebAPI/AiModels/package_price/` and hot-reloads the API you are talking to.
* **Deployed server:** `--upload` sends it to the running API's persistent model volume (see "Deployment" below). No rebuild, no redeploy.
* Bootstrap-only retrain (no server): `python train_price_model.py --promote`.

### Adding real data by hand (the fastest way to make the model better)

Type in real prices you can source - other outfitters' published lists, quotes you received, farm invoices - as JSONL and pass them
with `--extra`. One object per line, same schema as the model input (see `common.py`):

```json
{"kind": "SPECIES_FEE", "raw": {"species": "kudu", "tier": "trophy", "province": "limpopo", "market": "domestic"}, "target_usd": 1100, "source": "manual"}
{"kind": "FARM_DAILY_RATE", "raw": {"market": "domestic", "guide_included": false, "catering": true, "lodge_rooms": 4}, "target_usd": 62, "source": "manual"}
```
`target_usd` is always USD (ZAR / the exchange rate). A few dozen well-sourced **domestic** rows will do more than anything else,
because that is the segment that is currently assumed.

## The words: a hosted LLM API (nothing is self-hosted)

The advice text comes from an LLM API reached with a key. Set two values (user-secrets in development, `.env.prod` on the VM):

```
AiHelper__Llm__Provider=Gemini          # Gemini | Groq | OpenRouter | Mistral | GitHub | Anthropic | Custom | None
AiHelper__Llm__ApiKey=<key>
AiHelper__Llm__Fallback__Provider=Groq  # optional: used when the first provider is rate-limited or down
AiHelper__Llm__Fallback__ApiKey=<key>
```

| Provider | Free? | Default model (override with `AiHelper__Llm__Model`) | Key |
|---|---|---|---|
| **Gemini** (Google AI Studio) | free tier, no card | `gemini-flash-latest` | https://aistudio.google.com/apikey |
| **Groq** | free tier, no card | `openai/gpt-oss-120b` | https://console.groq.com/keys |
| **OpenRouter** | `:free` models, small daily cap | `meta-llama/llama-3.3-70b-instruct:free` | https://openrouter.ai/keys |
| **Mistral** | free Experiment plan | `mistral-small-latest` | https://console.mistral.ai/api-keys |
| **GitHub** (GitHub Models) | free, low limits | `openai/gpt-4.1-mini` | a GitHub PAT with the *models* permission |
| **Anthropic** (Claude) | paid | `claude-opus-5-5` (cheaper: `claude-haiku-5-5`) | https://platform.claude.com/settings/keys |
| **Custom** | - | set `AiHelper__Llm__BaseUrl` + `AiHelper__Llm__Model` | any OpenAI-compatible API |

How it is called (`Services/Ai/LlmClients.cs`): every provider except Anthropic uses the OpenAI chat-completions format. Where the
provider supports it the answer is schema-constrained (`response_format: json_schema`); otherwise, or when a model rejects that with a
400, it falls back to JSON mode with the schema in the prompt. Claude uses structured outputs (`output_config.format`, a strict copy of
the schema), no temperature, `effort: low`, and the server-side refusal fallback on the models that support it. Whatever comes back is
validated again by `ParseNarrative` and the "never invent a price" rule stays in the system prompt.

Free tiers have rate limits and may use your prompts to improve their models - that is why the AI Helper sends the LLM no names,
contact details or bios (team suggestions are anonymised as K1, K2, ...), and the page tells users not to type personal details into a
description. If no key is set, the provider is down, or every provider is rate-limited, the price band and the team ranking are still
returned; only the written advice is missing.

**Which provider writes the best Afrikaans?** Measure it on the production prompts:
```powershell
$env:MIKPUNT_ADMIN_TOKEN = "<Admin JWT>"; $env:GEMINI_API_KEY = "..."; $env:GROQ_API_KEY = "..."
python tools\ai\llm\compare_providers.py --api https://localhost:7038 --insecure --providers Gemini Groq --count 12
```
It reports schema validity, the quality gates (no invented numbers, Afrikaans, no duplicate tips), whether the headline states the
right price verdict, latency and errors per provider/model. Users' thumbs-up/down is exported with `GET api/aihelper/admin/advice-log`
to judge prompt changes against real feedback.

## Smart Teaming (how the match score learns)

`POST api/aihelper/team-matches` ranks partners: a PJ gets PH / VG / Boer candidates, a PH / VG / Boer gets outfitters. AP users get
the exclusion notice instead. Four 0-1 components per candidate come from real platform data (`Database/AiTeam_db.cs`):

| Component | Prior weight | From |
|---|---|---|
| proximity | 35% | shared province = 1, neighbouring = 0.6, far = 0.15, unknown = 0.5 (farm addresses, package farms, VG regions, PH bookings, team PJs) |
| credentials | 25% | rating shrunk towards 3.5 stars (5 virtual ratings) + years of experience |
| rate | 20% | PJ: day rate vs the PJ's budget. Others: the PJ's completed-hunt volume |
| history | 20% | hunts the pair already worked together, completed hunts |

The weights are the plan's prior. `LogisticModel` (in `TeamMatchService.cs`) trains on every request from the platform's own outcomes
- accepted invites and existing teams are positives, declined invites negatives - once there are at least 10 labels with 3 of each.
Its acceptance probability takes a share of the score that grows with the evidence, `n / (n + 50)`, capped at 80% (the same idea as
the price model's real-data share). The page shows which mode is active and on how many outcomes it learned.

## Deployment (GitLab -> Docker on the VM)

The pipeline (`.gitlab-ci.yml`) builds `src/WebAPI/Dockerfile.dockerfile` on every push to `main` / `main_dev` and runs
`docker compose -f docker-compose.prod.yml up -d` on the VM. Both AI layers must survive that:

**Price model.** Two places can hold a bundle; the one with the **newer training timestamp** is used:

| Place | What it is | When it changes |
|---|---|---|
| `/app/AiModels/package_price` | baked into the image (whatever is committed under `src/WebAPI/WebAPI/AiModels/`) | every deploy |
| `/data/ai-models/current` | a persistent volume (`AiHelper__ModelOverrideDirectory`, set in the Dockerfile) | `retrain.py --upload` |

So a retrained model survives restarts and redeploys, and a redeploy that ships a *newer* baked model is never beaten by an old volume.
Every bundle is integrity-checked on load: the trainer's golden vectors must reproduce through the real ONNX models, otherwise it is
skipped (a corrupt or wrongly-trained bundle can never serve prices) and the other bundle is used. Uploads additionally accept only
the known flat file names (no paths, no unknown files, size caps).

```powershell
# retrain on the deployed platform's real data and install it on the running server (Admin token from the deployed WebAPI):
python toolsi
etrain.py --api http://<vm>:8081 --token <ADMIN JWT> --upload
curl http://<vm>:8081/api/aihelper/status -H "Authorization: Bearer <JWT>"      # "source":"volume" -> the retrained model is active
```
Also `--promote` + commit if you want future images to ship the retrained model as their baked-in default.

The Dockerfile declares the volume, so persistence works even if the VM's `docker-compose.prod.yml` (not in this repo) is never edited
(Compose re-attaches it when it recreates the container). For a durable, named location add to the VM compose file, as in
`docker-compose.prod-local.yml`:
```yaml
services:
  webapi:
    volumes:
      - ai_models:/data/ai-models
volumes:
  ai_models:
```
`docker compose down -v`, or deleting the volume, throws the retrained model away - the image's baked-in model is then used again.

**LLM.** Nothing to host: set `AiHelper__Llm__Provider` and `AiHelper__Llm__ApiKey` (and optionally the `Fallback__` pair) in the
VM's `.env.prod`. The container only needs outbound HTTPS to the provider. `AiHelper__Llm__Provider=None` gives numbers only.

If the LLM is unreachable the AI Helper still returns the price band; the advice panel says the writing service is unavailable.

## Files

| File | Purpose |
|---|---|
| `common.py` | Feature contract (mirrored in C# `FeatureEncoder.cs`; pinned by `AiFeatureParityTests`) |
| `generate_synthetic.py` | Bootstrap data from published prices (`[ANCHORED]` / `[ASSUMED]` tags) |
| `train_price_model.py` | Train + ONNX export + sanity checks + golden vectors (`--real`, `--promote`) |
| `retrain.py` | Harvest -> download -> retrain -> `--upload` (deployed server) / `--promote` (repo + local reload) |
| `data/species_catalog.json`, `data/market_facts.json` | The published anchors, with source URLs |
| `llm/compare_providers.py` | Same production prompts through several LLM providers, scored by the quality gates |
| `llm/advice_common.py` | Admin API client, realistic random requests, advice quality gates |
| `out/` | Generated artefacts (git-ignored) |
