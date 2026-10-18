#!/usr/bin/env python3
"""Pre-flight the MikPunt 13-minute staging demo and print one check per segment."""

from __future__ import annotations

import argparse
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from demo_common import (  # type: ignore
        ApiClient,
        Database,
        DemoConfig,
        DemoError,
        MARKER_DOMAIN,
        add_common_arguments,
        asset_report,
        fail,
        load_cast,
        print_check,
        require_schema,
    )
else:
    from .demo_common import (
        ApiClient,
        Database,
        DemoConfig,
        DemoError,
        MARKER_DOMAIN,
        add_common_arguments,
        asset_report,
        fail,
        load_cast,
        print_check,
        require_schema,
    )


@dataclass
class Result:
    ok: bool
    label: str
    detail: str = ""


class Checks:
    def __init__(self, db: Database):
        self.db = db
        self.results: list[Result] = []

    def check(self, label: str, sql: str, params: tuple[Any, ...] = (), *, minimum: int = 1, detail: str = "") -> None:
        value = int(self.db.scalar(sql, params, 0) or 0)
        self.results.append(Result(value >= minimum, label, detail or f"found {value}, need {minimum}"))

    def predicate(self, label: str, fn: Callable[[], tuple[bool, str]]) -> None:
        ok, detail = fn()
        self.results.append(Result(ok, label, detail))

    def segment(self, name: str) -> None:
        self.results.append(Result(True, f"--- {name} ---"))


def _registry_id(db: Database, entity_type: str, key: str) -> int | None:
    value = db.scalar("SELECT entityId FROM dbo.demoSeedRegistry WHERE entityType=? AND entityKey=?", (entity_type, key))
    return None if value is None else int(value)


