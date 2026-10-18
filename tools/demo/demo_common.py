from __future__ import annotations

import argparse
import datetime as dt
import json
import time
import mimetypes
import os
import re
import ssl
import sys
import uuid
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable, Iterator, Sequence


DEMO_DIR = Path(__file__).resolve().parent
CAST_PATH = DEMO_DIR / "cast.json"
ASSETS_DIR = DEMO_DIR / "assets"
MARKER_DOMAIN = "demo.mikpunt.co.za"


class DemoError(RuntimeError):
    """An actionable demo-tooling failure."""


def env_first(*names: str, default: str = "") -> str:
    for name in names:
        value = os.getenv(name)
        if value:
            return value.strip()
    return default


def env_bool(name: str, default: bool = False) -> bool:
    value = os.getenv(name)
    if value is None:
        return default
    return value.strip().lower() in {"1", "true", "yes", "on"}


def parse_date(value: str) -> dt.date:
    try:
        return dt.date.fromisoformat(value)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("use YYYY-MM-DD") from exc


def add_common_arguments(parser: argparse.ArgumentParser, *, mutating: bool = False) -> None:
    parser.add_argument(
        "--demo-date",
        type=parse_date,
        default=dt.date.today(),
        help="Anchor date for all relative dates (default: today).",
    )
    parser.add_argument(
        "--cast",
        type=Path,
        default=CAST_PATH,
        help="Cast manifest (default: tools/demo/cast.json).",
    )
    if mutating:
        parser.add_argument(
            "--dry-run",
            action="store_true",
            help="Validate configuration and print the intended work without writing.",
        )


@dataclass(frozen=True)
class DemoConfig:
    web_api_url: str
    mobile_api_url: str
    password: str
    sql_connection_string: str
    admin_email: str = ""
    admin_password: str = ""
    allow_insecure_tls: bool = False
    request_timeout_seconds: int = 30

    @classmethod
    def from_env(cls, *, require_password: bool = False) -> "DemoConfig":
        password = os.getenv("DEMO_PASSWORD", "")
        if require_password and not password:
            raise DemoError(
                "DEMO_PASSWORD is required. Use a 12+ character password with upper/lower case, "
                "a digit and a special character; do not commit it."
            )
        return cls(
            web_api_url=env_first(
                "E2E_WEB_API_URL", "E2E_API_URL", default="https://localhost:7038"
            ).rstrip("/"),
            mobile_api_url=env_first(
                "E2E_MOBILE_API_URL", default="https://localhost:7096"
            ).rstrip("/"),
            password=password,
            sql_connection_string=sql_connection_string_from_env(),
            admin_email=env_first("E2E_ADMIN_EMAIL", "E2E_ADMIN_USER"),
            admin_password=env_first("E2E_ADMIN_PASSWORD"),
            allow_insecure_tls=env_bool("E2E_ALLOW_INSECURE_TLS", False),
            request_timeout_seconds=int(os.getenv("E2E_HTTP_TIMEOUT", "30")),
        )


def sql_connection_string_from_env() -> str:
    explicit = env_first("E2E_SQL_CONNECTION_STRING", "ConnectionStrings__DefaultConnection")
    if explicit:
        return explicit

    server = env_first("E2E_SQL_SERVER", default=r"(localdb)\Mikpunt")
    database = env_first("E2E_SQL_DATABASE", default="Mikpunt")
    driver = env_first("E2E_SQL_DRIVER", default="ODBC Driver 18 for SQL Server")
    user = env_first("E2E_SQL_USER", "E2E_SQL_USERNAME")
    password = env_first("E2E_SQL_PASSWORD")
    parts = [f"DRIVER={{{driver}}}", f"SERVER={server}", f"DATABASE={database}"]
    if user:
        parts.extend([f"UID={user}", f"PWD={password}"])
    else:
        parts.append("Trusted_Connection=yes")
    parts.extend(["TrustServerCertificate=yes", "Encrypt=no"])
    return ";".join(parts) + ";"


