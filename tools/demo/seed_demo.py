#!/usr/bin/env python3
"""Build the repeatable MikPunt staging data set for the 13-minute demo.

Accounts and the scenario's credential files use the public WebAPI. Historical
timestamps and volume data are inserted with parameterised SQL because the product
APIs intentionally do not allow clients to back-date them.
"""

from __future__ import annotations

import argparse
import base64
import datetime as dt
import hashlib
import json
import os
import re
import sys
import time
from itertools import cycle
from pathlib import Path
from typing import Any, Sequence

if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from demo_common import (  # type: ignore
        ApiClient,
        ASSETS_DIR,
        Database,
        DemoConfig,
        DemoError,
        MARKER_DOMAIN,
        account_by_key,
        add_common_arguments,
        all_accounts,
        asset_report,
        ensure_runtime_tables,
        fail,
        load_cast,
        registration_payload,
        require_schema,
        role_ids_for_user,
        route_payload,
        user_ids,
    )
    from demo_content import AD_IMAGES, FARM_DESCRIPTIONS, PACKAGE_DETAILS  # type: ignore
else:
    from .demo_common import (
        ApiClient,
        ASSETS_DIR,
        Database,
        DemoConfig,
        DemoError,
        MARKER_DOMAIN,
        account_by_key,
        add_common_arguments,
        all_accounts,
        asset_report,
        ensure_runtime_tables,
        fail,
        load_cast,
        registration_payload,
        require_schema,
        role_ids_for_user,
        route_payload,
        user_ids,
    )
    from .demo_content import AD_IMAGES, FARM_DESCRIPTIONS, PACKAGE_DETAILS


ROLE_NAMES = {
    "HUNTER": "Jagter",
    "PJ": "Toerusting Verskaffer",
    "PH": "Professionele Jagter",
    "VG": "Videograaf",
    "FARMER": "Boer/Grondeienaar",
    "AP": "Advertensie Plaser",
}

FARMS = [
    ("kameeldoring", "Kameeldoring Wildplaas", "Vaalwater", "Limpopo", -24.4820, 28.4170, 2400, "Bosveld", "Wildheining", 1450, 6, True),
    ("rooibosrand", "Rooibosrand Wildgoed", "Lephalale", "Limpopo", -23.6652, 27.7441, 3100, "Doringbosveld", "Wildheining", 1750, 8, True),
    ("moloporivier", "Moloporivier Jagplaas", "Vryburg", "Noordwes", -26.9601, 24.7284, 5200, "Kalahari-savanne", "Wildheining", 1325, 5, False),
    ("sneeuberg", "Sneeuberg Conservancy", "Graaff-Reinet", "Oos-Kaap", -32.2174, 24.5487, 6800, "Berggrasveld", "Wildheining", 1900, 10, True),
    ("vlaktesig", "Vlaktesig Wildlandgoed", "Bloemfontein", "Vrystaat", -29.3340, 26.4430, 4100, "Oop grasveld", "Jakkalsproef", 1180, 4, False),
    ("kameelpan", "Kameelpan Kalahari", "Upington", "Noord-Kaap", -28.1804, 21.2368, 8900, "Kalahari-duine", "Wildheining", 1550, 7, True),
    ("umfolozi", "Umfolozi Heuwels", "Hluhluwe", "KwaZulu-Natal", -28.0178, 32.2671, 3600, "Doringveld", "Wildheining", 2100, 9, True),
    ("komatibos", "Komatibos Safaris", "Komatipoort", "Mpumalanga", -25.4275, 31.9550, 4700, "Laeveld", "Wildheining", 1850, 8, True),
]

SPECIES = [
    ("Koedoe", 12, 28500), ("Rooibok", 34, 6200), ("Blouwildebees", 18, 14800),
    ("Vlakvark", 22, 4800), ("Gemsbok", 14, 17200), ("Waterbok", 9, 23800),
    ("Eland", 6, 39500), ("Blesbok", 26, 7200), ("Njala", 8, 26500),
    ("Sebra", 10, 14500), ("Swartwildebees", 11, 13900), ("Bosbok", 7, 17800),
]

TROPHY_SPECIES = ["Koedoe", "Gemsbok", "Rooibok", "Blouwildebees", "Blesbok", "Waterbok", "Njala", "Sebra"]

PACKAGES = [
    ("Waterberg Koedoe-safari, 4 dae", 48500, 4, "kameeldoring", "pieter", "Active"),
    ("Waterberg Familie-vlaktewild, 5 dae", 62500, 5, "kameeldoring", "pieter", "Active"),
    ("Rooibok en vlakvark naweek", 18000, 2, "rooibosrand", "background_01", "Active"),
    ("Bosveld gemengde sak, 6 dae", 88000, 6, "rooibosrand", "background_24", "Active"),
    ("Molopo boogjag, 5 dae", 54000, 5, "moloporivier", "background_25", "Active"),
    ("Kalahari gemsbok-ekspedisie", 71500, 6, "kameelpan", "pieter", "Active"),
    ("Sneeuberg koedoe en springbok", 68000, 5, "sneeuberg", "background_01", "Active"),
    ("Vrystaat blesbok-uitdunning", 24500, 3, "vlaktesig", "background_24", "Active"),
    ("Umfolozi njala-ervaring", 96500, 6, "umfolozi", "background_25", "Active"),
    ("Komatibos Laeveld-safari", 79000, 5, "komatibos", "pieter", "Active"),
    ("Kalahari familiepakket", 42000, 4, "kameelpan", "background_01", "Active"),
    ("Buffel-safari met gevaarlike-wild PH", 185000, 7, "komatibos", "background_24", "Active"),
    ("Limpopo beginnersjag", 31500, 4, "rooibosrand", "lerato", "Hidden"),
    ("Waterberg jongjagter-pakket", 27500, 3, "kameeldoring", "lerato", "Hidden"),
    ("Vorige seisoen se Oos-Kaap jag", 36000, 4, "sneeuberg", "pieter", "Expired"),
    ("Vorige Kalahari winterjag", 44000, 5, "kameelpan", "background_25", "Expired"),
]


def _validate_password(password: str) -> None:
    if not re.match(r"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{12,}$", password):
        raise DemoError("DEMO_PASSWORD does not meet the WebAPI registration policy.")


def _data_url(path: Path) -> str:
    """An image file as a data URL, the format the website stores farm and advert pictures in."""
    mime = "image/png" if path.suffix.lower() == ".png" else "image/jpeg"
    return f"data:{mime};base64,{base64.b64encode(path.read_bytes()).decode('ascii')}"


def _insert_id(db: Database, sql: str, params: Sequence[Any]) -> int:
    row = db.execute(sql, params).fetchone()
    if row is None:
        raise DemoError("SQL insert did not return an identity value.")
    return int(row[0])


def _ensure_registry(db: Database) -> None:
    db.execute(
        """
        IF OBJECT_ID(N'dbo.demoSeedRegistry', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.demoSeedRegistry (
                registryId INT IDENTITY(1,1) PRIMARY KEY,
                entityType NVARCHAR(40) NOT NULL,
                entityKey NVARCHAR(120) NOT NULL,
                entityId BIGINT NOT NULL,
                demoDate DATE NOT NULL,
                updatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                CONSTRAINT UQ_demoSeedRegistry UNIQUE(entityType, entityKey)
            );
        END;
        IF COL_LENGTH('dbo.ph', 'phStandardDailyRate') IS NULL
            ALTER TABLE dbo.ph ADD phStandardDailyRate DECIMAL(18,2) NULL;
        IF COL_LENGTH('dbo.ph', 'phDangerousGameDailyRate') IS NULL
            ALTER TABLE dbo.ph ADD phDangerousGameDailyRate DECIMAL(18,2) NULL;
        IF COL_LENGTH('dbo.ph', 'phHasDangerousGame') IS NULL
            ALTER TABLE dbo.ph ADD phHasDangerousGame BIT NOT NULL DEFAULT 0;
        IF COL_LENGTH('dbo.teamInvites', 'inviteExpeditionName') IS NULL
            ALTER TABLE dbo.teamInvites ADD inviteExpeditionName NVARCHAR(200) NULL;
        IF COL_LENGTH('dbo.teamInvites', 'inviteTerms') IS NULL
            ALTER TABLE dbo.teamInvites ADD inviteTerms NVARCHAR(1000) NULL;
        IF OBJECT_ID(N'dbo.roleSubscriptions', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.roleSubscriptions (
                subscriptionId INT IDENTITY(1,1) PRIMARY KEY,
                subUserId INT NOT NULL,
                subRoleCode NVARCHAR(50) NOT NULL,
                subIsActive BIT NOT NULL DEFAULT 1,
                subExpiryDate DATETIME2 NOT NULL,
                subLastPaidAmount DECIMAL(18,2) NULL,
                subLastPaymentReference NVARCHAR(100) NULL,
                subActiveDurationMonths INT NOT NULL DEFAULT 1,
                subLastPaidDate DATETIME2 NULL,
                subPaymentStatus NVARCHAR(30) NOT NULL DEFAULT 'Active'
            );
            CREATE UNIQUE INDEX UQ_roleSubscriptions_UserRole
                ON dbo.roleSubscriptions(subUserId, subRoleCode);
        END;
        """
    )


def _registry_get(db: Database, entity_type: str, key: str) -> int | None:
    value = db.scalar(
        "SELECT entityId FROM dbo.demoSeedRegistry WHERE entityType=? AND entityKey=?",
        (entity_type, key),
    )
    return None if value is None else int(value)


def _registry_set(db: Database, entity_type: str, key: str, entity_id: int, demo_date: dt.date) -> None:
    db.execute(
        """
        MERGE dbo.demoSeedRegistry AS target
        USING (SELECT ? AS entityType, ? AS entityKey) AS source
        ON target.entityType=source.entityType AND target.entityKey=source.entityKey
        WHEN MATCHED THEN UPDATE SET entityId=?, demoDate=?, updatedAt=SYSUTCDATETIME()
        WHEN NOT MATCHED THEN INSERT(entityType, entityKey, entityId, demoDate)
            VALUES(?, ?, ?, ?);
        """,
        (entity_type, key, entity_id, demo_date, entity_type, key, entity_id, demo_date),
    )


def _ensure_accounts(
    db: Database,
    api: ApiClient,
    accounts: list[dict[str, Any]],
    password: str,
) -> None:
    current = user_ids(db)
    missing = [person for person in accounts if person["email"].lower() not in current]
    if not missing:
        print("Accounts: all 45 already exist")
        return
    print(f"Accounts: registering {len(missing)} missing account(s) through WebAPI")
    db.commit()
    for number, person in enumerate(missing, start=1):
        api.post(
            "/api/user/register",
            registration_payload(person, password),
            expected=(200,),
        )
        if number % 10 == 0 or number == len(missing):
            print(f"  registered {number}/{len(missing)}")