def run_database_checks(db: Database) -> list[Result]:
    c = Checks(db)
    domain = f"%@{MARKER_DOMAIN}"

    c.segment("1:25 Registrasie en dokumente")
    c.check("45 geverifieerde demo-rekeninge", "SELECT COUNT(*) FROM dbo.users WHERE LOWER(uEmail) LIKE ? AND uIsEmailVerified=1", (domain,), minimum=45)
    c.check("Pieter, Thabo en Vusi se vereiste dokumente is goedgekeur", """
        SELECT COUNT(DISTINCT u.uEmail) FROM dbo.users u
        JOIN dbo.userRoles ur ON ur.urUserId=u.userId
        JOIN dbo.roles r ON r.roleId=ur.urRoleId
        WHERE LOWER(u.uEmail) IN (?,?,?) AND NOT EXISTS(
            SELECT 1 FROM dbo.roleDocumentRequirements req
            WHERE req.roleCode=r.roleCode AND req.isRequired=1 AND NOT EXISTS(
                SELECT 1 FROM dbo.userDocumentation d WHERE d.udUserRole=ur.userRoleId AND d.udDtId=req.documentTypeId AND d.udIsDeleted=0 AND d.udStatus='Approved'))
        """, (f"pieter.nel@{MARKER_DOMAIN}", f"thabo.mokoena@{MARKER_DOMAIN}", f"vusi.dlamini@{MARKER_DOMAIN}"), minimum=3)
    c.check("Thabo het 'n afgekeurde-en-vervangde dokumentgeskiedenis", """
        SELECT COUNT(*) FROM dbo.documentAuditLog l
        JOIN dbo.userRoles ur ON ur.userRoleId=l.userRoleId JOIN dbo.users u ON u.userId=ur.urUserId
        WHERE LOWER(u.uEmail)=? AND l.actionType='Rejected'
        """, (f"thabo.mokoena@{MARKER_DOMAIN}",))
    c.check("Lerato se dokumente wag in die keuringsry", """
        SELECT COUNT(*) FROM dbo.userDocumentation d JOIN dbo.userRoles ur ON ur.userRoleId=d.udUserRole
        JOIN dbo.users u ON u.userId=ur.urUserId WHERE LOWER(u.uEmail)=? AND d.udIsDeleted=0 AND d.udStatus='Pending'
        """, (f"lerato.khumalo@{MARKER_DOMAIN}",))

    c.segment("2:30 Plaas, pakket en advertensie")
    c.check("Kameeldoring Wildplaas is aktief", "SELECT COUNT(*) FROM dbo.farm f JOIN dbo.farmer fa ON fa.farmerId=f.farmFarmerId JOIN dbo.userRoles ur ON ur.userRoleId=fa.fUserRoleId JOIN dbo.users u ON u.userId=ur.urUserId WHERE f.farmName='Kameeldoring Wildplaas' AND f.farmStatus='Active' AND LOWER(u.uEmail) LIKE ?", (domain,))
    c.check("Kameeldoring het die sewe beplande wildspesies", "SELECT COUNT(*) FROM dbo.availableAnimals a JOIN dbo.farm f ON f.farmId=a.aaFarmId WHERE f.farmName='Kameeldoring Wildplaas' AND a.aaName IN(N'Koedoe',N'Rooibok',N'Blouwildebees',N'Vlakvark',N'Gemsbok',N'Waterbok',N'Eland')", minimum=7)
    c.check("Pieter se Waterberg-pakket is aktief", "SELECT COUNT(*) FROM dbo.packages WHERE pTitle='Waterberg Koedoe-safari, 4 dae' AND pStatus='Active' AND pStockPrice=48500")
    c.check("Lerato se pakkette is versteek", "SELECT COUNT(*) FROM dbo.packages p JOIN dbo.pj ON pj.pjId=p.pPjId JOIN dbo.userRoles ur ON ur.userRoleId=pj.pjUserRoleId JOIN dbo.users u ON u.userId=ur.urUserId WHERE LOWER(u.uEmail)=? AND p.pStatus='Hidden'", (f"lerato.khumalo@{MARKER_DOMAIN}",), minimum=2)
    c.check("Karoo Optics het 'n aktiewe betaalde advertensie", "SELECT COUNT(*) FROM dbo.advertisements a JOIN dbo.ap ON ap.apId=a.adApId JOIN dbo.userRoles ur ON ur.userRoleId=ap.apUserRoleId JOIN dbo.users u ON u.userId=ur.urUserId JOIN dbo.adPayments pay ON pay.adId=a.advertisementId WHERE LOWER(u.uEmail)=? AND a.adStatus='Aktief'", (f"bemarking@{MARKER_DOMAIN}",))
    c.check("Karoo Optics het 'n onbetaalde advertensie", "SELECT COUNT(*) FROM dbo.advertisements a JOIN dbo.ap ON ap.apId=a.adApId JOIN dbo.userRoles ur ON ur.userRoleId=ap.apUserRoleId JOIN dbo.users u ON u.userId=ur.urUserId WHERE LOWER(u.uEmail)=? AND a.adStatus='Pending Payment' AND NOT EXISTS(SELECT 1 FROM dbo.adPayments p WHERE p.adId=a.advertisementId)", (f"bemarking@{MARKER_DOMAIN}",))

    c.segment("3:35 Soek, bespreek en betaal")
    c.check("Minstens 12 aktiewe demo-pakkette", "SELECT COUNT(*) FROM dbo.packages p JOIN dbo.pj ON pj.pjId=p.pPjId JOIN dbo.userRoles ur ON ur.userRoleId=pj.pjUserRoleId JOIN dbo.users u ON u.userId=ur.urUserId WHERE LOWER(u.uEmail) LIKE ? AND p.pStatus='Active'", (domain,), minimum=12)
    c.check("Die lewendige besprekingdatums is oop", """
        SELECT CASE WHEN NOT EXISTS(
          SELECT 1 FROM dbo.bookings b JOIN dbo.userRoles ur ON ur.userRoleId=b.bookHunterId JOIN dbo.users u ON u.userId=ur.urUserId
          JOIN dbo.packages p ON p.packageId=b.bookPackageId
          WHERE LOWER(u.uEmail)=? AND p.pTitle='Waterberg Koedoe-safari, 4 dae'
            AND b.bookStartDate=DATEADD(day,35,CAST((SELECT demoDate FROM dbo.demoSeedRegistry WHERE entityType='booking' AND entityKey='b_team') AS date))
            AND b.bookStatus<>'Cancelled') THEN 1 ELSE 0 END
        """, (f"johan.vanwyk@{MARKER_DOMAIN}",))

    c.segment("5:00 Span en klets")
    b_team = _registry_id(db, "booking", "b_team") or -1
    c.check("B-Team is betaal", "SELECT COUNT(*) FROM dbo.bookings WHERE bookingId=? AND bookStatus='Paid' AND bookPaymentStatus='Paid'", (b_team,))
    c.check("B-Team se PH en VG het aanvaar", "SELECT COUNT(*) FROM dbo.bookingStaffAssignments WHERE bookingId=? AND status='Accepted' AND staffRole IN('PH','VG')", (b_team,), minimum=2)
    c.check("Mia is as jagmaat gekoppel", "SELECT COUNT(*) FROM dbo.bookingCompanions c JOIN dbo.userRoles ur ON ur.userRoleId=c.userRoleId JOIN dbo.users u ON u.userId=ur.urUserId WHERE c.bookingId=? AND LOWER(u.uEmail)=? AND c.status='Linked'", (b_team, f"mia.botha@{MARKER_DOMAIN}"))
    c.check("Die Waterberg-groepklets het minstens 15 boodskappe", "SELECT COUNT(*) FROM dbo.chatMessages m JOIN dbo.chat c ON c.chatId=m.cmChatId WHERE c.cTitle='Waterberg jagspan'", minimum=15)
    c.check("Twee kletsboodskappe wag vir moderering", "SELECT COUNT(*) FROM dbo.flaggedChatMessages f JOIN dbo.users u ON u.userId=f.fcmSenderId WHERE LOWER(u.uEmail) LIKE ? AND f.fcmStatus='PendingReview'", (domain,), minimum=2)
    c.check("Hangende spanuitnodiging wys ekspedisienaam en terme", "SELECT COUNT(*) FROM dbo.teamInvites WHERE inviteStatus='Pending' AND NULLIF(LTRIM(RTRIM(inviteExpeditionName)),'') IS NOT NULL AND NULLIF(LTRIM(RTRIM(inviteTerms)),'') IS NOT NULL")

    c.segment("6:20 Permitte en SAPS 520")
    c.check("Johan het twee beursiepermitte", "SELECT COUNT(*) FROM dbo.permits p JOIN dbo.users u ON u.userId=p.UserId WHERE LOWER(u.uEmail)=? AND p.PermitNumber LIKE 'DEMO-%'", (f"johan.vanwyk@{MARKER_DOMAIN}",), minimum=2)
    c.check("Johan het een goedgekeurde en een hangende SAPS 520", "SELECT COUNT(DISTINCT d.udStatus) FROM dbo.userDocumentation d JOIN dbo.userRoles ur ON ur.userRoleId=d.udUserRole JOIN dbo.users u ON u.userId=ur.urUserId JOIN dbo.documentType t ON t.dtId=d.udDtId WHERE LOWER(u.uEmail)=? AND t.dtNumber LIKE 'SAPS520-DEMO-%' AND d.udStatus IN('Approved','Pending')", (f"johan.vanwyk@{MARKER_DOMAIN}",), minimum=2)

    c.segment("7:15 Jag, media en storie")
    b_hunt = _registry_id(db, "booking", "b_hunt") or -1
    c.check("B-Hunt is voltooi", "SELECT COUNT(*) FROM dbo.bookings WHERE bookingId=? AND bookStatus='Completed' AND bookPaymentStatus='Paid'", (b_hunt,))
    c.check("B-Hunt het 'n ongeveer 7 km / 4 uur roete", "SELECT COUNT(*) FROM dbo.bookingRoutes WHERE bookingId=? AND distanceMeters BETWEEN 6500 AND 7500 AND durationSeconds BETWEEN 13800 AND 15000", (b_hunt,))
    c.predicate("B-Hunt het drie skootpenne", lambda: _pin_check(db, b_hunt))
    c.check("B-Hunt het 8 jagterfoto's", "SELECT COUNT(*) FROM dbo.bookingMedia WHERE bookingId=? AND uploaderRole='Hunter' AND deletedAt IS NULL", (b_hunt,), minimum=8)
    c.check("B-Hunt het 4 PH-foto's", "SELECT COUNT(*) FROM dbo.bookingMedia WHERE bookingId=? AND uploaderRole='PH' AND deletedAt IS NULL", (b_hunt,), minimum=4)
    c.check(
        "B-Hunt het 2 VG-videogrepe",
        "SELECT COUNT(*) FROM dbo.bookingMedia WHERE bookingId=? AND uploaderRole='VG' AND deletedAt IS NULL AND (sourceType='Video' OR contentType LIKE 'video/%')",
        (b_hunt,),
        minimum=2,
        detail="Plaas 2 VG-knipsels onder tools/demo/assets/hunt/vg en herseed om VG-videogrepe by te voeg",
    )
    c.check("B-Hunt se voorafgerenderde recap is gereed", "SELECT COUNT(*) FROM dbo.bookingRecaps WHERE bookingId=? AND status='Completed' AND progress=100 AND durationSeconds BETWEEN 20 AND 25 AND outputFileSize>0", (b_hunt,))

    c.segment("9:05 Trofee en gradering")
    c.check("B-Hunt het 'n goedgekeurde trofee-indiening", "SELECT COUNT(*) FROM dbo.trophySubmissions WHERE bookingId=? AND status='Approved' AND approvedTrophyId IS NOT NULL", (b_hunt,))
    c.check("B-Hunt het 'n regstelling-versoek", "SELECT COUNT(*) FROM dbo.trophySubmissions WHERE bookingId=? AND status='CorrectionRequested'", (b_hunt,))
    c.check("Johan se Koedoe is publiek en Rowland Ward-gekoppel", "SELECT COUNT(*) FROM dbo.trophies t JOIN dbo.users u ON u.userId=t.tOwnerUserId WHERE LOWER(u.uEmail)=? AND t.tSpecies='Koedoe' AND t.tIsPublic=1 AND t.tRowlandWardClassId IS NOT NULL", (f"johan.vanwyk@{MARKER_DOMAIN}",))
    c.check("Johan se graderings vir Thabo en Vusi is oop", """
        SELECT CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.ratings r JOIN dbo.users ratee ON ratee.userId=r.rateeUserId WHERE r.bookingId=? AND r.raterUserId=(SELECT userId FROM dbo.users WHERE LOWER(uEmail)=?) AND LOWER(ratee.uEmail) IN (?,?)) THEN 1 ELSE 0 END
        """, (b_hunt, f"johan.vanwyk@{MARKER_DOMAIN}", f"thabo.mokoena@{MARKER_DOMAIN}", f"vusi.dlamini@{MARKER_DOMAIN}"))

    c.segment("10:10 Administrasie en agtergrondvolume")
    c.check("Een graderingkommentaar wag vir moderering", "SELECT COUNT(*) FROM dbo.flaggedRatingComments f JOIN dbo.users u ON u.userId=f.frcRaterUserId WHERE LOWER(u.uEmail) LIKE ? AND f.frcStatus='PendingReview'", (domain,))
    c.check("Een oop graderingdispuut bestaan", "SELECT COUNT(*) FROM dbo.ratingDisputes d JOIN dbo.users u ON u.userId=d.disputedByUserId WHERE LOWER(u.uEmail) LIKE ? AND d.status='PendingReview'", (domain,))
    c.check("Twee demo-rekeninge is opgeskort", "SELECT COUNT(*) FROM dbo.users WHERE LOWER(uEmail) LIKE ? AND (uIsSuspended=1 OR uAccountStatus='Suspended')", (domain,), minimum=2)
    c.check("Agt demo-plase", "SELECT COUNT(*) FROM dbo.farm f JOIN dbo.farmer fa ON fa.farmerId=f.farmFarmerId JOIN dbo.userRoles ur ON ur.userRoleId=fa.fUserRoleId JOIN dbo.users u ON u.userId=ur.urUserId WHERE LOWER(u.uEmail) LIKE ?", (domain,), minimum=8)
    c.check("45 demo-besprekings", "SELECT COUNT(*) FROM dbo.bookings b JOIN dbo.userRoles ur ON ur.userRoleId=b.bookHunterId JOIN dbo.users u ON u.userId=ur.urUserId WHERE LOWER(u.uEmail) LIKE ?", (domain,), minimum=45)
    c.check("Ongeveer 70 graderings", "SELECT COUNT(*) FROM dbo.ratings r JOIN dbo.users u ON u.userId=r.raterUserId WHERE LOWER(u.uEmail) LIKE ?", (domain,), minimum=68)
    c.check("20 demo-trofees", "SELECT COUNT(*) FROM dbo.trophies t JOIN dbo.users u ON u.userId=t.tOwnerUserId WHERE LOWER(u.uEmail) LIKE ?", (domain,), minimum=20)
    c.check("12 openbare trofees", "SELECT COUNT(*) FROM dbo.trophies t JOIN dbo.users u ON u.userId=t.tOwnerUserId WHERE LOWER(u.uEmail) LIKE ? AND t.tIsPublic=1 AND t.tApprovalStatus='Approved'", (domain,), minimum=12)
    c.check("120 demo-kennisgewings", "SELECT COUNT(*) FROM dbo.userNotifications n JOIN dbo.users u ON u.userId=n.recipientUserId WHERE LOWER(u.uEmail) LIKE ? AND n.notificationType='DemoAktiwiteit'", (domain,), minimum=120)
    c.check("Agt demo-advertensies", "SELECT COUNT(*) FROM dbo.advertisements a JOIN dbo.ap ON ap.apId=a.adApId JOIN dbo.userRoles ur ON ur.userRoleId=ap.apUserRoleId JOIN dbo.users u ON u.userId=ur.urUserId WHERE LOWER(u.uEmail) LIKE ?", (domain,), minimum=8)

    # Pictures and package detail: every farm, trophy and advert has its own picture.
    c.check("Elke demo-plaas het eie foto's (8 verskillende stelle)", "SELECT COUNT(DISTINCT CHECKSUM(f.farmImagesJson)) FROM dbo.farm f JOIN dbo.farmer fa ON fa.farmerId=f.farmFarmerId JOIN dbo.userRoles ur ON ur.userRoleId=fa.fUserRoleId JOIN dbo.users u ON u.userId=ur.urUserId WHERE LOWER(u.uEmail) LIKE ? AND f.farmImagesJson LIKE '[[]\"data:image/%'", (domain,), minimum=8)
    c.check("Elke demo-trofee het 'n opgelaaide foto", "SELECT COUNT(DISTINCT t.tPhotoPath) FROM dbo.trophies t JOIN dbo.users u ON u.userId=t.tOwnerUserId WHERE LOWER(u.uEmail) LIKE ? AND t.tPhotoPath LIKE '/uploads/trophies/%'", (domain,), minimum=20)
    c.check("Elke demo-advertensie het 'n prent", "SELECT COUNT(DISTINCT CHECKSUM(a.adImagesJson)) FROM dbo.advertisements a JOIN dbo.ap ON ap.apId=a.adApId JOIN dbo.userRoles ur ON ur.userRoleId=ap.apUserRoleId JOIN dbo.users u ON u.userId=ur.urUserId WHERE LOWER(u.uEmail) LIKE ? AND a.adImagesJson LIKE '[[]\"data:image/%'", (domain,), minimum=8)
    c.check("Demo-pakkette het 'n volledige beskrywing en ingesluit/nie ingesluit nie", "SELECT COUNT(*) FROM dbo.packages p JOIN dbo.pj ON pj.pjId=p.pPjId JOIN dbo.userRoles ur ON ur.userRoleId=pj.pjUserRoleId JOIN dbo.users u ON u.userId=ur.urUserId WHERE LOWER(u.uEmail) LIKE ? AND LEN(p.pDescription) >= 100 AND p.pInclusions IS NOT NULL AND p.pExclusions IS NOT NULL AND p.pIncludedItems LIKE '%OBS_PRICE:%'", (domain,), minimum=16)
    return c.results