def load_cast(path: Path = CAST_PATH) -> dict[str, Any]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise DemoError(f"Cannot read cast manifest {path}: {exc}") from exc
    if data.get("marker_domain") != MARKER_DOMAIN:
        raise DemoError(
            f"Refusing cast manifest with marker_domain={data.get('marker_domain')!r}; "
            f"expected {MARKER_DOMAIN!r}."
        )
    people = data.get("people")
    if not isinstance(people, list) or not people:
        raise DemoError("cast.json must contain a non-empty people array.")
    seen: set[str] = set()
    for person in people:
        email = str(person.get("email", "")).lower()
        if not email.endswith("@" + MARKER_DOMAIN):
            raise DemoError(f"Non-demo email in cast manifest: {email!r}")
        if email in seen:
            raise DemoError(f"Duplicate cast email: {email}")
        seen.add(email)
    return data


def slug(value: str) -> str:
    value = value.lower().replace("é", "e").replace("ë", "e")
    return re.sub(r"[^a-z0-9]+", ".", value).strip(".")


def all_accounts(cast: dict[str, Any]) -> list[dict[str, Any]]:
    """Return 45 deterministic fictional accounts with the role counts in the plan.

    Three background people hold two roles so the requested role totals (48) fit
    into approximately 45 users without creating disposable-looking identities.
    """

    accounts = [dict(person) for person in cast["people"]]
    names = [
        ("Elmar", "du Plessis"), ("Nadia", "Jacobs"), ("Sibusiso", "Nkosi"),
        ("Carla", "van der Merwe"), ("Heinrich", "Meyer"), ("Anika", "Roux"),
        ("Kagiso", "Molefe"), ("Ruan", "Smit"), ("Leandi", "Kruger"),
        ("Bongani", "Zulu"), ("Marli", "Steyn"), ("Willem", "Coetzee"),
        ("Nokuthula", "Ndlovu"), ("Andre", "Fourie"), ("Tasneem", "Davids"),
        ("Jaco", "Venter"), ("Boitumelo", "Seabi"), ("Renier", "Bothma"),
        ("Elsabe", "Prinsloo"), ("Mandla", "Mthembu"), ("Karla", "Olivier"),
        ("Francois", "Naude"), ("Zanele", "Sithole"), ("Gerhard", "Visser"),
        ("Nomsa", "Mabena"), ("Dewald", "Theron"), ("Ayesha", "Khan"),
        ("Lwazi", "Cele"), ("Martie", "de Klerk"), ("Sipho", "Maseko"),
        ("Rika", "Grobler"), ("Themba", "Hadebe"), ("Ilze", "Louw"),
        ("Petrus", "Jordaan"), ("Ayanda", "Mkhize"), ("Henco", "Basson"),
        ("Naledi", "Mokoena")
    ]
    background: list[dict[str, Any]] = []
    foreign = ["Duitsland", "Verenigde State", "Denemarke", "Spanje"]
    for index, (first, surname) in enumerate(names):
        person = {
            "key": f"background_{index + 1:02d}",
            "first_name": first,
            "surname": surname,
            "email": f"{slug(first)}.{slug(surname)}@{MARKER_DOMAIN}",
            "phone": f"07155{index + 20:04d}",
            "roles": [],
        }
        if index < 23:
            person.update(
                roles=["HUNTER"],
                country=foreign[index] if index < 4 else "Suid-Afrika",
                username=f"{first}{2026 + index}",
                bio="Buitelewe-entoesias met respek vir etiese en volhoubare jag.",
            )
        background.append(person)

    # Remaining role slots, with three deliberate overlaps.
    background[0]["roles"].append("PJ")
    background[23]["roles"] = ["PJ"]
    background[24]["roles"] = ["PJ", "PH"]
    for index in range(25, 29):
        background[index]["roles"] = ["PH"]
    background[25]["roles"].append("VG")
    background[29]["roles"] = ["VG"]
    background[30]["roles"] = ["VG"]
    for index in range(31, 35):
        background[index]["roles"] = ["FARMER"]
    background[35]["roles"] = ["AP"]
    background[36]["roles"] = ["AP"]

    for index, person in enumerate(background):
        person.setdefault("business_name", f"{person['surname']} Wilddienste")
        person.setdefault("business_registration", f"2020/{610000 + index:06d}/07")
        person.setdefault("years_experience", 4 + index % 12)
        person.setdefault("bio", "Ervare Suid-Afrikaanse jag- en ekotoerismepraktisyn.")
    accounts.extend(background)

    if len(accounts) != 45:
        raise AssertionError(f"Expected 45 accounts, generated {len(accounts)}")
    return accounts