def _ensure_role_and_profile(
    db: Database,
    user_id: int,
    person: dict[str, Any],
    role_code: str,
    visible_till: dt.datetime,
) -> int:
    role_id = db.scalar("SELECT TOP 1 roleId FROM dbo.roles WHERE UPPER(roleCode)=?", (role_code,))
    if role_id is None:
        raise DemoError(f"Required role {role_code} does not exist.")
    user_role_id = db.scalar(
        "SELECT userRoleId FROM dbo.userRoles WHERE urUserId=? AND urRoleId=?",
        (user_id, role_id),
    )
    if user_role_id is None:
        user_role_id = _insert_id(
            db,
            "INSERT dbo.userRoles(urUserId,urRoleId) OUTPUT INSERTED.userRoleId VALUES(?,?)",
            (user_id, role_id),
        )
    user_role_id = int(user_role_id)
    name = person.get("business_name", f"{person['surname']} Wilddienste")
    reg = person.get("business_registration", "DEMO-NIE-GELDIG-NIE")
    years = int(person.get("years_experience", 5))
    bio = person.get("bio", "Ervare MikPunt-platformlid.")

    if role_code == "HUNTER":
        db.execute(
            """
            IF EXISTS(SELECT 1 FROM dbo.hunter WHERE hUserRoleId=?)
                UPDATE dbo.hunter SET hCountry=?,hUsername=?,hBio=? WHERE hUserRoleId=?
            ELSE INSERT dbo.hunter(hUserRoleId,hCountry,hUsername,hBio) VALUES(?,?,?,?);
            """,
            (user_role_id, person.get("country", "Suid-Afrika"), person.get("username", person["first_name"]), bio,
             user_role_id, user_role_id, person.get("country", "Suid-Afrika"), person.get("username", person["first_name"]), bio),
        )
    elif role_code == "PJ":
        db.execute(
            """
            IF EXISTS(SELECT 1 FROM dbo.pj WHERE pjUserRoleId=?)
                UPDATE dbo.pj SET pjBusinessName=?,pjBusinessRegistrationNumber=?,pjPubliclyVisibleTill=?,pjYearsOfExperience=?,pjBio=? WHERE pjUserRoleId=?
            ELSE INSERT dbo.pj(pjUserRoleId,pjBusinessName,pjBusinessRegistrationNumber,pjPubliclyVisibleTill,pjYearsOfExperience,pjBio) VALUES(?,?,?,?,?,?);
            """,
            (user_role_id, name, reg, visible_till, years, bio, user_role_id,
             user_role_id, name, reg, visible_till, years, bio),
        )
    elif role_code == "PH":
        standard = person.get("standard_daily_rate", 3500 + (user_id % 4) * 400)
        dangerous = person.get("dangerous_game_daily_rate", 6900 + (user_id % 3) * 500)
        has_dg = 1 if person.get("dangerous_game_daily_rate") else 0
        db.execute(
            """
            IF EXISTS(SELECT 1 FROM dbo.ph WHERE phUserRoleId=?)
                UPDATE dbo.ph SET phPubliclyVisibleTill=?,phYearsOfExperience=?,phBio=?,phStandardDailyRate=?,phDangerousGameDailyRate=?,phHasDangerousGame=? WHERE phUserRoleId=?
            ELSE INSERT dbo.ph(phUserRoleId,phPubliclyVisibleTill,phYearsOfExperience,phBio,phStandardDailyRate,phDangerousGameDailyRate,phHasDangerousGame) VALUES(?,?,?,?,?,?,?);
            """,
            (user_role_id, visible_till, years, bio, standard, dangerous, has_dg, user_role_id,
             user_role_id, visible_till, years, bio, standard, dangerous, has_dg),
        )
    elif role_code == "VG":
        db.execute(
            """
            IF EXISTS(SELECT 1 FROM dbo.vg WHERE vgUserRoleId=?)
                UPDATE dbo.vg SET vgHasDrone=?,vgDroneLicence=?,vgPubliclyVisibleTill=?,vgCompanyName=?,vgCompanyRegistrationNumber=?,vgYearsOfExperience=? WHERE vgUserRoleId=?
            ELSE INSERT dbo.vg(vgUserRoleId,vgHasDrone,vgDroneLicence,vgPubliclyVisibleTill,vgCompanyName,vgCompanyRegistrationNumber,vgYearsOfExperience) VALUES(?,?,?,?,?,?,?);
            """,
            (user_role_id, int(person.get("has_drone", True)), person.get("drone_licence", f"DEMO-RPL-{user_id}"), visible_till, name, reg, years, user_role_id,
             user_role_id, int(person.get("has_drone", True)), person.get("drone_licence", f"DEMO-RPL-{user_id}"), visible_till, name, reg, years),
        )
    elif role_code == "FARMER":
        db.execute(
            """
            IF EXISTS(SELECT 1 FROM dbo.farmer WHERE fUserRoleId=?)
                UPDATE dbo.farmer SET fBusinessName=?,fBusinessRegistrationNumber=?,fPubliclyVisibleTill=?,fBio=? WHERE fUserRoleId=?
            ELSE INSERT dbo.farmer(fUserRoleId,fBusinessName,fBusinessRegistrationNumber,fPubliclyVisibleTill,fBio) VALUES(?,?,?,?,?);
            """,
            (user_role_id, name, reg, visible_till, bio, user_role_id,
             user_role_id, name, reg, visible_till, bio),
        )
    elif role_code == "AP":
        db.execute(
            "IF NOT EXISTS(SELECT 1 FROM dbo.ap WHERE apUserRoleId=?) INSERT dbo.ap(apUserRoleId) VALUES(?)",
            (user_role_id, user_role_id),
        )

    if role_code in {"PJ", "PH", "VG", "FARMER"}:
        amount = {"PJ": 699, "PH": 299, "VG": 249, "FARMER": 499}[role_code]
        db.execute(
            """
            MERGE dbo.roleSubscriptions AS target
            USING(SELECT ? AS subUserId, ? AS subRoleCode) AS source
            ON target.subUserId=source.subUserId AND target.subRoleCode=source.subRoleCode
            WHEN MATCHED THEN UPDATE SET subIsActive=1,subExpiryDate=?,subLastPaidAmount=?,subLastPaymentReference=?,subActiveDurationMonths=12,subLastPaidDate=SYSUTCDATETIME(),subPaymentStatus='Active'
            WHEN NOT MATCHED THEN INSERT(subUserId,subRoleCode,subIsActive,subExpiryDate,subLastPaidAmount,subLastPaymentReference,subActiveDurationMonths,subLastPaidDate,subPaymentStatus)
                VALUES(?,?,1,?,?,?,12,SYSUTCDATETIME(),'Active');
            """,
            (user_id, role_code, visible_till, amount, f"DEMO-SUB-{user_id}-{role_code}",
             user_id, role_code, visible_till, amount, f"DEMO-SUB-{user_id}-{role_code}"),
        )
    return user_role_id


def _seed_profiles(db: Database, accounts: list[dict[str, Any]], demo_date: dt.date) -> dict[str, dict[str, int]]:
    ids = user_ids(db)
    if len(ids) != 45:
        raise DemoError(f"Expected 45 demo accounts after registration; found {len(ids)}.")
    visible_till = dt.datetime.combine(demo_date + dt.timedelta(days=365), dt.time(12))
    role_map: dict[str, dict[str, int]] = {}
    for person in accounts:
        user_id = ids[person["email"].lower()]
        db.execute(
            """
            UPDATE dbo.users SET uName=?,uSurname=?,uPhone=?,uIsEmailVerified=1,uIsPhoneVerified=1,
                uRegistrationDate=COALESCE(uRegistrationDate,?),uIsSuspended=?,uAccountStatus=?,uStatusReason=?
            WHERE userId=?
            """,
            (
                person["first_name"], person["surname"], person["phone"],
                dt.datetime.combine(demo_date - dt.timedelta(days=240), dt.time(9)),
                1 if person["key"] in {"background_36", "background_37"} else 0,
                "Suspended" if person["key"] in {"background_36", "background_37"} else "Active",
                "Rekening tydelik opgeskort vir die administrasie-demo." if person["key"] in {"background_36", "background_37"} else None,
                user_id,
            ),
        )
        role_map[person["key"]] = {}
        for role in person["roles"]:
            role_map[person["key"]][role] = _ensure_role_and_profile(
                db, user_id, person, role, visible_till
            )
    return role_map


def _seed_background_documents(
    db: Database,
    accounts: list[dict[str, Any]],
    role_map: dict[str, dict[str, int]],
    demo_date: dt.date,
) -> None:
    api_keys = {"pieter", "thabo", "vusi", "anna", "lerato"}
    requirements = db.rows(
        """
        SELECT rdr.roleCode,rdr.documentTypeId,dt.dtName
        FROM dbo.roleDocumentRequirements rdr
        JOIN dbo.documentType dt ON dt.dtId=rdr.documentTypeId
        WHERE rdr.isRequired=1
        """
    )
    by_role: dict[str, list[tuple[int, str]]] = {}
    for row in requirements:
        by_role.setdefault(str(row[0]).upper(), []).append((int(row[1]), str(row[2])))
    for person in accounts:
        if person["key"] in api_keys:
            continue
        user_id = user_ids(db)[person["email"].lower()]
        for role, user_role_id in role_map[person["key"]].items():
            for document_type_id, document_name in by_role.get(role, []):
                existing = db.scalar(
                    "SELECT udId FROM dbo.userDocumentation WHERE udUserRole=? AND udDtId=? AND udIsDeleted=0",
                    (user_role_id, document_type_id),
                )
                if existing is None:
                    document_id = _insert_id(
                        db,
                        """
                        INSERT dbo.userDocumentation(udUserRole,udName,udFilePath,udIssuedTimestamp,udExpirationTimestamp,udStatus,udUploadedTimestamp,udDtId,udUploadedBy,udIsDeleted,udVerifiedBy,udVerifiedAt)
                        OUTPUT INSERTED.udId VALUES(?,?,?,?,?,'Approved',?,?,?,0,?,?)
                        """,
                        (
                            user_role_id, f"{document_name} - {person['first_name']} {person['surname']}",
                            "data:application/pdf;base64,JVBERi0xLjQKJSVFT0YK",
                            demo_date - dt.timedelta(days=180), demo_date + dt.timedelta(days=545),
                            demo_date - dt.timedelta(days=60), document_type_id, user_id, user_id,
                            demo_date - dt.timedelta(days=59),
                        ),
                    )
                    if db.table_exists("documentAuditLog"):
                        db.execute(
                            """
                            INSERT dbo.documentAuditLog(documentId,userRoleId,actorUserId,actionType,documentTypeName,fileName,createdAt,notes)
                            VALUES(?,?,?,'Created',?,?,?,'Demo-agtergrondprofiel deur die stelsel geskep.')
                            """,
                            (document_id, user_role_id, user_id, document_name, f"{person['key']}.pdf", demo_date - dt.timedelta(days=60)),
                        )
                else:
                    db.execute(
                        "UPDATE dbo.userDocumentation SET udStatus='Approved',udVerifiedAt=COALESCE(udVerifiedAt,?),udRejectionReason=NULL WHERE udId=?",
                        (demo_date - dt.timedelta(days=59), existing),
                    )


