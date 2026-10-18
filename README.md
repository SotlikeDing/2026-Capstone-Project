# MikPunt

**Span 2 (Kode-X) · Akademia Capstone 2026**

MikPunt is an integrated hunter and farm-management platform for the South African
hunting industry. It connects farmers, hunters, professional hunters, equipment
suppliers and videographers in one system: farms and packages, bookings and payments,
real-time chat, team building, ratings, document verification, SAPS 520 firearm-permit
generation, and a mobile trophy room with Rowland Ward evaluation and a public
leaderboard.

The user interface is in Afrikaans; code, comments and this README are in English.

## Architecture

```
                 ┌────────────────────┐        ┌────────────────────┐
 Browser ──────▶ │ WebApp (Blazor)    │ ─────▶ │ WebAPI (.NET 8)    │ ──┐
                 └────────────────────┘  HTTP  │ REST + SignalR chat│   │
                                                └────────────────────┘   ▼
                 ┌────────────────────┐        ┌────────────────────┐  ┌─────────────┐
 Phone ────────▶ │ jagbestuur_app     │ ─────▶ │ MobileAPI (.NET 8) │─▶│ SQL Server  │
                 │ (Flutter)          │  HTTP  │ REST               │  │ (Mikpunt)   │
                 └────────────────────┘        └────────────────────┘  └─────────────┘
```

| Component | Path | Stack |
|---|---|---|
| **WebAPI** | `src/WebAPI/WebAPI` | ASP.NET Core 8, raw ADO.NET (`*_db.cs` classes), JWT, SignalR, rate limiting |
| **WebApp** | `src/WebApp` | Blazor Server (interactive), QuestPDF for SAPS 520 PDFs |
| **MobileAPI** | `src/MobileAPI/MobileAPI` | ASP.NET Core 8, `Microsoft.Data.SqlClient`, JWT access + refresh tokens |
| **jagbestuur_app** | `src/jagbestuur_app` | Flutter / Dart (Android, iOS, desktop and web targets) |
| **Database** | `db/` | SQL Server; `Complete_create.sql` plus idempotent `Add*.sql` migrations |

The WebApp talks to the WebAPI only; the Flutter app talks to the MobileAPI only. Both
APIs share one database.

### Roles

| Code | Role (Afrikaans) | Meaning |
|---|---|---|
| `HUNTER` | Jagter | Hunter |
| `PH` | Professionele Jagter | Professional hunter |
| `PJ` | Toerusting Verskaffer | Equipment supplier |
| `FARMER` | Boer / Grondeienaar | Farmer / landowner |
| `VG` | Videograaf | Videographer |
| `AP` | Advertensie Plaser | Advertiser |
| `ADMIN` | Administrateur | Platform administrator |

### Authorization (WebAPI)

- Every controller is `[Authorize]` by default; only genuinely public reads (browse/search,
  public profile, sign-in/register/password reset) are marked `[AllowAnonymous]`.
- Anything keyed by a userId, userRoleId or resource id must check ownership: use the
  helpers in `Controllers/CallerIdentity.cs` (`IsSelfOrAdmin`, `IsOwnerOrAdminAsync`,
  `OwnsUserRoleOrAdminAsync`) with the lookups in `Database/ResourceOwnership_db.cs`.
- The JWT carries the userId (`NameIdentifier`) and role codes only - resolve role-row ids
  (pjId, farmerId, userRoleId) from the database, never trust them from the request.
- The Admin role can only be granted by an existing Admin.
- The WebApp attaches the JWT to every API call (`Services/AuthTokenHandler.cs`) and to the
  chat hub connection.

## Repository layout

```
src/
  WebAPI/            WebAPI project (WebAPI/) and solution
  WebAPI.Tests/      xUnit tests for the WebAPI (unit + integration)
  WebApp/            Blazor Server front end
  MobileAPI/         MobileAPI project (MobileAPI/) and its tests (MobileAPI.Tests/)
  jagbestuur_app/    Flutter mobile app
db/                  SQL schema, migrations, seed and maintenance scripts
Plans/               Design plans for upcoming features
Dokumentasie/        Meeting minutes (Notules) and checklists (Sakelyste)
tools/               Helper scripts (PowerShell, Python, JS)
MikPunt.slnx         Solution covering WebAPI, WebApp and MobileAPI
docker-compose.*.yml Local dev / prod-like container setups
.gitlab-ci.yml       Build, push and deploy pipeline
```