def account_by_key(accounts: Sequence[dict[str, Any]], key: str) -> dict[str, Any]:
    return next(person for person in accounts if person["key"] == key)


def role_registration_code(code: str) -> str:
    return {"HUNTER": "Jagter", "FARMER": "Boer"}.get(code.upper(), code.upper())


def registration_payload(person: dict[str, Any], password: str) -> dict[str, Any]:
    roles = [role_registration_code(role) for role in person["roles"]]
    return {
        "first_name": person["first_name"],
        "surname": person["surname"],
        "email": person["email"],
        "password": password,
        "phone_country_code": "+27",
        "phone": person["phone"],
        "primary_role_code": roles[0],
        "selected_roles": roles,
        "leaderboard_nickname": person.get("username", person["first_name"]),
        "passport_or_id_number": f"DEMO-{slug(person['key']).upper()}-INVALID",
        "pj_license_number": f"PJ-DEMO-{person['key']}",
        "pj_license_expiry": "2027-12-31",
        "pj_province_documents": "Limpopo",
        "cipc_reg_number": person.get("business_registration", "2026/000000/07"),
        "tax_vat_number": "DEMO-NIE-GELDIG-NIE",
        "physical_address": "Fiktiewe demo-adres, Suid-Afrika",
        "ph_license_number": f"PH-DEMO-{person['key']}",
        "ph_issuing_province": "Limpopo",
        "ph_license_expiry": "2027-12-31",
        "dangerous_game_endorsement": "Ja" if person.get("dangerous_game_daily_rate") else "Nee",
        "ph_association_name": "Fiktiewe Demo Vereniging",
        "ph_association_member_number": f"DEMO-{person['key']}",
        "ph_association_document": "Word deur die demo-dokumentvloei opgelaai",
        "first_aid_cert_details": "Vlak 2",
        "first_aid_expiry": "2027-12-31",
        "firearm_competency_details": "Demo-verwysing; nie 'n regte lisensie nie",
        "drone_licenses": person.get("drone_licence", "") if "VG" in person["roles"] else "",
        "media_qualifications": "Wildlewe-videografie",
        "farm_name": person.get("business_name", f"{person['surname']} Wildplaas"),
        "farm_region": "Limpopo",
        "farm_reg_number": person.get("business_registration", "DEMO"),
        "business_name": person.get("business_name", f"{person['surname']} Wilddienste"),
        "terms_accepted": True,
    }