def _seed_farms(
    db: Database,
    accounts: list[dict[str, Any]],
    roles: dict[str, dict[str, int]],
    demo_date: dt.date,
) -> dict[str, int]:
    farmer_keys = ["anna", "background_32", "background_33", "background_34", "background_35"]
    farm_ids: dict[str, int] = {}
    for index, spec in enumerate(FARMS):
        key, name, city, province, lat, lon, size, terrain, fencing, lodge_price, rooms, catering = spec
        farmer_key = farmer_keys[index % len(farmer_keys)]
        farmer_id = int(db.scalar("SELECT farmerId FROM dbo.farmer WHERE fUserRoleId=?", (roles[farmer_key]["FARMER"],)))
        farm_id = _registry_get(db, "farm", key)
        if farm_id is None or not db.scalar("SELECT COUNT(*) FROM dbo.farm WHERE farmId=?", (farm_id,)):
            address_id = _insert_id(
                db,
                """
                INSERT dbo.addresses(addressNumber,addressStreetName,addressCity,addressRegion,addressPostalCode,addressCountry,addressLatitude,addressLongitude)
                OUTPUT INSERTED.addressId VALUES('Plaas','Distrikspad',?,?,?,?,?,?)
                """,
                (city, province, f"{500 + index:04d}", "Suid-Afrika", lat, lon),
            )
            farm_id = _insert_id(
                db,
                """
                INSERT dbo.farm(farmFarmerId,farmDescription,farmMediaFolder,farmName,farmSize,farmAddressId,farmTerrainType,farmFencingType,farmStatus)
                OUTPUT INSERTED.farmId VALUES(?,?,?,?,?,?,?,?, 'Active')
                """,
                (farmer_id, FARM_DESCRIPTIONS[key], f"demo/farms/{key}", name, size, address_id, terrain, fencing),
            )
        else:
            db.execute(
                "UPDATE dbo.farm SET farmFarmerId=?,farmDescription=?,farmName=?,farmSize=?,farmTerrainType=?,farmFencingType=?,farmStatus='Active' WHERE farmId=?",
                (farmer_id, FARM_DESCRIPTIONS[key], name, size, terrain, fencing, farm_id),
            )
        _registry_set(db, "farm", key, farm_id, demo_date)
        farm_ids[key] = farm_id
        # Every farm gets its own photos (stored like uploads from the farm page: data URLs in farmImagesJson).
        photos = sorted((ASSETS_DIR / "farms" / key).glob("*.jpg"))
        if photos:
            db.execute("UPDATE dbo.farm SET farmImagesJson=? WHERE farmId=?", (json.dumps([_data_url(photo) for photo in photos]), farm_id))
        db.execute(
            """
            IF EXISTS(SELECT 1 FROM dbo.farmLodge WHERE flFarmId=?)
                UPDATE dbo.farmLodge SET flPrice=?,flRooms=?,flCateringIncluded=?,flFacilities=? WHERE flFarmId=?
            ELSE INSERT dbo.farmLodge(flFarmId,flPrice,flRooms,flCateringIncluded,flFacilities) VALUES(?,?,?,?,?);
            """,
            (farm_id, lodge_price, rooms, int(catering), "En suite kamers, braaiplek, Wi-Fi by die lodge en veilige vuurwapenkluis", farm_id,
             farm_id, lodge_price, rooms, int(catering), "En suite kamers, braaiplek, Wi-Fi by die lodge en veilige vuurwapenkluis"),
        )
        for offset in range(3 + index % 6):
            animal, quota, price = SPECIES[(index * 2 + offset) % len(SPECIES)]
            db.execute(
                """
                IF EXISTS(SELECT 1 FROM dbo.availableAnimals WHERE aaFarmId=? AND aaName=?)
                    UPDATE dbo.availableAnimals SET aaQuota=?,aaEntityPrice=? WHERE aaFarmId=? AND aaName=?
                ELSE INSERT dbo.availableAnimals(aaFarmId,aaName,aaQuota,aaEntityPrice) VALUES(?,?,?,?);
                """,
                (farm_id, animal, quota, price, farm_id, animal, farm_id, animal, quota, price),
            )
    # Kameeldoring always has the seven species called out in the script.
    for animal, quota, price in SPECIES[:7]:
        db.execute(
            """
            IF EXISTS(SELECT 1 FROM dbo.availableAnimals WHERE aaFarmId=? AND aaName=?)
                UPDATE dbo.availableAnimals SET aaQuota=?,aaEntityPrice=? WHERE aaFarmId=? AND aaName=?
            ELSE INSERT dbo.availableAnimals(aaFarmId,aaName,aaQuota,aaEntityPrice) VALUES(?,?,?,?);
            """,
            (farm_ids["kameeldoring"], animal, quota, price, farm_ids["kameeldoring"], animal,
             farm_ids["kameeldoring"], animal, quota, price),
        )
    return farm_ids


def _seed_packages(
    db: Database,
    roles: dict[str, dict[str, int]],
    farm_ids: dict[str, int],
    demo_date: dt.date,
) -> dict[str, int]:
    package_ids: dict[str, int] = {}
    for index, (title, price, duration, farm_key, pj_key, status) in enumerate(PACKAGES):
        pj_id = int(db.scalar("SELECT pjId FROM dbo.pj WHERE pjUserRoleId=?", (roles[pj_key]["PJ"],)))
        package_key = f"package_{index + 1:02d}"
        package_id = _registry_get(db, "package", package_key)
        available_from = demo_date + dt.timedelta(days=12 + index * 3)
        available_till = demo_date + dt.timedelta(days=240)
        if status == "Expired":
            available_from = demo_date - dt.timedelta(days=180)
            available_till = demo_date - dt.timedelta(days=20)
        description, inclusions, exclusions, observer_price = PACKAGE_DETAILS[title]
        # pIncludedItems is the website's "species | OBS_PRICE:<amount>" field: the farm's huntable species.
        species = [str(row[0]) for row in db.rows("SELECT aaName FROM dbo.availableAnimals WHERE aaFarmId=? ORDER BY aaName", (farm_ids[farm_key],))]
        values = (
            dt.datetime.combine(demo_date - dt.timedelta(days=90 - index), dt.time(8)),
            available_till, farm_ids[farm_key], pj_id, price,
            description,
            title, available_from, duration, 1, 6, duration, 6,
            f"{', '.join(species)} | OBS_PRICE:{observer_price}", status,
            "\n".join(inclusions), "\n".join(exclusions),
        )
        if package_id is None or not db.scalar("SELECT COUNT(*) FROM dbo.packages WHERE packageId=?", (package_id,)):
            package_id = _insert_id(
                db,
                """
                INSERT dbo.packages(pCreatedTimestamp,pAvailableTill,pFarmId,pPjId,pStockPrice,pDescription,pTitle,pAvailableFrom,pMinExpeditionLength,pMinGuests,pMaxGuests,pDurationDays,pMaxParticipants,pIncludedItems,pStatus,pInclusions,pExclusions)
                OUTPUT INSERTED.packageId VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)
                """,
                values,
            )
        else:
            db.execute(
                """
                UPDATE dbo.packages SET pCreatedTimestamp=?,pAvailableTill=?,pFarmId=?,pPjId=?,pStockPrice=?,pDescription=?,pTitle=?,pAvailableFrom=?,pMinExpeditionLength=?,pMinGuests=?,pMaxGuests=?,pDurationDays=?,pMaxParticipants=?,pIncludedItems=?,pStatus=?,pInclusions=?,pExclusions=? WHERE packageId=?
                """,
                values + (package_id,),
            )
        _registry_set(db, "package", package_key, package_id, demo_date)
        package_ids[package_key] = package_id
    return package_ids


def _seed_team_and_vg_packages(db: Database, roles: dict[str, dict[str, int]], demo_date: dt.date) -> None:
    pieter = roles["pieter"]["PJ"]
    members = [roles["thabo"]["PH"], roles["vusi"]["VG"], roles["background_26"]["PH"], roles["background_30"]["VG"]]
    for member in members:
        db.execute(
            "IF NOT EXISTS(SELECT 1 FROM dbo.team WHERE teamPjId=? AND teamMemberId=?) INSERT dbo.team(teamPjId,teamMemberId,teamRow3Placeholder) VALUES(?,?,'Aktiewe spanlid')",
            (pieter, member, pieter, member),
        )
    invitee = roles["background_27"]["PH"]
    db.execute(
        """
        IF EXISTS(SELECT 1 FROM dbo.teamInvites WHERE invitePjId=? AND inviteMemberId=?)
            UPDATE dbo.teamInvites SET inviteStatus='Pending',inviteDate=?,
                inviteExpeditionName=N'Waterberg Koedoe-safari, 4 dae',
                inviteTerms=N'PH-dienste vir vier dae; daaglikse tarief soos op profiel; verblyf en etes ingesluit.'
            WHERE invitePjId=? AND inviteMemberId=?
        ELSE INSERT dbo.teamInvites(invitePjId,inviteMemberId,inviteStatus,inviteDate,inviteExpeditionName,inviteTerms)
            VALUES(?,?,'Pending',?,N'Waterberg Koedoe-safari, 4 dae',N'PH-dienste vir vier dae; daaglikse tarief soos op profiel; verblyf en etes ingesluit.');
        """,
        (pieter, invitee, demo_date - dt.timedelta(days=1), pieter, invitee, pieter, invitee, demo_date - dt.timedelta(days=1)),
    )
    vusi_id = int(db.scalar("SELECT vgId FROM dbo.vg WHERE vgUserRoleId=?", (roles["vusi"]["VG"],)))
    vg_packages = [
        ("Halwe jagdag-foto's", "Limpopo", "Twee kameras", "Geredigeerde foto's", 6500),
        ("Volledige jagdag-video", "Limpopo en Noordwes", "4K-kameras en klank", "Volledige jagdag met kort storie", 12500),
        ("Hommeltuig-byvoeging", "Limpopo en Noordwes", "Gelisansieerde hommeltuig", "Lugbeeld-byvoeging", 4200),
        ("Trofee en lodge-pakket", "Suid-Afrika", "Studio-beligting", "Trofee-, span- en lodgefoto's", 5800),
    ]
    for title, region, equipment, description, price in vg_packages:
        db.execute(
            """
            IF EXISTS(SELECT 1 FROM dbo.vgPackages WHERE vgId=? AND vgpTitle=?)
                UPDATE dbo.vgPackages SET vgpRegionCovered=?,vgpEquipmentProvided=?,vgpDescription=?,vgpPrice=? WHERE vgId=? AND vgpTitle=?
            ELSE INSERT dbo.vgPackages(vgId,vgpTitle,vgpRegionCovered,vgpEquipmentProvided,vgpDescription,vgpPrice) VALUES(?,?,?,?,?,?);
            """,
            (vusi_id, title, region, equipment, description, price, vusi_id, title,
             vusi_id, title, region, equipment, description, price),
        )