## Prerequisites

- **.NET SDK 8.0.423** (pinned in `global.json`, `rollForward: latestFeature`)
- **SQL Server**: LocalDB (`(localdb)\Mikpunt`, the default in `appsettings.json`) or
  any SQL Server instance you point the connection string at
- **Flutter** with Dart `^3.11.5` (only for the mobile app)
- **Docker Desktop** (only for the container setups)

## Configuration and secrets

Never commit real secrets. `appsettings.json` ships with local-development placeholders
only; set real values with [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
(or environment variables in Docker).

**WebAPI** (`src/WebAPI/WebAPI`):

```bash
dotnet user-secrets set "Jwt:Key" "<random string, 32+ characters>"
dotnet user-secrets set "EmailSettings:SenderEmail" "your_address@gmail.com"
dotnet user-secrets set "EmailSettings:SenderPassword" "<16-character Gmail App Password>"
```

The WebAPI refuses to start without `Jwt:Key`. Email is used for verification and
password-reset mails; create the App Password under Google Account → Security
(2-Step Verification must be on).

**MobileAPI** (`src/MobileAPI/MobileAPI`):

```bash
dotnet user-secrets set "Jwt:SecretKey" "<random string, 32+ characters>"
```

Generate a key with `python -c "import secrets; print(secrets.token_urlsafe(48))"`.

### Environment variables (Docker)

Copy `.env.example` to `.env.dev` (dev compose) or `.env.prod` (prod compose) and fill
it in. `.env.*` is git-ignored; only `.env.example` is tracked.

| Variable | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | SQL Server connection string. Keep `MultipleActiveResultSets=True`. Inside a container use `host.docker.internal,1433` to reach a SQL Server on the host. |
| `Jwt__Key` | WebAPI token signing key |
| `Jwt__SecretKey` | MobileAPI token signing key |
| `EmailSettings__SenderEmail` / `__SenderPassword` | Gmail SMTP credentials |
| `WebAppBaseUrl` | Public WebApp URL, used in links inside emails |
| `ApiBaseUrl` | WebAPI URL as seen by the WebApp (`http://webapi:80` between containers) |
| `ASPNETCORE_ENVIRONMENT` | `Development` / `Production` |

## Database

The WebAPI provisions the database itself on startup (`Services/DatabaseBootstrapper.cs`):
if `dbo.users` does not exist it runs `db/Complete_create.sql`, then applies every
migration listed in `WebAPI.csproj` in order. The scripts are embedded in the assembly
and are idempotent, so re-running them on an up-to-date database is a no-op.

- Start the **WebAPI first** on a fresh database. The MobileAPI does not create schema.
- Only genuinely idempotent schema scripts are embedded. Seed and cleanup scripts
  (`ToetsData.sql`, `CleanDB.sql`, `DeleteUser.sql`, `SeedChats.sql`, `seed_users.sql`, ...)
  are **manual** utilities and are never run automatically.
- New migration? Add `db/AddXyz.sql` and register it in **both** `WebAPI.csproj`
  (`EmbeddedResource`) and `DatabaseBootstrapper.MigrationScriptOrder`.

## Running locally

Start the three .NET projects (separate terminals, from the repo root):

```bash
dotnet run --project src/WebAPI/WebAPI --launch-profile https
dotnet run --project src/WebApp --launch-profile https
dotnet run --project src/MobileAPI/MobileAPI --launch-profile https
```

| Service | HTTPS | HTTP |
|---|---|---|
| WebAPI | https://localhost:7038 | http://localhost:5004 |
| WebApp | https://localhost:7285 | http://localhost:5147 |
| MobileAPI | https://localhost:7096 | http://localhost:5133 |

Swagger is available on both APIs in Development. The WebApp expects the WebAPI at
`ApiBaseUrl` (`https://localhost:7038/` by default), and the WebAPI's `WebAppBaseUrl`
is set for the ports above. Alternatively open `MikPunt.slnx` in Visual Studio / Rider
and start all three.

### Mobile app

```bash
cd src/jagbestuur_app
flutter pub get
flutter run --dart-define=API_BASE_URL=http://localhost:5133 \
            --dart-define=WEB_APP_BASE_URL=http://localhost:5147
```

`API_BASE_URL` is the MobileAPI; `WEB_APP_BASE_URL` is the WebApp used for the web
login/register hand-off (`jagbestuur://auth-callback`). Without the flags the app
defaults to `http://localhost:5133` and `http://localhost:5180`. On an Android emulator
use `10.0.2.2` instead of `localhost`.

### Docker

```bash
cp .env.example .env.dev        # then fill in the values
docker compose -f docker-compose.dev-local.yml up --build
```

| Service | Dev port | Prod-like port |
|---|---|---|
| WebApp | 9080 | 8080 |
| WebAPI | 9081 | 8081 |
| MobileAPI | 9082 | 8082 |

`docker-compose.prod-local.yml` pulls the pipeline-built images from the GitLab
registry and reads `.env.prod`. Neither compose file starts a database; supply one via
`ConnectionStrings__DefaultConnection`.

## AI Helper (price and package optimisation)

`/ai-helper` (WebApp) has two tabs. The **packet maker** lets PJ, PH, VG, Boer and AP users load an existing package / farm /
ad (or start fresh), check it against the market and get concrete Afrikaans advice, then open the improved offer in the normal editor
to review and save (PH day rates are saved directly). **Smart Teaming** ranks partners (PJ -> PH/VG/Boer, PH/VG/Boer -> PJ) with an
invite button; AP is excluded. A trained price model (ONNX, runs in the WebAPI) produces the numbers; a hosted LLM API (Gemini,
Groq, ... or Claude, reached with an API key) writes advice grounded in them and never produces a price. Real listings, invite
outcomes and thumbs-up feedback feed the learning loops. **How it is trained, which free API keys work, and how to retrain:
[`tools/ai/README.md`](tools/ai/README.md).**

```bash
dotnet user-secrets set "AiHelper:Llm:Provider" "Gemini" --project src/WebAPI/WebAPI
dotnet user-secrets set "AiHelper:Llm:ApiKey" "<key from https://aistudio.google.com/apikey>" --project src/WebAPI/WebAPI
```
Endpoints: `POST api/aihelper/optimize`, `POST api/aihelper/team-matches`, `POST api/aihelper/feedback`, `GET api/aihelper/status`,
plus Admin-only `admin/harvest`, `admin/observations`, `admin/advice-log`, `admin/dry-run`, `admin/reload-models`, `admin/upload-model`.

## Testing

```bash
dotnet test src/WebAPI.Tests/WebAPI.Tests.csproj
dotnet test src/MobileAPI/MobileAPI.Tests/MobileAPI.Tests.csproj

cd src/jagbestuur_app && flutter analyze && flutter test
```

Some WebAPI integration tests (`WebApplicationFactory`) insert throwaway rows into the
database from `appsettings.json` (LocalDB by default). Classes that need a database
skip themselves when none is reachable, so the suite still runs without SQL Server.

## CI/CD

`.gitlab-ci.yml` builds a Docker image for each service, pushes them to the GitLab
container registry, and deploys with `docker compose -f docker-compose.prod.yml up -d`
on the VM. It runs on `main` and `main_dev` only. Each developer works on their own
`main_dev_<name>` branch.

## Further reading

- `Plans/multi_hunter_bookings_plan.md`: main hunter plus linked companions on one booking
- `Plans/gunshot_detection_implementation_plan.md`: on-device gunshot detection that auto-pins the map
- `Plans/ai_helper_optimization_and_teaming_plan.md`: AI package optimisation and smart team matching
- `Dokumentasie/`: meeting minutes and checklists