class ApiClient:
    def __init__(self, base_url: str, *, allow_insecure_tls: bool = False, timeout: int = 30):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        self.ssl_context = None
        if allow_insecure_tls:
            self.ssl_context = ssl._create_unverified_context()  # noqa: SLF001 - explicit staging opt-in

    def request(
        self,
        method: str,
        path: str,
        *,
        body: Any | None = None,
        token: str = "",
        expected: Iterable[int] = (200,),
        _attempt: int = 0,
    ) -> Any:
        data = None
        headers = {"Accept": "application/json"}
        if body is not None:
            data = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json"
        if token:
            headers["Authorization"] = f"Bearer {token}"
        request = urllib.request.Request(
            f"{self.base_url}/{path.lstrip('/')}", data=data, headers=headers, method=method
        )
        try:
            with urllib.request.urlopen(
                request, timeout=self.timeout, context=self.ssl_context
            ) as response:
                raw = response.read()
                if response.status not in set(expected):
                    raise DemoError(f"{method} {path} returned HTTP {response.status}")
                if not raw:
                    return None
                content_type = response.headers.get("Content-Type", "")
                return json.loads(raw) if "json" in content_type else raw
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            if exc.code in set(expected):
                return json.loads(detail) if detail else None
            # The APIs rate-limit sign-in, registration and admin calls per IP/user: wait as told and retry.
            if exc.code == 429 and _attempt < 8:
                wait = exc.headers.get("Retry-After")
                time.sleep(min(int(wait) if wait and wait.isdigit() else 15, 70))
                return self.request(method, path, body=body, token=token, expected=expected, _attempt=_attempt + 1)
            raise DemoError(f"{method} {path} failed (HTTP {exc.code}): {detail[:800]}") from exc
        except urllib.error.URLError as exc:
            raise DemoError(f"Cannot reach {self.base_url}: {exc.reason}") from exc

    def post(self, path: str, body: Any, *, token: str = "", expected: Iterable[int] = (200,)) -> Any:
        return self.request("POST", path, body=body, token=token, expected=expected)

    def multipart(
        self,
        path: str,
        *,
        fields: dict[str, Any],
        file_field: str,
        file_path: Path,
        token: str,
        expected: Iterable[int] = (200, 201),
    ) -> Any:
        """Upload one file without adding a requests dependency."""

        boundary = f"----mikpunt-demo-{uuid.uuid4().hex}"
        body = bytearray()

        def line(value: str = "") -> None:
            body.extend(value.encode("utf-8"))
            body.extend(b"\r\n")

        for name, value in fields.items():
            if value is None:
                continue
            line(f"--{boundary}")
            line(f'Content-Disposition: form-data; name="{name}"')
            line()
            line(str(value))
        line(f"--{boundary}")
        safe_name = file_path.name.replace('"', "")
        line(f'Content-Disposition: form-data; name="{file_field}"; filename="{safe_name}"')
        line(f"Content-Type: {mimetypes.guess_type(file_path.name)[0] or 'application/octet-stream'}")
        line()
        body.extend(file_path.read_bytes())
        body.extend(b"\r\n")
        line(f"--{boundary}--")

        headers = {
            "Accept": "application/json",
            "Authorization": f"Bearer {token}",
            "Content-Type": f"multipart/form-data; boundary={boundary}",
        }
        request = urllib.request.Request(
            f"{self.base_url}/{path.lstrip('/')}", data=bytes(body), headers=headers, method="POST"
        )
        accepted = set(expected)
        try:
            with urllib.request.urlopen(
                request, timeout=self.timeout, context=self.ssl_context
            ) as response:
                raw = response.read()
                if response.status not in accepted:
                    raise DemoError(f"POST {path} returned HTTP {response.status}")
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            if exc.code in accepted:
                return json.loads(detail) if detail else None
            raise DemoError(f"POST {path} failed (HTTP {exc.code}): {detail[:800]}") from exc
        except (OSError, urllib.error.URLError) as exc:
            raise DemoError(f"Cannot upload {file_path} to {self.base_url}: {exc}") from exc


@dataclass
class Database:
    connection_string: str
    connection: Any = field(init=False, default=None)

    def __enter__(self) -> "Database":
        try:
            import pyodbc  # type: ignore
        except ImportError as exc:
            raise DemoError(
                "pyodbc is required. Install tools/demo/requirements.txt in your demo environment."
            ) from exc
        try:
            self.connection = pyodbc.connect(self.connection_string, autocommit=False, timeout=15)
        except Exception as exc:
            raise DemoError(f"Could not connect to SQL Server: {exc}") from exc
        return self

    def __exit__(self, exc_type: Any, exc: Any, traceback: Any) -> None:
        if self.connection is not None:
            if exc_type is not None:
                self.connection.rollback()
            self.connection.close()

    def execute(self, sql: str, params: Sequence[Any] = ()) -> Any:
        return self.connection.execute(sql, *params)

    def executemany(self, sql: str, rows: Iterable[Sequence[Any]]) -> Any:
        cursor = self.connection.cursor()
        cursor.fast_executemany = True
        return cursor.executemany(sql, list(rows))

    def scalar(self, sql: str, params: Sequence[Any] = (), default: Any = None) -> Any:
        row = self.execute(sql, params).fetchone()
        return default if row is None else row[0]

    def row(self, sql: str, params: Sequence[Any] = ()) -> Any:
        return self.execute(sql, params).fetchone()

    def rows(self, sql: str, params: Sequence[Any] = ()) -> list[Any]:
        return list(self.execute(sql, params).fetchall())

    def commit(self) -> None:
        self.connection.commit()

    def rollback(self) -> None:
        self.connection.rollback()

    def table_exists(self, table: str) -> bool:
        return bool(self.scalar("SELECT CASE WHEN OBJECT_ID(?, 'U') IS NULL THEN 0 ELSE 1 END", (f"dbo.{table}",)))

    def column_exists(self, table: str, column: str) -> bool:
        return bool(self.scalar("SELECT CASE WHEN COL_LENGTH(?, ?) IS NULL THEN 0 ELSE 1 END", (f"dbo.{table}", column)))

    def identity(self, sql: str, params: Sequence[Any] = ()) -> int:
        row = self.execute(sql + "; SELECT CAST(SCOPE_IDENTITY() AS int);", params).fetchone()
        if row is None:
            raise DemoError("Expected an identity value but SQL Server returned none.")
        return int(row[0])