def _upsert_booking(
    db: Database,
    key: str,
    demo_date: dt.date,
    hunter_role_id: int,
    package_id: int,
    start: dt.date,
    end: dt.date,
    status: str,
    hunt_guests: int,
    non_hunt_guests: int,
    ph_id: int | None,
    vg_id: int | None,
    payment: str,
) -> int:
    booking_id = _registry_get(db, "booking", key)
    params = (hunter_role_id, package_id, start, end, status, hunt_guests, non_hunt_guests, ph_id, vg_id, payment)
    if booking_id is None or not db.scalar("SELECT COUNT(*) FROM dbo.bookings WHERE bookingId=?", (booking_id,)):
        booking_id = _insert_id(
            db,
            """
            INSERT dbo.bookings(bookHunterId,bookPackageId,bookStartDate,bookEndDate,bookStatus,bookHuntGuests,bookNonHuntGuests,bookPhId,bookVgId,bookPaymentStatus)
            OUTPUT INSERTED.bookingId VALUES(?,?,?,?,?,?,?,?,?,?)
            """,
            params,
        )
    else:
        db.execute(
            """
            UPDATE dbo.bookings SET bookHunterId=?,bookPackageId=?,bookStartDate=?,bookEndDate=?,bookStatus=?,bookHuntGuests=?,bookNonHuntGuests=?,bookPhId=?,bookVgId=?,bookPaymentStatus=? WHERE bookingId=?
            """,
            params + (booking_id,),
        )
    # The outfitter confirms the price when approving: every booking past "Pending" carries a rand total
    # (package rate per hunter, R1 450 a night per guest at the lodge) and a price breakdown in ZAR.
    if status != "Pending":
        rate = float(db.scalar("SELECT pStockPrice FROM dbo.packages WHERE packageId=?", (package_id,)) or 0)
        nights = max((end - start).days, 1)
        guests = max(hunt_guests, 1) + max(non_hunt_guests, 0)
        accommodation = 1450.0 * nights * guests
        total = rate * max(hunt_guests, 1) + accommodation
        breakdown = json.dumps({
            "Days": nights + 1, "HunterCount": max(hunt_guests, 1), "ObserverCount": max(non_hunt_guests, 0),
            "AccommodationTotal": accommodation, "GrandTotal": total, "Currency": "ZAR",
            "DepositPercentage": 100, "DepositAmount": total, "OutstandingBalance": 0 if payment == "Paid" else total,
        })
        db.execute(
            "UPDATE dbo.bookings SET bookTotalPrice=?,bookAccommodationPrice=?,bookPriceBreakdown=?,"
            "bookPaidAt=CASE WHEN ?='Paid' THEN ? ELSE NULL END WHERE bookingId=?",
            (total, accommodation, breakdown, payment, dt.datetime.combine(start - dt.timedelta(days=10), dt.time(10)), booking_id),
        )
    _registry_set(db, "booking", key, booking_id, demo_date)
    return booking_id


def _seed_bookings(
    db: Database,
    accounts: list[dict[str, Any]],
    roles: dict[str, dict[str, int]],
    package_ids: dict[str, int],
    demo_date: dt.date,
) -> dict[str, int]:
    thabo_ph = int(db.scalar("SELECT phId FROM dbo.ph WHERE phUserRoleId=?", (roles["thabo"]["PH"],)))
    vusi_vg = int(db.scalar("SELECT vgId FROM dbo.vg WHERE vgUserRoleId=?", (roles["vusi"]["VG"],)))
    johan = roles["johan"]["HUNTER"]
    main_package = package_ids["package_01"]
    bookings: dict[str, int] = {}
    bookings["b_team"] = _upsert_booking(db, "b_team", demo_date, johan, main_package, demo_date + dt.timedelta(days=21), demo_date + dt.timedelta(days=25), "Paid", 2, 0, thabo_ph, vusi_vg, "Paid")
    bookings["b_hunt"] = _upsert_booking(db, "b_hunt", demo_date, johan, main_package, demo_date - dt.timedelta(days=5), demo_date - dt.timedelta(days=2), "Completed", 1, 0, thabo_ph, vusi_vg, "Paid")
    for index, days_ago in enumerate((55, 105, 155, 215), start=1):
        bookings[f"history_{index}"] = _upsert_booking(
            db, f"history_{index}", demo_date, johan, package_ids[f"package_{2 + index:02d}"],
            demo_date - dt.timedelta(days=days_ago + 4), demo_date - dt.timedelta(days=days_ago),
            "Completed", 1, index % 2, thabo_ph, vusi_vg if index % 2 else None, "Paid",
        )

    hunters = [(person["key"], roles[person["key"]]["HUNTER"]) for person in accounts if "HUNTER" in person["roles"] and person["key"] != "johan"]
    statuses = cycle(["Completed", "Completed", "Completed", "Paid", "Approved", "PendingStaffAcceptance", "Pending", "Rejected", "Cancelled"])
    for index in range(39):
        status = next(statuses)
        hunter_key, hunter_role = hunters[index % len(hunters)]
        if status in {"Completed", "Rejected", "Cancelled"}:
            start = demo_date - dt.timedelta(days=15 + index * 4)
        else:
            start = demo_date + dt.timedelta(days=8 + index * 3)
        end = start + dt.timedelta(days=2 + index % 5)
        ph_id = thabo_ph if status not in {"Pending", "Rejected"} else None
        vg_id = vusi_vg if index % 3 == 0 and ph_id else None
        payment = "Paid" if status in {"Paid", "Completed"} else "Pending"
        key = f"background_booking_{index + 1:02d}"
        bookings[key] = _upsert_booking(
            db, key, demo_date, hunter_role, package_ids[f"package_{index % 12 + 1:02d}"],
            start, end, status, 1 + index % 2, index % 3 == 0, ph_id, vg_id, payment,
        )

    # Accepted staff and the linked companion are central to B-Team.
    user_id_by_email = user_ids(db)
    pieter_id = user_id_by_email[account_by_key(accounts, "pieter")["email"]]
    thabo_id = user_id_by_email[account_by_key(accounts, "thabo")["email"]]
    vusi_id = user_id_by_email[account_by_key(accounts, "vusi")["email"]]
    for booking_key in ("b_team", "b_hunt"):
        booking_id = bookings[booking_key]
        for role, assignee in (("PH", thabo_id), ("VG", vusi_id)):
            db.execute(
                """
                IF EXISTS(SELECT 1 FROM dbo.bookingStaffAssignments WHERE bookingId=? AND staffRole=? AND status IN('Pending','Accepted'))
                    UPDATE dbo.bookingStaffAssignments SET assigneeUserId=?,assignedByUserId=?,status='Accepted',declineReason=NULL,respondedAt=? WHERE bookingId=? AND staffRole=? AND status IN('Pending','Accepted')
                ELSE INSERT dbo.bookingStaffAssignments(bookingId,staffRole,assigneeUserId,assignedByUserId,status,assignedAt,respondedAt) VALUES(?,?,?,?,'Accepted',?,?);
                """,
                (booking_id, role, assignee, pieter_id, demo_date - dt.timedelta(days=2), booking_id, role,
                 booking_id, role, assignee, pieter_id, demo_date - dt.timedelta(days=6), demo_date - dt.timedelta(days=5)),
            )
    mia_role = roles["mia"]["HUNTER"]
    mia_user = user_id_by_email[account_by_key(accounts, "mia")["email"]]
    db.execute(
        """
        IF EXISTS(SELECT 1 FROM dbo.bookingCompanions WHERE bookingId=? AND inviteEmail=?)
            UPDATE dbo.bookingCompanions SET userRoleId=?,inviteName='Mia Botha',status='Linked',linkedAt=? WHERE bookingId=? AND inviteEmail=?
        ELSE INSERT dbo.bookingCompanions(bookingId,userRoleId,inviteName,inviteEmail,status,addedByUserId,addedAt,linkedAt) VALUES(?,?,'Mia Botha',?,'Linked',?,?,?);
        """,
        (bookings["b_team"], account_by_key(accounts, "mia")["email"], mia_role, demo_date - dt.timedelta(days=4), bookings["b_team"], account_by_key(accounts, "mia")["email"],
         bookings["b_team"], mia_role, account_by_key(accounts, "mia")["email"], user_id_by_email[account_by_key(accounts, "johan")["email"]], demo_date - dt.timedelta(days=4), demo_date - dt.timedelta(days=4)),
    )
    return bookings


def _seed_route_media_and_recap(
    db: Database,
    accounts: list[dict[str, Any]],
    roles: dict[str, dict[str, int]],
    bookings: dict[str, int],
    demo_date: dt.date,
) -> None:
    booking_id = bookings["b_hunt"]
    payload = route_payload(demo_date)
    johan_id = user_ids(db)[account_by_key(accounts, "johan")["email"]]
    route_id = _registry_get(db, "route", "b_hunt")
    points_json = json.dumps(payload["points"], ensure_ascii=False, separators=(",", ":"))
    pins_json = json.dumps(payload["pins"], ensure_ascii=False, separators=(",", ":"))
    if route_id is None or not db.scalar("SELECT COUNT(*) FROM dbo.bookingRoutes WHERE routeId=?", (route_id,)):
        route_id = _insert_id(
            db,
            """
            INSERT dbo.bookingRoutes(bookingId,ownerUserId,ownerUserRoleId,sourceHuntSessionId,routePointsJson,pinsJson,pointCount,distanceMeters,durationSeconds,routeVersion,status,submittedAt,updatedAt)
            OUTPUT INSERTED.routeId VALUES(?,?,?,?,?,?,?,?,?,1,'Submitted',?,?)
            """,
            (booking_id, johan_id, roles["johan"]["HUNTER"], payload["sourceHuntSessionId"], points_json, pins_json, len(payload["points"]), payload["distanceMeters"], payload["durationSeconds"], demo_date - dt.timedelta(days=2), demo_date - dt.timedelta(days=2)),
        )
    else:
        db.execute(
            "UPDATE dbo.bookingRoutes SET bookingId=?,ownerUserId=?,ownerUserRoleId=?,sourceHuntSessionId=?,routePointsJson=?,pinsJson=?,pointCount=?,distanceMeters=?,durationSeconds=?,status='Submitted',submittedAt=?,updatedAt=? WHERE routeId=?",
            (booking_id, johan_id, roles["johan"]["HUNTER"], payload["sourceHuntSessionId"], points_json, pins_json, len(payload["points"]), payload["distanceMeters"], payload["durationSeconds"], demo_date - dt.timedelta(days=2), demo_date - dt.timedelta(days=2), route_id),
        )
    _registry_set(db, "route", "b_hunt", route_id, demo_date)
    db.execute("UPDATE dbo.bookingRouteSelections SET supersededAt=? WHERE bookingId=? AND supersededAt IS NULL AND routeId<>?", (demo_date - dt.timedelta(days=2), booking_id, route_id))
    db.execute(
        "IF NOT EXISTS(SELECT 1 FROM dbo.bookingRouteSelections WHERE bookingId=? AND routeId=? AND supersededAt IS NULL) INSERT dbo.bookingRouteSelections(bookingId,routeId,selectedByUserId,selectedAt) VALUES(?,?,?,?)",
        (booking_id, route_id, booking_id, route_id, user_ids(db)[account_by_key(accounts, "thabo")["email"]], demo_date - dt.timedelta(days=2)),
    )



def _mobile_token(
    api: ApiClient,
    person: dict[str, Any],
    user_role_id: int,
    password: str,
) -> str:
    response = api.post(
        "/api/auth/login",
        {"email": person["email"], "password": password, "userRoleId": user_role_id},
        expected=(200,),
    )
    token = str((response or {}).get("accessToken") or (response or {}).get("AccessToken") or "")
    if not token:
        raise DemoError(f"MobileAPI did not return an access token for {person['email']}")
    return token


