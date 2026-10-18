#!/usr/bin/env python3
"""Restore the mutable MikPunt demo objects before a rehearsal."""

from __future__ import annotations

import argparse
import datetime as dt
import sys
from pathlib import Path

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from demo_common import (  # type: ignore
        ApiClient,
        ASSETS_DIR,
        Database,
        DemoConfig,
        DemoError,
        MARKER_DOMAIN,
        add_common_arguments,
        fail,
        all_accounts,
        load_cast,
        require_schema,
        role_ids_for_user,
        user_ids,
    )
    from seed_demo import _mobile_token, _seed_media_and_recap_via_api  # type: ignore
else:
    from .demo_common import (
        ApiClient,
        ASSETS_DIR,
        Database,
        DemoConfig,
        DemoError,
        MARKER_DOMAIN,
        add_common_arguments,
        fail,
        all_accounts,
        load_cast,
        require_schema,
        role_ids_for_user,
        user_ids,
    )
    from .seed_demo import _mobile_token, _seed_media_and_recap_via_api


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    add_common_arguments(parser, mutating=True)
    parser.add_argument(
        "--skip-media",
        action="store_true",
        help="Only reset the database rows; leave the B-Hunt photos, clips and story as they are.",
    )
    return parser


def _included_media(db: Database, booking_id: int) -> list[int]:
    return [
        int(row[0])
        for row in db.rows(
            "SELECT mediaId FROM dbo.bookingMedia WHERE bookingId=? AND deletedAt IS NULL "
            "AND inclusionStatus='Included' ORDER BY mediaId",
            (booking_id,),
        )
    ]


def reset_media(db: Database, config: DemoConfig, cast_path: Path, demo_date: dt.date) -> dict[str, int]:
    """Put the B-Hunt media back to the seeded set and make sure its story matches it.

    Rehearsal uploads are soft-deleted (the MobileAPI hides deletedAt rows), missing
    seeded files are uploaded again, the five story photos are included again and a
    fresh story is rendered whenever the included set had to change.
    """
    booking_id = _registry_id(db, "booking", "b_hunt")
    seeded = sorted(
        path.name
        for pattern in ("hunt/hunter/*.jpg", "hunt/ph/*.jpg", "hunt/vg/*.mp4")
        for path in ASSETS_DIR.glob(pattern)
    )
    if not seeded:
        print("Media reset skipped: tools/demo/assets/hunt has no photos or clips.")
        return {"media_rehearsal_uploads_hidden": 0, "media_story_rerendered": 0}
    placeholders = ",".join("?" for _ in seeded)
    removed = db.execute(
        f"UPDATE dbo.bookingMedia SET deletedAt=SYSUTCDATETIME() WHERE bookingId=? AND deletedAt IS NULL "
        f"AND originalFileName NOT IN ({placeholders})",
        (booking_id, *seeded),
    ).rowcount
    db.commit()
    before = _included_media(db, booking_id)

    accounts = all_accounts(load_cast(cast_path))
    ids = user_ids(db)
    roles = {
        account["key"]: role_ids_for_user(db, ids[account["email"].lower()])
        for account in accounts
        if account["email"].lower() in ids
    }
    api = ApiClient(config.mobile_api_url, allow_insecure_tls=config.allow_insecure_tls, timeout=config.request_timeout_seconds)
    bookings = {"b_hunt": booking_id}
    _seed_media_and_recap_via_api(db, api, config, accounts, roles, bookings, demo_date)

    rerendered = 0
    if removed or _included_media(db, booking_id) != before:
        # The existing story was rendered from other photos; render one from the restored set.
        johan = next(account for account in accounts if account["key"] == "johan")
        token = _mobile_token(api, johan, roles["johan"]["HUNTER"], config.password)
        api.post(
            f"/api/bookings/{booking_id}/recaps",
            {"title": "Johan se Waterberg-jag"},
            token=token,
            expected=(202,),
        )
        _seed_media_and_recap_via_api(db, api, config, accounts, roles, bookings, demo_date)
        rerendered = 1
    return {"media_rehearsal_uploads_hidden": removed, "media_story_rerendered": rerendered}


def _registry_id(db: Database, entity_type: str, key: str) -> int:
    value = db.scalar(
        "SELECT entityId FROM dbo.demoSeedRegistry WHERE entityType=? AND entityKey=?",
        (entity_type, key),
    )
    if value is None:
        raise DemoError(f"Demo registry entry missing: {entity_type}/{key}. Run seed_demo.py first.")
    return int(value)


def _assert_demo_owned(db: Database, user_id: int, label: str) -> None:
    email = db.scalar("SELECT LOWER(uEmail) FROM dbo.users WHERE userId=?", (user_id,))
    if not email or not str(email).endswith("@" + MARKER_DOMAIN):
        raise DemoError(f"Safety check failed: {label} is not owned by a @{MARKER_DOMAIN} account.")