REQUIRED_TABLES = {
    "users", "roles", "userRoles", "hunter", "pj", "ph", "vg", "farmer", "ap",
    "addresses", "farm", "farmLodge", "availableAnimals", "packages", "bookings",
    "ratings", "ratingHistory", "farmRatings", "chat", "chatParticipants", "chatMessages",
    "advertisements", "adPayments", "userDocumentation", "roleDocumentRequirements",
    "documentAuditLog", "documentType",
    "bookingStaffAssignments", "bookingCompanions", "bookingMedia", "bookingRoutes",
    "bookingRouteSelections", "bookingRecaps", "trophySubmissions", "trophies",
    "ratingDisputes", "flaggedChatMessages", "flaggedRatingComments",
    "team", "teamInvites", "vgPackages",
}

REQUIRED_COLUMNS = {
    "users": ("uAccountStatus", "uIsSuspended", "uIsEmailVerified"),
    "userDocumentation": ("udVerifiedBy", "udVerifiedAt", "udRejectionReason"),
    "documentAuditLog": ("notes",),
    "teamInvites": ("inviteExpeditionName", "inviteTerms"),
    "vgPackages": ("vgpTitle", "vgpRegionCovered", "vgpEquipmentProvided"),
    "bookingMedia": ("sourceType", "mediaVersion", "inclusionStatus"),
}


def require_schema(db: Database) -> None:
    missing = sorted(table for table in REQUIRED_TABLES if not db.table_exists(table))
    if missing:
        raise DemoError(
            "The staging schema is behind the application migrations. Missing tables: "
            + ", ".join(missing)
        )
    missing_columns = [
        f"{table}.{column}"
        for table, columns in REQUIRED_COLUMNS.items()
        for column in columns
        if not db.column_exists(table, column)
    ]
    if missing_columns:
        raise DemoError(
            "The staging schema is behind the application migrations. Missing columns: "
            + ", ".join(missing_columns)
        )


def ensure_runtime_tables(db: Database) -> None:
    db.execute(
        """
        IF OBJECT_ID(N'dbo.userNotifications', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.userNotifications (
                notificationId INT IDENTITY(1,1) PRIMARY KEY,
                recipientUserId INT NOT NULL,
                senderUserId INT NULL,
                notificationType NVARCHAR(50) NOT NULL,
                title NVARCHAR(200) NOT NULL,
                message NVARCHAR(MAX) NOT NULL,
                actionUrl NVARCHAR(500) NULL,
                isRead BIT NOT NULL DEFAULT 0,
                createdTimestamp DATETIME2 NOT NULL DEFAULT GETDATE(),
                readTimestamp DATETIME2 NULL
            );
            CREATE INDEX IX_userNotifications_recipient
                ON dbo.userNotifications(recipientUserId, isRead);
        END;
        IF OBJECT_ID(N'dbo.permits', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.permits (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                UserId INT NOT NULL,
                PermitNumber NVARCHAR(100) NOT NULL,
                PermitType NVARCHAR(100) NOT NULL,
                IssueDate DATETIME NOT NULL,
                ExpiryDate DATETIME NOT NULL,
                DocumentUrl NVARCHAR(500) NOT NULL,
                Province NVARCHAR(50) NOT NULL
            );
        END;
        """
    )