def _seed_media_and_recap_via_api(
    db: Database,
    api: ApiClient,
    config: DemoConfig,
    accounts: list[dict[str, Any]],
    roles: dict[str, dict[str, int]],
    bookings: dict[str, int],
    demo_date: dt.date,
) -> None:
    """Create genuine stored media and a worker-rendered recap when assets exist."""

    booking_id = bookings["b_hunt"]
    hunter_files = sorted((ASSETS_DIR / "hunt" / "hunter").glob("*.jpg"))[:8]
    ph_files = sorted((ASSETS_DIR / "hunt" / "ph").glob("*.jpg"))[:4]
    vg_files = sorted((ASSETS_DIR / "hunt" / "vg").glob("*.mp4"))[:2]
    if len(hunter_files) < 8 or len(ph_files) < 4:
        print(
            "Media/recap skipped: add 8 hunter JPGs and 4 PH JPGs under "
            "tools/demo/assets/hunt; check_demo.py will keep this visible."
        )
        return

    johan = account_by_key(accounts, "johan")
    thabo = account_by_key(accounts, "thabo")
    vusi = account_by_key(accounts, "vusi")
    hunter_token = _mobile_token(api, johan, roles["johan"]["HUNTER"], config.password)
    ph_token = _mobile_token(api, thabo, roles["thabo"]["PH"], config.password)
    vg_token = (
        _mobile_token(api, vusi, roles["vusi"]["VG"], config.password)
        if vg_files and "vusi" in roles and "VG" in roles["vusi"]
        else None
    )

    trophy_asset = ASSETS_DIR / "trophies" / "kudu.jpg"
    if trophy_asset.exists():
        trophy_id = _registry_get(db, "trophy", "trophy_01")
        submission_id = _registry_get(db, "submission", "b_hunt_approved")
        photo_path = db.scalar("SELECT tPhotoPath FROM dbo.trophies WHERE trophyId=?", (trophy_id,))
        stored_photo_ready = False
        if photo_path and str(photo_path).startswith("/uploads/trophies/"):
            try:
                api.request("GET", str(photo_path), token=hunter_token, expected=(200,))
                stored_photo_ready = True
            except DemoError:
                pass
        if not stored_photo_ready:
            uploaded = api.multipart(
                "/api/trophies/photos",
                fields={},
                file_field="photo",
                file_path=trophy_asset,
                token=hunter_token,
                expected=(201,),
            )
            photo_path = uploaded.get("photoPath", uploaded.get("PhotoPath"))
            if not photo_path:
                raise DemoError("MobileAPI accepted the trophy photo without returning photoPath.")
            db.execute("UPDATE dbo.trophies SET tPhotoPath=? WHERE trophyId=?", (photo_path, trophy_id))
            db.execute(
                "UPDATE dbo.trophySubmissions SET trophyPhotoStorageReference=? WHERE submissionId=?",
                (photo_path, submission_id),
            )
            db.commit()

    existing = api.request("GET", f"/api/bookings/{booking_id}/media", token=hunter_token, expected=(200,)) or []
    existing_keys = {
        (str(item.get("uploaderRole", item.get("UploaderRole", ""))),
         str(item.get("originalFileName", item.get("OriginalFileName", ""))))
        for item in existing
    }
    route = route_payload(demo_date)
    points = route["points"]
    upload_specs = [
        ("Hunter", hunter_token, hunter_files, "Jagoomblik"),
        ("PH", ph_token, ph_files, "PH-veldnotas"),
    ]
    if vg_token and vg_files:
        upload_specs.append(("VG", vg_token, vg_files, "VG-videogreep"))

    point_index = 2
    for role_name, token, files, caption in upload_specs:
        for index, file_path in enumerate(files, start=1):
            if (role_name, file_path.name) in existing_keys:
                continue
            point = points[point_index % len(points)]
            file_field = "video" if file_path.suffix.lower() == ".mp4" else "photo"
            # The API refuses capture times in the future; before the demo date the hunt
            # still lies ahead, so upload with GPS only and stamp the hunt time afterwards.
            captured = dt.datetime.fromisoformat(str(point["timestamp"]).replace("Z", "+00:00"))
            if captured.tzinfo is None:
                captured = captured.replace(tzinfo=dt.timezone.utc)
            in_future = captured > dt.datetime.now(dt.timezone.utc)
            uploaded = api.multipart(
                f"/api/bookings/{booking_id}/media" + ("/videos" if file_field == "video" else ""),
                fields={
                    **({} if in_future else {"originalCaptureTimestamp": point["timestamp"]}),
                    "latitude": point["latitude"],
                    "longitude": point["longitude"],
                    **({} if file_field == "video" else {"orientation": 1}),
                    "caption": f"{caption} {index}",
                },
                file_field=file_field,
                file_path=file_path,
                token=token,
                expected=(201,),
            )
            if in_future:
                db.execute(
                    "UPDATE dbo.bookingMedia SET originalCaptureTimestamp=? WHERE mediaId=?",
                    (captured.astimezone(dt.timezone.utc).replace(tzinfo=None), int(uploaded.get("mediaId", uploaded.get("MediaId")))),
                )
                db.commit()
            point_index += 2

    media = api.request("GET", f"/api/bookings/{booking_id}/media", token=ph_token, expected=(200,)) or []
    demo_names = {path.name for path in hunter_files + ph_files + vg_files}
    demo_media = [
        item for item in media
        if str(item.get("originalFileName", item.get("OriginalFileName", ""))) in demo_names
    ]
    photo_names = {path.name for path in hunter_files + ph_files}
    photo_media = [
        item for item in demo_media
        if str(item.get("originalFileName", item.get("OriginalFileName", ""))) in photo_names
    ]
    # Five included photos yield a deterministic 20-25 second story while all
    # genuine uploads (including VG video clips) remain available in the media panel.
    included_ids = {
        int(item.get("mediaId", item.get("MediaId")))
        for item in sorted(
            photo_media,
            key=lambda item: int(item.get("mediaId", item.get("MediaId", 0))),
        )[:5]
    }
    for item in demo_media:
        media_id = int(item.get("mediaId", item.get("MediaId", 0)))
        desired = "Included" if media_id in included_ids else "Excluded"
        current = str(item.get("inclusionStatus", item.get("InclusionStatus", "")))
        if media_id and current != desired:
            api.post(
                f"/api/bookings/{booking_id}/media/{media_id}/moderation",
                {
                    "inclusionStatus": desired,
                    "reason": "Beperk die voorafgerenderde demo-storie tot 20-25 sekondes."
                    if desired == "Excluded" else None,
                },
                token=ph_token,
                expected=(204,),
            )

    recap = db.row(
        "SELECT TOP 1 recapId,status,outputPrivateStorageReference FROM dbo.bookingRecaps WHERE bookingId=? ORDER BY version DESC",
        (booking_id,),
    )
    if recap is not None and str(recap[1]) in {"Completed", "Published"}:
        try:
            api.request("GET", f"/api/recaps/{int(recap[0])}/content", token=hunter_token, expected=(200,))
            _registry_set(db, "recap", "b_hunt", int(recap[0]), demo_date)
            db.commit()
            return
        except DemoError:
            db.execute(
                "UPDATE dbo.bookingRecaps SET status='Failed',failureReason='Demo seed detected a missing stored output.' WHERE recapId=?",
                (int(recap[0]),),
            )
            db.commit()
            recap = db.row("SELECT recapId,status,NULL FROM dbo.bookingRecaps WHERE recapId=?", (int(recap[0]),))

    if recap is None:
        queued = api.post(
            f"/api/bookings/{booking_id}/recaps",
            {"title": "Johan se Waterberg-jag"},
            token=hunter_token,
            expected=(202,),
        )
        recap_id = int(queued.get("recapId", queued.get("RecapId")))
    else:
        recap_id = int(recap[0])
        status = str(recap[1])
        if status == "Failed":
            api.post(
                f"/api/bookings/{booking_id}/recaps/{recap_id}/retry",
                {},
                token=hunter_token,
                expected=(202,),
            )

    wait_seconds = max(10, int(os.getenv("DEMO_RECAP_WAIT_SECONDS", "180")))
    deadline = time.monotonic() + wait_seconds
    while time.monotonic() < deadline:
        current = db.row(
            "SELECT status,progress,durationSeconds,failureReason FROM dbo.bookingRecaps WHERE recapId=?",
            (recap_id,),
        )
        if current and str(current[0]) == "Completed":
            duration = int(current[2] or 0)
            if not 20 <= duration <= 25:
                raise DemoError(f"Recap {recap_id} completed at {duration}s; expected 20-25s.")
            _registry_set(db, "recap", "b_hunt", recap_id, demo_date)
            db.commit()
            return
        if current and str(current[0]) == "Failed":
            raise DemoError(f"Recap {recap_id} failed: {current[3]}")
        time.sleep(2)
    raise DemoError(
        f"Recap {recap_id} did not finish within {wait_seconds}s. Keep MobileAPI/FFmpeg running and rerun the seed."
    )


def _seed_trophy_photos_via_api(
    db: Database,
    api: ApiClient,
    config: DemoConfig,
    accounts: list[dict[str, Any]],
    roles: dict[str, dict[str, int]],
) -> None:
    """Upload a species photo for every demo trophy (trophy_01's kudu is done with the B-Hunt media)."""
    ids = user_ids(db)
    owners = {ids[account["email"].lower()]: account for account in accounts if account["email"].lower() in ids}
    tokens: dict[int, str] = {}
    uploaded = 0
    for index in range(1, 20):
        trophy_id = _registry_get(db, "trophy", f"trophy_{index + 1:02d}")
        if trophy_id is None:
            continue
        species = TROPHY_SPECIES[index % len(TROPHY_SPECIES)]
        photo = ASSETS_DIR / "trophies" / f"{species.lower()}-{index // len(TROPHY_SPECIES) + 1}.jpg"
        row = db.row("SELECT tOwnerUserId,tPhotoPath FROM dbo.trophies WHERE trophyId=?", (trophy_id,))
        owner_id, path = int(row[0]), str(row[1] or "")
        if not photo.exists() or owner_id not in owners:
            continue
        person = owners[owner_id]
        if owner_id not in tokens:
            tokens[owner_id] = _mobile_token(api, person, roles[person["key"]]["HUNTER"], config.password)
        if path.startswith(f"/uploads/trophies/{owner_id}/"):
            try:
                api.request("GET", path, token=tokens[owner_id], expected=(200,))
                continue
            except DemoError:
                pass
        result = api.multipart("/api/trophies/photos", fields={}, file_field="photo", file_path=photo,
                               token=tokens[owner_id], expected=(201,))
        new_path = result.get("photoPath", result.get("PhotoPath"))
        if not new_path:
            raise DemoError("MobileAPI accepted a trophy photo without returning photoPath.")
        db.execute("UPDATE dbo.trophies SET tPhotoPath=? WHERE trophyId=?", (new_path, trophy_id))
        db.commit()
        uploaded += 1
    print(f"Trophy photos uploaded: {uploaded}")