def _assert_demo_entity(db: Database, sql: str, entity_id: int, label: str) -> None:
    email = db.scalar(sql, (entity_id,))
    if not email or not str(email).lower().endswith("@" + MARKER_DOMAIN):
        raise DemoError(f"Safety check failed: {label} is not owned by a @{MARKER_DOMAIN} account.")


def reset(db: Database, demo_date: dt.date) -> dict[str, int]:
    package_id = _registry_id(db, "package", "package_01")
    hunt_booking = _registry_id(db, "booking", "b_hunt")
    farm_id = _registry_id(db, "farm", "kameeldoring")
    correction_submission = _registry_id(db, "submission", "b_hunt_correction")

    _assert_demo_entity(
        db,
        "SELECT LOWER(u.uEmail) FROM dbo.packages p JOIN dbo.pj ON pj.pjId=p.pPjId JOIN dbo.userRoles ur ON ur.userRoleId=pj.pjUserRoleId JOIN dbo.users u ON u.userId=ur.urUserId WHERE p.packageId=?",
        package_id,
        "demo package",
    )
    _assert_demo_entity(
        db,
        "SELECT LOWER(u.uEmail) FROM dbo.bookings b JOIN dbo.userRoles ur ON ur.userRoleId=b.bookHunterId JOIN dbo.users u ON u.userId=ur.urUserId WHERE b.bookingId=?",
        hunt_booking,
        "B-Hunt",
    )
    _assert_demo_entity(
        db,
        "SELECT LOWER(u.uEmail) FROM dbo.farm f JOIN dbo.farmer fa ON fa.farmerId=f.farmFarmerId JOIN dbo.userRoles ur ON ur.userRoleId=fa.fUserRoleId JOIN dbo.users u ON u.userId=ur.urUserId WHERE f.farmId=?",
        farm_id,
        "Kameeldoring",
    )
    _assert_demo_entity(
        db,
        "SELECT LOWER(u.uEmail) FROM dbo.trophySubmissions s JOIN dbo.users u ON u.userId=s.hunterUserId WHERE s.submissionId=?",
        correction_submission,
        "correction submission",
    )

    johan = db.row("SELECT userId FROM dbo.users WHERE LOWER(uEmail)=?", (f"johan.vanwyk@{MARKER_DOMAIN}",))
    thabo = db.row("SELECT userId FROM dbo.users WHERE LOWER(uEmail)=?", (f"thabo.mokoena@{MARKER_DOMAIN}",))
    vusi = db.row("SELECT userId FROM dbo.users WHERE LOWER(uEmail)=?", (f"vusi.dlamini@{MARKER_DOMAIN}",))
    lerato_role = db.scalar(
        """
        SELECT ur.userRoleId FROM dbo.users u
        JOIN dbo.userRoles ur ON ur.urUserId=u.userId
        JOIN dbo.roles r ON r.roleId=ur.urRoleId
        WHERE LOWER(u.uEmail)=? AND UPPER(r.roleCode)='PJ'
        """,
        (f"lerato.khumalo@{MARKER_DOMAIN}",),
    )
    if not johan or not thabo or not vusi or lerato_role is None:
        raise DemoError("One or more core cast accounts are missing. Run seed_demo.py first.")
    johan_id, thabo_id, vusi_id = int(johan[0]), int(thabo[0]), int(vusi[0])
    for user_id, label in ((johan_id, "Johan"), (thabo_id, "Thabo"), (vusi_id, "Vusi")):
        _assert_demo_owned(db, user_id, label)

    results: dict[str, int] = {}
    live_start = demo_date + dt.timedelta(days=35)
    live_end = demo_date + dt.timedelta(days=38)
    results["cancelled_live_bookings"] = max(
        0,
        db.execute(
            """
            UPDATE b SET bookStatus='Cancelled',bookPaymentStatus='Cancelled'
            FROM dbo.bookings b
            JOIN dbo.userRoles ur ON ur.userRoleId=b.bookHunterId
            WHERE ur.urUserId=? AND b.bookPackageId=?
              AND b.bookStartDate=? AND b.bookEndDate=? AND b.bookStatus<>'Cancelled'
            """,
            (johan_id, package_id, live_start, live_end),
        ).rowcount,
    )
    # Ratings are append-audited and ratingHistory has an FK to ratings, so a
    # rehearsal's ratings cannot safely be deleted. Preserve them on an identical
    # frozen booking instead. The registered B-Hunt keeps its route/media/recap,
    # including private storage paths whose booking-id prefix must not change.
    live_rating_count = int(
        db.scalar(
            "SELECT COUNT(*) FROM dbo.ratings WHERE bookingId=? AND raterUserId=? AND rateeUserId IN (?,?)",
            (hunt_booking, johan_id, thabo_id, vusi_id),
            0,
        )
        or 0
    )
    results["archived_rating_copies"] = 0
    if live_rating_count:
        frozen_hunt = int(
            db.execute(
                """
                INSERT dbo.bookings(bookHunterId,bookPackageId,bookStartDate,bookEndDate,bookStatus,
                    bookHuntGuests,bookNonHuntGuests,bookPhId,bookVgId,bookPaymentStatus)
                OUTPUT INSERTED.bookingId
                SELECT bookHunterId,bookPackageId,bookStartDate,bookEndDate,'Archived',
                    bookHuntGuests,bookNonHuntGuests,bookPhId,bookVgId,'Paid'
                FROM dbo.bookings WHERE bookingId=?
                """,
                (hunt_booking,),
            ).fetchone()[0]
        )
        db.execute(
            "UPDATE dbo.ratings SET bookingId=? WHERE bookingId=? AND raterUserId=? AND rateeUserId IN (?,?)",
            (frozen_hunt, hunt_booking, johan_id, thabo_id, vusi_id),
        )
        results["archived_rating_copies"] = 1

    results["trophy_corrections"] = max(
        0,
        db.execute(
            """
            UPDATE dbo.trophySubmissions
            SET status='CorrectionRequested',reviewNotes='Laai asseblief ''n duideliker syfoto met die maatband sigbaar.',
                approvedTrophyId=NULL,updatedAt=SYSUTCDATETIME()
            WHERE submissionId=? AND bookingId=?
            """,
            (correction_submission, hunt_booking),
        ).rowcount,
    )
    results["live_ratings_open"] = 1 if int(db.scalar(
        "SELECT COUNT(*) FROM dbo.ratings WHERE bookingId=? AND raterUserId=? AND rateeUserId IN (?,?)",
        (hunt_booking, johan_id, thabo_id, vusi_id), 0) or 0) == 0 else 0
    results["chat_flags_reopened"] = max(
        0,
        db.execute(
            """
            UPDATE f SET fcmStatus='PendingReview',fcmReviewedBy=NULL,fcmReviewedAt=NULL
            FROM dbo.flaggedChatMessages f
            JOIN dbo.chatMessages m ON m.chatMessageId=f.fcmMessageId
            JOIN dbo.users u ON u.userId=m.cmSenderId
            WHERE LOWER(u.uEmail) LIKE ?
            """,
            (f"%@{MARKER_DOMAIN}",),
        ).rowcount,
    )
    results["rating_flags_reopened"] = max(
        0,
        db.execute(
            """
            UPDATE f SET frcStatus='PendingReview',frcReviewedBy=NULL,frcReviewedAt=NULL
            FROM dbo.flaggedRatingComments f
            JOIN dbo.users u ON u.userId=f.frcRaterUserId
            WHERE LOWER(u.uEmail) LIKE ?
            """,
            (f"%@{MARKER_DOMAIN}",),
        ).rowcount,
    )
    results["disputes_reopened"] = max(
        0,
        db.execute(
            """
            UPDATE d SET status='PendingReview',reviewedBy=NULL,reviewedAt=NULL,resolutionNotes=NULL
            FROM dbo.ratingDisputes d
            JOIN dbo.users u ON u.userId=d.disputedByUserId
            WHERE LOWER(u.uEmail) LIKE ?
            """,
            (f"%@{MARKER_DOMAIN}",),
        ).rowcount,
    )
    results["lerato_documents_pending"] = max(
        0,
        db.execute(
            """
            UPDATE dbo.userDocumentation
            SET udStatus='Pending',udVerifiedBy=NULL,udVerifiedAt=NULL,udRejectionReason=NULL
            WHERE udUserRole=? AND udIsDeleted=0
            """,
            (lerato_role,),
        ).rowcount,
    )
    # Springbok is deliberately absent from Kameeldoring at curtain-up and is the
    # species Anna adds live. This deletion cannot touch another owner's farm.
    results["live_species_removed"] = max(
        0,
        db.execute(
            "DELETE FROM dbo.availableAnimals WHERE aaFarmId=? AND aaName=N'Springbok'",
            (farm_id,),
        ).rowcount,
    )
    results["sessions_revoked"] = max(
        0,
        db.execute(
            """
            DELETE token FROM dbo.AuthTokens token
            JOIN dbo.users u ON u.userId=token.atUserId
            WHERE LOWER(u.uEmail) LIKE ?
            """,
            (f"%@{MARKER_DOMAIN}",),
        ).rowcount,
    )
    return results


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        load_cast(args.cast)
        if args.dry_run:
            print(f"Would reset mutable demo state for {args.demo_date.isoformat()} only.")
            print("Append-only ratingHistory, documentAuditLog and adminAuditLog rows are never deleted.")
            return 0
        config = DemoConfig.from_env()
        with Database(config.sql_connection_string) as db:
            require_schema(db)
            if not db.table_exists("demoSeedRegistry"):
                raise DemoError("demoSeedRegistry is missing. Run seed_demo.py first.")
            results = reset(db, args.demo_date)
            db.commit()
            if not args.skip_media:
                results.update(reset_media(db, config, args.cast, args.demo_date))
        for name, count in results.items():
            print(f"{name}: {count}")
        print("Demo reset completed; append-only audit tables were preserved.")
        return 0
    except DemoError as exc:
        return fail(str(exc))
    except KeyboardInterrupt:
        return fail("Interrupted; the SQL transaction was rolled back.")


if __name__ == "__main__":
    raise SystemExit(main())