def user_ids(db: Database) -> dict[str, int]:
    return {
        str(row[1]).lower(): int(row[0])
        for row in db.rows(
            "SELECT userId, uEmail FROM dbo.users WHERE LOWER(uEmail) LIKE ?",
            (f"%@{MARKER_DOMAIN}",),
        )
    }


def role_ids_for_user(db: Database, user_id: int) -> dict[str, int]:
    return {
        str(row[1]).upper(): int(row[0])
        for row in db.rows(
            """
            SELECT ur.userRoleId, r.roleCode
            FROM dbo.userRoles ur JOIN dbo.roles r ON r.roleId=ur.urRoleId
            WHERE ur.urUserId=?
            """,
            (user_id,),
        )
    }


def asset_report(asset_dir: Path = ASSETS_DIR) -> list[tuple[bool, str]]:
    manifest_path = asset_dir / "manifest.json"
    if not manifest_path.exists():
        return [(False, f"asset manifest missing: {manifest_path}")]
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    report: list[tuple[bool, str]] = []
    for spec in manifest.get("files", []):
        matches = [path for path in asset_dir.glob(spec["glob"]) if path.is_file()]
        enough = len(matches) >= int(spec["minimum"])
        licensed = bool(str(spec.get("source", "")).strip())
        report.append((enough, f"{spec['purpose']}: {len(matches)}/{spec['minimum']} file(s)"))
        report.append((licensed, f"{spec['purpose']}: licence/source note present"))
    return report


def iso_datetime(value: dt.datetime) -> str:
    return value.replace(microsecond=0).isoformat() + ("Z" if value.tzinfo is None else "")


def route_payload(demo_date: dt.date) -> dict[str, Any]:
    start = dt.datetime.combine(demo_date - dt.timedelta(days=4), dt.time(6, 30))
    points: list[dict[str, Any]] = []
    # A deterministic loop on Kameeldoring. SQL stores the supplied 7.04 km summary.
    offsets = [
        (0.0000, 0.0000), (0.0040, 0.0030), (0.0080, 0.0060), (0.0120, 0.0030),
        (0.0160, -0.0010), (0.0200, -0.0060), (0.0240, -0.0110), (0.0200, -0.0160),
        (0.0160, -0.0200), (0.0110, -0.0230), (0.0060, -0.0200), (0.0010, -0.0160),
        (-0.0040, -0.0110), (-0.0060, -0.0050), (-0.0030, 0.0000), (0.0000, 0.0000),
    ]
    for index, (lat_offset, lon_offset) in enumerate(offsets):
        points.append(
            {
                "latitude": round(-24.4820 + lat_offset, 6),
                "longitude": round(28.4170 + lon_offset, 6),
                "timestamp": iso_datetime(start + dt.timedelta(minutes=16 * index)),
            }
        )
    pins = []
    for number, point_index in enumerate((4, 9, 13), start=1):
        point = points[point_index]
        pins.append(
            {
                "id": f"shot-{number}",
                "type": "Shot",
                "latitude": point["latitude"],
                "longitude": point["longitude"],
                "timestamp": point["timestamp"],
                "note": f"Skootpunt {number}",
            }
        )
    return {
        "sourceHuntSessionId": f"mikpunt-demo-{demo_date.isoformat()}",
        "points": points,
        "pins": pins,
        "distanceMeters": 7040.0,
        "durationSeconds": 4 * 60 * 60,
        "routeVersion": 1,
    }


def print_check(ok: bool, label: str, detail: str = "") -> None:
    symbol = "[OK]" if ok else "[FAIL]"
    suffix = f" - {detail}" if detail else ""
    print(f"{symbol} {label}{suffix}")


def fail(message: str) -> int:
    print(f"ERROR: {message}", file=sys.stderr)
    return 2