def _seed_chats(
    db: Database,
    accounts: list[dict[str, Any]],
    bookings: dict[str, int],
    demo_date: dt.date,
) -> None:
    ids = user_ids(db)
    people = [ids[person["email"].lower()] for person in accounts]
    core = [ids[account_by_key(accounts, key)["email"]] for key in ("johan", "mia", "pieter", "thabo", "vusi")]
    messages = [
        "Goeiemore almal, die Waterberg se weer lyk uitstekend.",
        "Ek bevestig dat die datums nog vir ons werk.",
        "Die paklys is op die bespreking beskikbaar.",
        "Onthou asseblief julle oorspronklike permitte.",
        "Ek bring die .308 en genoeg oefenammunisie saam.",
        "Die lodge het bevestig dat aandete ingesluit is.",
        "Ons ontmoet sesuur by die hoofhek.",
        "Ek sal die kameras en ekstra batterye gereed hê.",
        "Die wind draai teen laatmiddag suidwes.",
        "Dankie, ek sien baie uit na die jag.",
        "Veiligheidsinligting volg sodra almal aangemeld is.",
        "Kan ons die eerste oggend by die oostelike kamp begin?",
        "Ja, Thabo sal die roete by die lodge verduidelik.",
        "Alles is gereed. Ry veilig.",
        "betaal kontant by die hek",
    ]
    for chat_index in range(12):
        chat_key = f"chat_{chat_index + 1:02d}"
        chat_id = _registry_get(db, "chat", chat_key)
        title = "Waterberg jagspan" if chat_index == 0 else f"Jaggesprek {chat_index + 1}"
        if chat_id is None or not db.scalar("SELECT COUNT(*) FROM dbo.chat WHERE chatId=?", (chat_id,)):
            chat_id = _insert_id(
                db,
                "INSERT dbo.chat(cCreatedTimestamp,cTitle) OUTPUT INSERTED.chatId VALUES(?,?)",
                (demo_date - dt.timedelta(days=20 - chat_index), title),
            )
        else:
            db.execute("UPDATE dbo.chat SET cTitle=? WHERE chatId=?", (title, chat_id))
            db.execute("DELETE FROM dbo.flaggedChatMessages WHERE fcmChatId=?", (chat_id,))
            db.execute("DELETE FROM dbo.chatParticipants WHERE cpChatId=?", (chat_id,))
            db.execute("DELETE FROM dbo.chatMessages WHERE cmChatId=?", (chat_id,))
        _registry_set(db, "chat", chat_key, chat_id, demo_date)
        participants = core if chat_index == 0 else [people[(chat_index * 3) % len(people)], people[(chat_index * 3 + 1) % len(people)]]
        for participant in dict.fromkeys(participants):
            db.execute("INSERT dbo.chatParticipants(cpChatId,cpUserId,cpJoinedTimestamp) VALUES(?,?,?)", (chat_id, participant, demo_date - dt.timedelta(days=5)))
        count = 15 if chat_index == 0 else (13 if chat_index < 3 else 12)
        for index in range(count):
            sender = participants[index % len(participants)]
            content = messages[index % len(messages)] if chat_index == 0 else f"{messages[(index + chat_index) % (len(messages) - 1)]} ({chat_index + 1})"
            sent_at = dt.datetime.combine(demo_date - dt.timedelta(days=3 - index // 5), dt.time(8 + index % 10, (index * 7) % 60))
            message_id = _insert_id(db, "INSERT dbo.chatMessages(cmChatId,cmSenderId,cmContent,cmSentTimestamp) OUTPUT INSERTED.chatMessageId VALUES(?,?,?,?)", (chat_id, sender, content, sent_at))
            if chat_index == 0 and index == 14:
                db.execute(
                    "INSERT dbo.flaggedChatMessages(fcmMessageId,fcmChatId,fcmSenderId,fcmMessageText,fcmFlaggedKeywords,fcmSentAt,fcmStatus) VALUES(?,?,?,?,?,?,'PendingReview')",
                    (message_id, chat_id, sender, content, "kontant;hek", sent_at),
                )
            if chat_index == 1 and index == 4:
                db.execute(
                    "INSERT dbo.flaggedChatMessages(fcmMessageId,fcmChatId,fcmSenderId,fcmMessageText,fcmFlaggedKeywords,fcmSentAt,fcmStatus) VALUES(?,?,?,?,?,?,'PendingReview')",
                    (message_id, chat_id, sender, content, "buite platform", sent_at),
                )


def _seed_ratings_trophies_and_moderation(
    db: Database,
    accounts: list[dict[str, Any]],
    roles: dict[str, dict[str, int]],
    bookings: dict[str, int],
    farm_ids: dict[str, int],
    demo_date: dt.date,
) -> None:
    ids = user_ids(db)
    completed = [int(row[0]) for row in db.rows("SELECT b.bookingId FROM dbo.bookings b JOIN dbo.userRoles ur ON ur.userRoleId=b.bookHunterId JOIN dbo.users u ON u.userId=ur.urUserId WHERE LOWER(u.uEmail) LIKE ? AND b.bookStatus='Completed' AND b.bookingId<>? ORDER BY b.bookingId", (f"%@{MARKER_DOMAIN}", bookings["b_hunt"]))]
    demo_users = list(ids.values())
    comments = [
        "Uitstekende kommunikasie en professionele diens.", "Veilige, goed georganiseerde jagervaring.",
        "Pragtige plaas en baie goeie verblyf.", "Die span het elke detail betyds hanteer.",
        "Goeie ervaring; die aanwysings kon duideliker wees.", "Sal beslis weer saam met hierdie span jag.",
        "Excellent guiding and thoughtful attention to safety.", "A memorable South African hunting experience.",
    ]
    rating_ids: list[int] = []
    for index in range(70):
        booking_id = completed[index % len(completed)]
        rater = demo_users[index % len(demo_users)]
        ratee = demo_users[(index * 7 + 5) % len(demo_users)]
        if rater == ratee:
            ratee = demo_users[(index * 7 + 6) % len(demo_users)]
        existing = db.scalar("SELECT ratingId FROM dbo.ratings WHERE bookingId=? AND raterUserId=? AND rateeUserId=?", (booking_id, rater, ratee))
        stars = 5 if index % 10 < 6 else 4 if index % 10 < 9 else 3
        comment = comments[index % len(comments)]
        created = dt.datetime.combine(demo_date - dt.timedelta(days=210 - (index * 3) % 205), dt.time(17, index % 60))
        if existing is None:
            rating_id = _insert_id(db, "INSERT dbo.ratings(bookingId,raterUserId,rateeUserId,rateeRoleCode,stars,comment,createdAt,isExcluded) OUTPUT INSERTED.ratingId VALUES(?,?,?,'PH',?,?,?,0)", (booking_id, rater, ratee, stars, comment, created))
            if db.table_exists("ratingHistory"):
                db.execute("INSERT dbo.ratingHistory(changeType,ratingId,bookingId,raterUserId,rateeUserId,previousStars,previousComment,newStars,newComment,changedAt) VALUES('INSERT',?,?,?,?,NULL,NULL,?,?,?)", (rating_id, booking_id, rater, ratee, stars, comment, created))
        else:
            rating_id = int(existing)
        rating_ids.append(rating_id)

    # Keep Johan's live B-Hunt ratings open; Thabo -> Johan is already done.
    johan_id = ids[account_by_key(accounts, "johan")["email"]]
    thabo_id = ids[account_by_key(accounts, "thabo")["email"]]
    vusi_id = ids[account_by_key(accounts, "vusi")["email"]]
    db.execute("DELETE FROM dbo.ratings WHERE bookingId=? AND raterUserId=? AND rateeUserId IN (?,?)", (bookings["b_hunt"], johan_id, thabo_id, vusi_id))
    if not db.scalar("SELECT ratingId FROM dbo.ratings WHERE bookingId=? AND raterUserId=? AND rateeUserId=?", (bookings["b_hunt"], thabo_id, johan_id)):
        _insert_id(db, "INSERT dbo.ratings(bookingId,raterUserId,rateeUserId,rateeRoleCode,stars,comment,createdAt,isExcluded) OUTPUT INSERTED.ratingId VALUES(?,?,?,'HUNTER',5,'Johan het veilig en verantwoord gejag.',?,0)", (bookings["b_hunt"], thabo_id, johan_id, demo_date - dt.timedelta(days=1)))

    # Five farms receive realistic aggregate history.
    for index, farm_id in enumerate(list(farm_ids.values())[:5]):
        booking_id = completed[index]
        rater = demo_users[index + 2]
        db.execute(
            """
            IF EXISTS(SELECT 1 FROM dbo.farmRatings WHERE bookingId=? AND raterUserId=? AND farmId=?)
                UPDATE dbo.farmRatings SET stars=?,description=?,updatedAt=? WHERE bookingId=? AND raterUserId=? AND farmId=?
            ELSE INSERT dbo.farmRatings(bookingId,farmId,raterUserId,stars,description,createdAt,updatedAt) VALUES(?,?,?,?,?,?,?);
            """,
            (booking_id, rater, farm_id, 4 + index % 2, comments[index], demo_date,
             booking_id, rater, farm_id, booking_id, farm_id, rater, 4 + index % 2, comments[index], demo_date - dt.timedelta(days=20 + index), demo_date - dt.timedelta(days=20 + index)),
        )

    # One flagged rating plus one open dispute.
    flagged_rating = rating_ids[-1]
    rating_row = db.row("SELECT raterUserId,rateeUserId,comment FROM dbo.ratings WHERE ratingId=?", (flagged_rating,))
    db.execute("DELETE FROM dbo.flaggedRatingComments WHERE frcRatingId=?", (flagged_rating,))
    db.execute("INSERT dbo.flaggedRatingComments(frcRatingId,frcRaterUserId,frcRateeUserId,frcCommentText,frcFlaggedKeywords,frcCreatedAt,frcStatus) VALUES(?,?,?,?,?,?,'PendingReview')", (flagged_rating, int(rating_row[0]), int(rating_row[1]), str(rating_row[2]), "onvanpaste taal", demo_date - dt.timedelta(days=2)))
    disputed_rating = rating_ids[-2]
    disputed_by = int(db.scalar("SELECT rateeUserId FROM dbo.ratings WHERE ratingId=?", (disputed_rating,)))
    db.execute("DELETE FROM dbo.ratingDisputes WHERE ratingId=?", (disputed_rating,))
    db.execute("INSERT dbo.ratingDisputes(ratingId,disputedByUserId,reason,status,createdAt) VALUES(?,?,'Die kommentaar beskryf nie die bespreking akkuraat nie.','PendingReview',?)", (disputed_rating, disputed_by, demo_date - dt.timedelta(days=1)))

    species = TROPHY_SPECIES
    # Trophies belong to hunters (only a hunter may upload a trophy photo); Johan owns trophy_01.
    hunters = [ids[account["email"].lower()] for account in accounts
               if "HUNTER" in roles.get(account["key"], {}) and account["email"].lower() in ids]
    johan_id = ids[account_by_key(accounts, "johan")["email"].lower()]
    hunters = [johan_id] + [user for user in hunters if user != johan_id]
    for index in range(20):
        key = f"trophy_{index + 1:02d}"
        trophy_id = _registry_get(db, "trophy", key)
        owner = hunters[index % len(hunters)]
        values = (owner, species[index % len(species)], f"demo/trophies/{key}.jpg", 132 if index == 0 else 82 + index, 180 + index * 3, ".308 Winchester", demo_date - dt.timedelta(days=2 + index * 7), FARMS[index % len(FARMS)][1], "Eties geoes en volgens plaasreëls aangeteken.", 1 if index < 12 else 0, 165 + index * 4, "southern_greater_kudu" if index == 0 else None, "Koedoe" if index == 0 else None)
        if trophy_id is None or not db.scalar("SELECT COUNT(*) FROM dbo.trophies WHERE trophyId=?", (trophy_id,)):
            trophy_id = _insert_id(db, "INSERT dbo.trophies(tOwnerUserId,tSpecies,tPhotoPath,tAntlerSize,tCarcassWeight,tFirearmType,tHarvestDate,tLocationText,tNotes,tIsPublic,tShotDistanceMeters,tRowlandWardClassId,tRowlandWardSpeciesNameAf,tApprovalStatus) OUTPUT INSERTED.trophyId VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,'Approved')", values)
        else:
            # A photo uploaded by an earlier run stays, as long as it is stored under this owner.
            values = values[:2] + (db.scalar("SELECT CASE WHEN tPhotoPath LIKE ? THEN tPhotoPath ELSE ? END FROM dbo.trophies WHERE trophyId=?", (f"/uploads/trophies/{owner}/%", values[2], trophy_id)),) + values[3:]
            db.execute("UPDATE dbo.trophies SET tOwnerUserId=?,tSpecies=?,tPhotoPath=?,tAntlerSize=?,tCarcassWeight=?,tFirearmType=?,tHarvestDate=?,tLocationText=?,tNotes=?,tIsPublic=?,tShotDistanceMeters=?,tRowlandWardClassId=?,tRowlandWardSpeciesNameAf=?,tApprovalStatus='Approved' WHERE trophyId=?", values + (trophy_id,))
        _registry_set(db, "trophy", key, trophy_id, demo_date)

    approved_trophy = _registry_get(db, "trophy", "trophy_01")
    main_submission = _registry_get(db, "submission", "b_hunt_approved")
    submission_created = demo_date - dt.timedelta(days=2)
    submission_values = (bookings["b_hunt"], johan_id, roles["johan"]["HUNTER"], "Koedoe", "demo/trophies/kudu.jpg", 132, demo_date - dt.timedelta(days=3), "Kameeldoring Wildplaas", 186, ".308 Winchester", "Rowland Ward-evaluering voltooi.", "[]", json.dumps({"classId": "southern_greater_kudu"}), approved_trophy, submission_created, submission_created)
    if main_submission is None or not db.scalar("SELECT COUNT(*) FROM dbo.trophySubmissions WHERE submissionId=?", (main_submission,)):
        main_submission = _insert_id(db, "INSERT dbo.trophySubmissions(bookingId,hunterUserId,hunterUserRoleId,species,trophyPhotoStorageReference,antlerSizeCm,harvestDate,locationText,shotDistanceMeters,firearmType,notes,supportingMediaIdsJson,rowlandWardRequestJson,status,approvedTrophyId,createdAt,updatedAt) OUTPUT INSERTED.submissionId VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,'Approved',?,?,?)", submission_values)
    else:
        db.execute("UPDATE dbo.trophySubmissions SET status='Approved',approvedTrophyId=?,reviewNotes='Goedgekeur vir die trofeekamer.',updatedAt=? WHERE submissionId=?", (approved_trophy, demo_date - dt.timedelta(days=1), main_submission))
    _registry_set(db, "submission", "b_hunt_approved", main_submission, demo_date)
    if db.column_exists("trophies", "tSourceSubmissionId"):
        db.execute("UPDATE dbo.trophies SET tSourceSubmissionId=? WHERE trophyId=?", (main_submission, approved_trophy))
    correction = _registry_get(db, "submission", "b_hunt_correction")
    if correction is None or not db.scalar("SELECT COUNT(*) FROM dbo.trophySubmissions WHERE submissionId=?", (correction,)):
        correction = _insert_id(db, "INSERT dbo.trophySubmissions(bookingId,hunterUserId,hunterUserRoleId,species,trophyPhotoStorageReference,antlerSizeCm,harvestDate,locationText,shotDistanceMeters,firearmType,notes,supportingMediaIdsJson,status,reviewNotes,createdAt,updatedAt) OUTPUT INSERTED.submissionId VALUES(?,?,?,?,?,?,?,?,?,?,?,'[]','CorrectionRequested','Laai asseblief ''n duideliker syfoto met die maatband sigbaar.',?,?)", (bookings["b_hunt"], johan_id, roles["johan"]["HUNTER"], "Rooibok", "demo/trophies/rooibok-correction.jpg", 58, demo_date - dt.timedelta(days=3), "Kameeldoring Wildplaas", 142, ".308 Winchester", "Wag vir regstelling.", demo_date - dt.timedelta(days=2), demo_date - dt.timedelta(days=1)))
    else:
        db.execute("UPDATE dbo.trophySubmissions SET status='CorrectionRequested',reviewNotes='Laai asseblief ''n duideliker syfoto met die maatband sigbaar.',approvedTrophyId=NULL,updatedAt=? WHERE submissionId=?", (demo_date - dt.timedelta(days=1), correction))
    _registry_set(db, "submission", "b_hunt_correction", correction, demo_date)


def _seed_ads_notifications_and_permits(
    db: Database,
    accounts: list[dict[str, Any]],
    roles: dict[str, dict[str, int]],
    demo_date: dt.date,
) -> None:
    ids = user_ids(db)
    advertisers = [person for person in accounts if "AP" in person["roles"]]
    ad_specs = [
        ("Karoo Optics veldverkyker", "Optika", "Active", 12900, 2400, 183),
        ("Karoo Optics winteraanbod", "Optika", "PendingPayment", 8900, 0, 0),
        ("Bosveld Taksidermie", "Taksidermie", "Active", 6800, 1530, 112),
        ("Kalahari Lodge", "Verblyf", "Active", 2100, 980, 77),
        ("Veldspoor Jagdrag", "Toerusting", "Active", 3200, 1750, 136),
        ("Wildvang SA", "Wildbestuur", "Active", 7500, 620, 48),
        ("Safari-kluise", "Toerusting", "Active", 5400, 890, 65),
        ("Oesfees 2025", "Geleenthede", "Expired", 1200, 410, 22),
    ]
    karoo = next((p for p in advertisers if p["email"].lower().startswith("bemarking@")), advertisers[0])
    # adStatus holds the app's Afrikaans values; the English names above are only for readability.
    db_status = {"Active": "Aktief", "Expired": "Verval", "PendingPayment": "Pending Payment"}
    for index, (title, category, status, price, views, clicks) in enumerate(ad_specs):
        # The scenario's "Karoo Optics" adverts (paid and unpaid) belong to Karoo Optics; the rest rotate.
        person = karoo if title.startswith("Karoo Optics") else advertisers[index % len(advertisers)]
        ap_id = int(db.scalar("SELECT apId FROM dbo.ap WHERE apUserRoleId=?", (roles[person["key"]]["AP"],)))
        existing = db.scalar("SELECT advertisementId FROM dbo.advertisements WHERE adApId=? AND adTitle=?", (ap_id, title))
        picture = ASSETS_DIR / "ads" / AD_IMAGES[title]
        ad_images = json.dumps([_data_url(picture)]) if picture.exists() else "[]"
        expires = demo_date + dt.timedelta(days=90) if status == "Active" else demo_date - dt.timedelta(days=15) if status == "Expired" else demo_date + dt.timedelta(days=30)
        if existing is None:
            ad_id = _insert_id(db, "INSERT dbo.advertisements(adApId,adTitle,adShortDescription,adLongDescription,adType,adCategory,adStatus,adPrice,adCreatedTimestamp,adExpirationTimestamp,adContactNumber,adViews,adClicks,adImagesJson,adTargetUrl,adPaymentRef,adLastPaymentMonths,adLastPaymentAmount,adLastPaymentDate,adAdvertiserName) OUTPUT INSERTED.advertisementId VALUES(?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?)", (ap_id, title, "Praktiese veldproduk vir Suid-Afrikaanse jagters.", "Plaaslike diens, eerlike pryse en landwye ondersteuning.", "Marketplace", category, db_status[status], price, demo_date - dt.timedelta(days=45), expires, person["phone"], views, clicks, ad_images, "https://example.invalid/demo", f"DEMO-AD-{index + 1}" if status == "Active" else None, 3 if status == "Active" else None, 300 if status == "Active" else None, demo_date - dt.timedelta(days=15) if status == "Active" else None, person.get("business_name", f"{person['first_name']} {person['surname']}") ))
        else:
            ad_id = int(existing)
            db.execute("UPDATE dbo.advertisements SET adStatus=?,adExpirationTimestamp=?,adViews=?,adClicks=?,adImagesJson=? WHERE advertisementId=?", (db_status[status], expires, views, clicks, ad_images, ad_id))
        if status == "Active" and not db.scalar("SELECT COUNT(*) FROM dbo.adPayments WHERE adId=?", (ad_id,)):
            db.execute("INSERT dbo.adPayments(adId,paymentRef,paymentDate,months,amount,periodStartDate,periodEndDate,paymentMethod) VALUES(?,?,?,?,?,?,?,'Simulator')", (ad_id, f"DEMO-AD-{index + 1}", demo_date - dt.timedelta(days=15), 3, 300, demo_date - dt.timedelta(days=15), demo_date + dt.timedelta(days=75)))

    demo_users = list(ids.values())
    sender = ids[account_by_key(accounts, "pieter")["email"]]
    existing_notifications = int(db.scalar("SELECT COUNT(*) FROM dbo.userNotifications n JOIN dbo.users u ON u.userId=n.recipientUserId WHERE LOWER(u.uEmail) LIKE ? AND n.notificationType LIKE 'Demo%'", (f"%@{MARKER_DOMAIN}",), 0))
    if existing_notifications < 120:
        db.execute("DELETE n FROM dbo.userNotifications n JOIN dbo.users u ON u.userId=n.recipientUserId WHERE LOWER(u.uEmail) LIKE ? AND n.notificationType LIKE 'Demo%'", (f"%@{MARKER_DOMAIN}",))
        for index in range(120):
            recipient = demo_users[index % len(demo_users)]
            created = dt.datetime.combine(demo_date - dt.timedelta(days=index % 45), dt.time(8 + index % 10, index % 60))
            read = index % 3 != 0
            db.execute("INSERT dbo.userNotifications(recipientUserId,senderUserId,notificationType,title,message,actionUrl,isRead,createdTimestamp,readTimestamp) VALUES(?,?,'DemoAktiwiteit',?,?,?, ?,?,?)", (recipient, sender, "MikPunt-aktiwiteit", "Daar is nuwe aktiwiteit op jou profiel of bespreking.", "/notifications", int(read), created, created + dt.timedelta(hours=2) if read else None))

    johan_id = ids[account_by_key(accounts, "johan")["email"]]
    db.execute("DELETE FROM dbo.permits WHERE UserId=? AND PermitNumber LIKE 'DEMO-%'", (johan_id,))
    db.execute("INSERT dbo.permits(UserId,PermitNumber,PermitType,IssueDate,ExpiryDate,DocumentUrl,Province) VALUES(?, 'DEMO-LP-26041','Jagpermit',?,?, 'demo/permits/limpopo.pdf','Limpopo'),(?, 'DEMO-MP-26017','Jagpermit',?,?, 'demo/permits/mpumalanga.pdf','Mpumalanga')", (johan_id, demo_date - dt.timedelta(days=120), demo_date + dt.timedelta(days=245), johan_id, demo_date - dt.timedelta(days=50), demo_date + dt.timedelta(days=18)))
    hunter_role = roles["johan"]["HUNTER"]
    for number, status in enumerate(("Approved", "Pending"), start=1):
        type_name = f"SAPS 520 Demo {number}"
        document_type = db.scalar("SELECT dtId FROM dbo.documentType WHERE dtName=?", (type_name,))
        if document_type is None:
            document_type = _insert_id(db, "INSERT dbo.documentType(dtName,dtNumber) OUTPUT INSERTED.dtId VALUES(?,?)", (type_name, f"SAPS520-DEMO-{number}"))
        payload = json.dumps({"application": f"DEMO-SAPS-{number}", "rifles": [{"calibre": ".308 Winchester", "serial": "DEMO-NOT-VALID-308"}, {"calibre": ".375 H&H", "serial": "DEMO-NOT-VALID-375"}]})
        existing = db.scalar("SELECT udId FROM dbo.userDocumentation WHERE udUserRole=? AND udDtId=? AND udIsDeleted=0", (hunter_role, document_type))
        if existing is None:
            db.execute("INSERT dbo.userDocumentation(udUserRole,udName,udFilePath,udStatus,udUploadedTimestamp,udDtId,udUploadedBy,udIsDeleted,udVerifiedAt) VALUES(?,?,?,?,?,?,?,0,?)", (hunter_role, f"SAPS 520 - {number}", payload, status, demo_date - dt.timedelta(days=10 - number), document_type, johan_id, demo_date - dt.timedelta(days=5) if status == "Approved" else None))
        else:
            db.execute("UPDATE dbo.userDocumentation SET udFilePath=?,udStatus=?,udVerifiedAt=? WHERE udId=?", (payload, status, demo_date - dt.timedelta(days=5) if status == "Approved" else None, existing))


def _seed_documents_via_api(
    db: Database,
    api: ApiClient,
    config: DemoConfig,
    accounts: list[dict[str, Any]],
    roles: dict[str, dict[str, int]],
    demo_date: dt.date,
) -> None:
    tiny_pdf = base64.b64encode(b"%PDF-1.4\n1 0 obj<</Type/Catalog>>endobj\n%%EOF\n").decode("ascii")
    file_data = f"data:application/pdf;base64,{tiny_pdf}"
    admin_id = db.scalar(
        """
        SELECT TOP 1 u.userId FROM dbo.users u
        JOIN dbo.userRoles ur ON ur.urUserId=u.userId
        JOIN dbo.roles r ON r.roleId=ur.urRoleId WHERE UPPER(r.roleCode)='ADMIN'
        ORDER BY u.userId
        """
    )
    if admin_id is None:
        admin_id = user_ids(db)[account_by_key(accounts, "pieter")["email"]]

    for key, role in (("pieter", "PJ"), ("thabo", "PH"), ("vusi", "VG"), ("anna", "FARMER"), ("lerato", "PJ")):
        person = account_by_key(accounts, key)
        login = api.post("/api/user/signin", {"email": person["email"], "password": config.password}, expected=(200,))
        token = login.get("accessToken", "")
        if not token:
            raise DemoError(f"WebAPI did not return an access token for {person['email']}")
        requirements = api.request("GET", f"/api/documents/requirements/{role}", expected=(200,))
        user_role_id = roles[key][role]
        for requirement in requirements:
            type_id = int(requirement.get("documentTypeId", requirement.get("id", 0)))
            type_name = str(requirement.get("documentTypeName", requirement.get("name", "Dokument")))
            if not type_id:
                continue
            existing = db.scalar("SELECT udId FROM dbo.userDocumentation WHERE udUserRole=? AND udDtId=? AND udIsDeleted=0", (user_role_id, type_id))
            if existing is None:
                response = api.post(
                    f"/api/documents/{user_role_id}/upload",
                    {"documentTypeId": type_id, "fileName": f"{key}-{type_id}.pdf", "fileDataUrl": file_data},
                    token=token,
                    expected=(200,),
                )
                existing = int(response["id"])
            if key == "thabo" and not db.scalar("SELECT COUNT(*) FROM dbo.documentAuditLog WHERE documentId=? AND actionType='Rejected'", (existing,)):
                db.execute("UPDATE dbo.userDocumentation SET udStatus='Rejected',udRejectionReason='Die oorspronklike skandering was onleesbaar.',udVerifiedBy=?,udVerifiedAt=? WHERE udId=?", (admin_id, demo_date - dt.timedelta(days=40), existing))
                db.execute("INSERT dbo.documentAuditLog(documentId,userRoleId,actorUserId,actionType,documentTypeName,fileName,createdAt,notes) VALUES(?,?,?,'Rejected',?,?,?,'Die oorspronklike skandering was onleesbaar.')", (existing, user_role_id, admin_id, type_name, f"{key}-{type_id}.pdf", demo_date - dt.timedelta(days=40)))
                db.commit()
                api.post(
                    f"/api/documents/{user_role_id}/upload",
                    {"documentTypeId": type_id, "fileName": f"{key}-{type_id}-vervanging.pdf", "fileDataUrl": file_data},
                    token=token,
                    expected=(200,),
                )
            if key == "lerato":
                db.execute("UPDATE dbo.userDocumentation SET udStatus='Pending',udVerifiedBy=NULL,udVerifiedAt=NULL,udRejectionReason=NULL WHERE udId=?", (existing,))
            else:
                db.execute("UPDATE dbo.userDocumentation SET udStatus='Approved',udVerifiedBy=?,udVerifiedAt=?,udRejectionReason=NULL WHERE udId=?", (admin_id, demo_date - dt.timedelta(days=20), existing))
                if not db.scalar("SELECT COUNT(*) FROM dbo.documentAuditLog WHERE documentId=? AND actionType='Approved'", (existing,)):
                    db.execute("INSERT dbo.documentAuditLog(documentId,userRoleId,actorUserId,actionType,documentTypeName,fileName,createdAt,notes) VALUES(?,?,?,'Approved',?,?,?,'Dokument vir die demonstrasie goedgekeur.')", (existing, user_role_id, admin_id, type_name, f"{key}-{type_id}.pdf", demo_date - dt.timedelta(days=20)))
            # Commit before the next API call: the WebAPI would otherwise wait on these uncommitted rows.
            db.commit()
        db.commit()


def _hide_legacy_visible_data(db: Database) -> tuple[int, int]:
    farms = db.execute(
        """
        UPDATE f SET farmStatus='Hidden'
        FROM dbo.farm f
        LEFT JOIN dbo.farmer fa ON fa.farmerId=f.farmFarmerId
        LEFT JOIN dbo.userRoles ur ON ur.userRoleId=fa.fUserRoleId
        LEFT JOIN dbo.users u ON u.userId=ur.urUserId
        WHERE f.farmStatus IN('Active','ACTIVE','Aktief')
          AND LOWER(ISNULL(u.uEmail,'')) NOT LIKE ?
          AND (LOWER(ISNULL(u.uEmail,'')) LIKE '%@test.com' OR LOWER(f.farmName) LIKE '%e2e%' OR LOWER(f.farmName) LIKE '%toets%')
        """,
        (f"%@{MARKER_DOMAIN}",),
    ).rowcount
    packages = db.execute(
        """
        UPDATE p SET pStatus='Hidden'
        FROM dbo.packages p
        LEFT JOIN dbo.pj pj ON pj.pjId=p.pPjId
        LEFT JOIN dbo.userRoles ur ON ur.userRoleId=pj.pjUserRoleId
        LEFT JOIN dbo.users u ON u.userId=ur.urUserId
        WHERE p.pStatus IN('Active','ACTIVE','Aktief')
          AND LOWER(ISNULL(u.uEmail,'')) NOT LIKE ?
          AND (LOWER(ISNULL(u.uEmail,'')) LIKE '%@test.com' OR LOWER(p.pTitle) LIKE '%e2e%' OR LOWER(p.pTitle) LIKE '%toets%')
        """,
        (f"%@{MARKER_DOMAIN}",),
    ).rowcount
    return max(farms, 0), max(packages, 0)


def _print_plan(accounts: list[dict[str, Any]], demo_date: dt.date) -> None:
    role_counts: dict[str, int] = {}
    for person in accounts:
        for role in person["roles"]:
            role_counts[role] = role_counts.get(role, 0) + 1
    print(f"Demo date: {demo_date.isoformat()}")
    print(f"Accounts: {len(accounts)} ({', '.join(f'{k}={v}' for k, v in sorted(role_counts.items()))})")
    print("Farms: 8; packages: 16 (12 active, 2 hidden, 2 expired); bookings: 45")
    print("Ratings: 70; trophies: 20; chats: 12/~150 messages; ads: 8; notifications: 120")
    for ok, label in asset_report():
        print(f"Assets {'ready' if ok else 'pending'}: {label}")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    add_common_arguments(parser, mutating=True)
    parser.add_argument("--keep-legacy-visible", action="store_true", help="Do not hide obvious E2E/Toets/test.com farms and packages.")
    parser.add_argument("--skip-document-api", action="store_true", help="Skip core document upload/replace calls (useful only for diagnosing an API outage).")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        cast = load_cast(args.cast)
        accounts = all_accounts(cast)
        if args.dry_run:
            _print_plan(accounts, args.demo_date)
            return 0
        config = DemoConfig.from_env(require_password=True)
        _validate_password(config.password)
        api = ApiClient(config.web_api_url, allow_insecure_tls=config.allow_insecure_tls, timeout=config.request_timeout_seconds)
        mobile_api = ApiClient(config.mobile_api_url, allow_insecure_tls=config.allow_insecure_tls, timeout=config.request_timeout_seconds)
        with Database(config.sql_connection_string) as db:
            require_schema(db)
            ensure_runtime_tables(db)
            _ensure_registry(db)
            db.commit()
            _ensure_accounts(db, api, accounts, config.password)
            roles = _seed_profiles(db, accounts, args.demo_date)
            _seed_background_documents(db, accounts, roles, args.demo_date)
            farm_ids = _seed_farms(db, accounts, roles, args.demo_date)
            package_ids = _seed_packages(db, roles, farm_ids, args.demo_date)
            _seed_team_and_vg_packages(db, roles, args.demo_date)
            bookings = _seed_bookings(db, accounts, roles, package_ids, args.demo_date)
            _seed_route_media_and_recap(db, accounts, roles, bookings, args.demo_date)
            _seed_chats(db, accounts, bookings, args.demo_date)
            _seed_ratings_trophies_and_moderation(db, accounts, roles, bookings, farm_ids, args.demo_date)
            _seed_ads_notifications_and_permits(db, accounts, roles, args.demo_date)
            hidden = (0, 0) if args.keep_legacy_visible else _hide_legacy_visible_data(db)
            db.commit()
            if not args.skip_document_api:
                _seed_documents_via_api(db, api, config, accounts, roles, args.demo_date)
            db.commit()
            _seed_media_and_recap_via_api(db, mobile_api, config, accounts, roles, bookings, args.demo_date)
            db.commit()
            _seed_trophy_photos_via_api(db, mobile_api, config, accounts, roles)
            db.commit()
            print(f"Legacy fixtures hidden (not deleted): {hidden[0]} farm(s), {hidden[1]} package(s)")
        print("Demo seed completed. Run tools/demo/check_demo.py next.")
        return 0
    except DemoError as exc:
        return fail(str(exc))
    except KeyboardInterrupt:
        return fail("Interrupted; the current SQL transaction was rolled back.")


if __name__ == "__main__":
    raise SystemExit(main())