def _pin_check(db: Database, booking_id: int) -> tuple[bool, str]:
    raw = db.scalar("SELECT TOP 1 pinsJson FROM dbo.bookingRoutes WHERE bookingId=? ORDER BY submittedAt DESC", (booking_id,))
    if not raw:
        return False, "geen roete-penne gevind nie"
    import json
    try:
        pins = json.loads(str(raw))
    except json.JSONDecodeError:
        return False, "pinsJson is ongeldig"
    count = sum(1 for pin in pins if str(pin.get("type", "")).lower() == "shot")
    return count == 3, f"{count} skootpen(ne)"


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    add_common_arguments(parser)
    parser.add_argument("--assets", action="store_true", help="Check only the local asset manifest and files.")
    parser.add_argument("--api", action="store_true", help="Also check WebAPI and MobileAPI reachability.")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        load_cast(args.cast)
        results: list[Result] = []
        for ok, label in asset_report():
            results.append(Result(ok, f"Bate: {label}"))
        if not args.assets:
            config = DemoConfig.from_env()
            with Database(config.sql_connection_string) as db:
                require_schema(db)
                if not db.table_exists("demoSeedRegistry"):
                    raise DemoError("demoSeedRegistry is missing. Run seed_demo.py first.")
                results.extend(run_database_checks(db))
            if args.api:
                web = ApiClient(config.web_api_url, allow_insecure_tls=config.allow_insecure_tls, timeout=config.request_timeout_seconds)
                mobile = ApiClient(config.mobile_api_url, allow_insecure_tls=config.allow_insecure_tls, timeout=config.request_timeout_seconds)
                for label, client, path in (("WebAPI", web, "/health"), ("MobileAPI", mobile, "/health")):
                    try:
                        client.request("GET", path, expected=(200,))
                        results.append(Result(True, f"{label} is bereikbaar"))
                    except DemoError as exc:
                        results.append(Result(False, f"{label} is bereikbaar", str(exc)))
        failures = 0
        for result in results:
            if result.label.startswith("---"):
                print(f"\n{result.label}")
                continue
            print_check(result.ok, result.label, result.detail)
            failures += 0 if result.ok else 1
        print(f"\nPre-flight: {len(results) - failures}/{len(results)} checks passed; {failures} failed.")
        return 0 if failures == 0 else 1
    except DemoError as exc:
        return fail(str(exc))


if __name__ == "__main__":
    raise SystemExit(main())
