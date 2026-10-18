"""Written content for the demo seed: farm descriptions, package details and advert pictures.

Kept apart from seed_demo.py so the wording can be edited without touching the seeding logic.
Package keys are the package titles in seed_demo.PACKAGES.
"""

from __future__ import annotations

FARM_DESCRIPTIONS: dict[str, str] = {
    "kameeldoring": (
        "Kameeldoring Wildplaas lê in die hart van die Waterberg-biosfeer, 25 km buite Vaalwater. "
        "Die 2 400 ha gemengde bosveld met kameeldoring, rooibos en oop vleie dra gesonde troppe koedoe, "
        "rooibok, blouwildebees en eland. Die familie-lodge het ses en-suite kamers, 'n lapa met boma-vuur en "
        "'n afslagkamer met koelkamer."
    ),
    "rooibosrand": (
        "Rooibosrand Wildgoed strek oor 3 100 ha doringbosveld naby Lephalale, met die Mokolorivier as "
        "noordgrens. Digte rooibosruigtes en oop klipkoppies maak dit ideaal vir bekruipjag op vlakvark, "
        "rooibok en koedoe. Agt kamers in twee grasdak-chalets, 'n swembad en 'n skietbaan van 200 m."
    ),
    "moloporivier": (
        "Moloporivier Jagplaas is 5 200 ha Kalahari-savanne langs die droë Molopo-loop suid van Vryburg. "
        "Boogjagters sit in vier verskuilings by standhoudende watergate; geweerjag gebeur op die oop vlaktes. "
        "Eenvoudige, gemaklike verblyf vir vyf gaste; self-sorg met 'n volledig toegeruste kombuis."
    ),
    "sneeuberg": (
        "Sneeuberg Conservancy beslaan 6 800 ha berggrasveld en Karoo-kranse bokant Graaff-Reinet, tot "
        "1 900 m bo seevlak. Bekend vir uitdagende bergjag op koedoe en vaal ribbok, en vir springbok op die "
        "hoë vlaktes. Die gerestoureerde opstal van 1890 slaap tien gaste."
    ),
    "vlaktesig": (
        "Vlaktesig Wildlandgoed is 4 100 ha oop Vrystaatse grasveld oos van Bloemfontein. Die plaas bestuur "
        "groot troppe blesbok, swartwildebees en springbok, en bied jaarlikse uitdunningsjagte vir groepe. "
        "Plaasstyl-verblyf vir vier in die ou waenhuis."
    ),
    "kameelpan": (
        "Kameelpan Kalahari lê tussen rooi duine en ou panne 60 km noord van Upington. Die 8 900 ha is tuiste "
        "van gemsbok, springbok en rooihartbees, en die nagte is stil en sterreryk. Die lodge het sewe kamers, "
        "'n kampvuurkring en 'n uitkykdek oor die pan."
    ),
    "umfolozi": (
        "Umfolozi Heuwels grens aan die Hluhluwe-iMfolozi-gebied in KwaZulu-Natal. Die 3 600 ha doringveld en "
        "riviervlakte het van die beste njala-bevolkings in die land, asook bosbok en rooibok. Nege kamers met "
        "uitsig oor die Swart Umfolozi."
    ),
    "komatibos": (
        "Komatibos Safaris is 4 700 ha Laeveld tussen Komatipoort en die Krugerwildtuin, met buffel, koedoe, "
        "waterbok en njala. 'n Gevaarlike-wild-PH is op die plaas gebaseer. Agt luukse tente met buitestorte, "
        "'n boma en 'n afslagkamer wat aan uitvoerstandaarde voldoen."
    ),
}

_STANDARD_EXCLUSIONS = [
    "Trofeefooie per geskiete dier (volgens die plaas se pryslys)",
    "Taksidermie, verpakking en uitvoer van trofeë",
    "Vlugte en vervoer na en van die plaas",
    "Alkoholiese drank en fooitjies vir personeel",
]

# title -> (description, inclusions, exclusions, observer price per day "vanaf")
PACKAGE_DETAILS: dict[str, tuple[str, list[str], list[str], int]] = {
    "Waterberg Koedoe-safari, 4 dae": (
        "'n Klassieke koedoejag in die Waterberg-bosveld op Kameeldoring Wildplaas. Jy bekruip koedoebulle in die "
        "vroeë oggend langs die rante en sit smiddae by die watergate. Die PH help met trofee-evaluering volgens "
        "Rowland Ward, en die span hanteer die afslag en soutkamer.\n\n"
        "Dag 1: Aankoms teen 14:00, skietbaan-toets en aandete by die boma.\n"
        "Dag 2-3: Oggend- en middagjag; middagrus by die lodge.\n"
        "Dag 4: Laaste oggendjag, trofeë na die afslagkamer en vertrek teen 12:00.",
        [
            "Professionele jagter (PH) en spoorsnyer, 1 jagter : 1 PH",
            "3 nagte verblyf in 'n en-suite kamer, drie etes per dag",
            "Een koedoebul tot 132 cm (trofeefooi ingesluit)",
            "Veldvoertuig, brandstof en vervoer op die plaas",
            "Afslag, soutkamer en verkoeling van die karkas",
            "Rowland Ward-meting deur die PH",
        ],
        _STANDARD_EXCLUSIONS[1:] + ["Bykomende diere buite die koedoe"],
        950,
    ),
    "Waterberg Familie-vlaktewild, 5 dae": (
        "Bring die gesin saam: terwyl twee jagters vlaktewild jag, verken die nie-jagters die Waterberg met "
        "staproetes, perdry en 'n aand-wildrit. Ideaal vir 'n eerste jag met 'n ervare PH wat elke stap verduidelik.\n\n"
        "Dag 1: Aankoms, veiligheidsinligting en skietbaan.\n"
        "Dag 2-4: Jag op blouwildebees, rooibok en blesbok; gesinsaktiwiteite vir nie-jagters.\n"
        "Dag 5: Braai-ontbyt en vertrek.",
        [
            "PH vir twee jagters en 'n spoorsnyer",
            "4 nagte verblyf vir tot 6 gaste, volle losies",
            "Een rooibok per jagter (trofeefooi ingesluit)",
            "Staproete, perdry en een aand-wildrit vir nie-jagters",
            "Veldvoertuig en vervoer op die plaas",
            "Afslag, slagwerk en vakuumverpakking van vleis",
        ],
        _STANDARD_EXCLUSIONS + ["Blouwildebees en blesbok (betaal per dier)"],
        700,
    ),
    "Rooibok en vlakvark naweek": (
        "'n Kort, bekostigbare naweekjag in die Lephalale-bosveld. Perfek om 'n nuwe geweer in te jag of om "
        "vleis vir die vrieskas te kry.\n\n"
        "Vrydag: Aankoms teen 15:00, skietbaan en aandjag.\n"
        "Saterdag: Oggend- en middagjag.\n"
        "Sondag: Oggendjag en vertrek teen 13:00.",
        [
            "Gids en spoorsnyer",
            "2 nagte verblyf in 'n chalet, ontbyt en aandete",
            "Twee rooibokke en een vlakvark (trofeefooie ingesluit)",
            "Afslag en verkoeling",
        ],
        ["Middagetes", "Vervoer na die plaas", "Bykomende diere", "Alkoholiese drank"],
        450,
    ),
    "Bosveld gemengde sak, 6 dae": (
        "Ses dae op Rooibosrand vir die jagter wat 'n verskeidenheid wil jag: koedoe, blouwildebees, rooibok, "
        "vlakvark en eland. Die PH beplan die jag rondom jou wenslys en die plaas se kwotas.\n\n"
        "Dag 1: Aankoms en skietbaan.\n"
        "Dag 2-5: Jag volgens die wenslys; een middag by die Mokolorivier.\n"
        "Dag 6: Vertrek ná ontbyt.",
        [
            "PH en spoorsnyer, 1 jagter : 1 PH",
            "5 nagte volle losies",
            "Trofeefooie vir 'n rooibok en 'n vlakvark",
            "Veldvoertuig en brandstof",
            "Afslag, soutkamer en verkoeling",
            "Wasgoed elke tweede dag",
        ],
        _STANDARD_EXCLUSIONS + ["Koedoe, blouwildebees en eland (betaal per dier)"],
        900,
    ),
    "Molopo boogjag, 5 dae": (
        "Boogjag vanuit vier verskuilings by standhoudende watergate langs die Molopo. Die meeste skote is tussen "
        "15 en 25 m. Die PH help met jou opstelling en boogtoerusting.\n\n"
        "Dag 1: Aankoms en oefen op die 3D-teikens.\n"
        "Dag 2-4: Heeldag in die verskuiling (kospakkie ingesluit).\n"
        "Dag 5: Oggendsitting en vertrek.",
        [
            "Boogjag-PH en spoorsnyer met spoorhond",
            "4 nagte self-sorg-verblyf met basiese kruideniersware",
            "Vier verskuilings met kameras by die watergate",
            "Kospakkies vir die verskuiling",
            "Afslag en verkoeling",
        ],
        ["Pyle en breekpunte", "Alle trofeefooie", "Vervoer na die plaas", "Alkoholiese drank"],
        500,
    ),
    "Kalahari gemsbok-ekspedisie": (
        "Jag gemsbok soos dit hoort: bekruip oor die rooi duine van Kameelpan, met lang skote oor oop panne. "
        "Die nagte is vir sterrekyk by die kampvuur.\n\n"
        "Dag 1: Aankoms in Upington, oordrag na die plaas (2 uur).\n"
        "Dag 2-5: Duinjag op gemsbok en springbok.\n"
        "Dag 6: Oordrag terug na Upington.",
        [
            "PH en Boesman-spoorsnyer",
            "5 nagte volle losies in die lodge",
            "Een gemsbok (trofeefooi ingesluit)",
            "Oordrag van en na Upington-lughawe",
            "4x4-veldvoertuig en brandstof",
            "Afslag, soutkamer en verkoeling",
        ],
        ["Springbok en rooihartbees (betaal per dier)", "Vlugte na Upington", "Taksidermie en uitvoer", "Alkoholiese drank"],
        1100,
    ),
    "Sneeuberg koedoe en springbok": (
        "Bergjag vir fikse jagters in die Sneeuberge: koedoe in die kranse en springbok op die hoë vlaktes. "
        "Reken op 8-12 km stap per dag en skote tot 250 m.\n\n"
        "Dag 1: Aankoms by die opstal en skietbaan op 200 m.\n"
        "Dag 2-4: Bergjag met 'n middagrus in die opstal.\n"
        "Dag 5: Vertrek ná ontbyt.",
        [
            "PH en spoorsnyer",
            "4 nagte volle losies in die opstal van 1890",
            "Een springbok (trofeefooi ingesluit)",
            "4x4-vervoer na die bergpaaie",
            "Afslag en verkoeling",
        ],
        _STANDARD_EXCLUSIONS + ["Koedoe (betaal per dier)"],
        950,
    ),
    "Vrystaat blesbok-uitdunning": (
        "'n Groepjag om die plaas se blesbokbevolking te bestuur. Pryse is per jagter vir drie dae; vleis kan "
        "teen 'n klein fooi verwerk word.\n\n"
        "Dag 1: Aankoms en groepsinligting.\n"
        "Dag 2: Uitdunning op die vlaktes.\n"
        "Dag 3: Oggendjag en vertrek.",
        [
            "Jaggids vir elke twee jagters",
            "2 nagte verblyf en drie etes per dag",
            "Vier blesbokke per jagter (uitdunningsdiere)",
            "Vervoer op die plaas",
            "Afslag en koelkamer",
        ],
        ["Trofeediere", "Vleisverwerking (per kilogram)", "Vervoer na die plaas", "Alkoholiese drank"],
        400,
    ),
    "Umfolozi njala-ervaring": (
        "Die njala is een van Afrika se mooiste bokke. Op Umfolozi Heuwels jag jy hulle in digte riviervegetasie "
        "langs die Swart Umfolozi, met een middag op 'n fotosafari in die naburige park.\n\n"
        "Dag 1: Aankoms en skietbaan.\n"
        "Dag 2-5: Njala- en bosbokjag; een middag fotosafari.\n"
        "Dag 6: Vertrek.",
        [
            "PH en Zoeloe-spoorsnyer",
            "5 nagte volle losies",
            "Een njalabul (trofeefooi ingesluit)",
            "Halfdag-fotosafari in Hluhluwe-iMfolozi (parkfooie ingesluit)",
            "Afslag, soutkamer en verkoeling",
        ],
        _STANDARD_EXCLUSIONS + ["Bosbok (betaal per dier)"],
        1200,
    ),
    "Komatibos Laeveld-safari": (
        "'n Laeveld-safari met die Krugerwildtuin as buurman. Jag koedoe, waterbok en rooibok in gemengde bosveld, "
        "en slaap in luukse tente.\n\n"
        "Dag 1: Aankoms en skietbaan.\n"
        "Dag 2-4: Jag; aande by die boma.\n"
        "Dag 5: Vertrek ná ontbyt.",
        [
            "PH en spoorsnyer",
            "4 nagte in 'n luukse tent, volle losies",
            "Een rooibok (trofeefooi ingesluit)",
            "Veldvoertuig en brandstof",
            "Afslag en verkoeling volgens uitvoerstandaarde",
        ],
        _STANDARD_EXCLUSIONS + ["Koedoe en waterbok (betaal per dier)"],
        1100,
    ),
    "Kalahari familiepakket": (
        "Vier dae in die Kalahari vir 'n jagter met familie of vriende. Een springbok vir die jagter, en vir die "
        "res sandduin-planking, sterrekyk en 'n besoek aan 'n meerkat-kolonie.\n\n"
        "Dag 1: Aankoms en sonsondergang op die duine.\n"
        "Dag 2-3: Oggendjag; middagaktiwiteite vir die gesin.\n"
        "Dag 4: Vertrek.",
        [
            "Jaggids vir een jagter",
            "3 nagte volle losies vir tot 5 gaste",
            "Een springbok (trofeefooi ingesluit)",
            "Sandduin-planking, sterrekyk en meerkat-besoek",
        ],
        ["Bykomende diere", "Vervoer na die plaas", "Alkoholiese drank"],
        800,
    ),
    "Buffel-safari met gevaarlike-wild PH": (
        "Sewe dae op Komatibos vir 'n Kaapse buffelbul, saam met 'n gelisensieerde gevaarlike-wild-PH en 'n "
        "rugsteungeweer. Minimum kaliber .375 H&H. Slegs vir ervare jagters.\n\n"
        "Dag 1: Aankoms, skietbaan met jou buffelgeweer en veiligheidsinligting.\n"
        "Dag 2-6: Spoorsny en bekruip; buffels word te voet gevolg.\n"
        "Dag 7: Vertrek.",
        [
            "Gevaarlike-wild-PH, rugsteungeweer en twee spoorsnyers",
            "6 nagte in 'n luukse tent, volle losies",
            "Een buffelbul (trofeefooi ingesluit)",
            "Veldvoertuig en brandstof",
            "Afslag, soutkamer en veeartsenykundige dokumente vir uitvoer",
        ],
        ["Uitvoerpermitte (CITES waar nodig) en verskeping", "Vuurwapen-invoerpermit (SAPS 520) vir buitelanders",
         "Vlugte en vervoer na die plaas", "Alkoholiese drank en fooitjies"],
        1500,
    ),
    "Limpopo beginnersjag": (
        "'n Eerste jag vir nuwe jagters: 'n halwe dag skietopleiding, wildkennis en etiese jag, gevolg deur 'n "
        "begeleide jag op rooibok.\n\n"
        "Dag 1: Opleiding en skietbaan.\n"
        "Dag 2-3: Begeleide jag.\n"
        "Dag 4: Afslag-demonstrasie en vertrek.",
        [
            "Instrukteur en gids",
            "3 nagte volle losies",
            "Huurgeweer en ammunisie vir die opleiding",
            "Een rooibok (trofeefooi ingesluit)",
        ],
        ["Bykomende diere", "Vervoer na die plaas", "Alkoholiese drank"],
        500,
    ),
    "Waterberg jongjagter-pakket": (
        "Vir jagters van 12 tot 17 saam met 'n ouer. Klem op veiligheid, wildkennis en 'n eerste skoot onder "
        "toesig.\n\n"
        "Dag 1: Veiligheid en skietbaan.\n"
        "Dag 2: Begeleide jag.\n"
        "Dag 3: Vertrek.",
        [
            "Gids vir die jongjagter en ouer",
            "2 nagte volle losies vir twee",
            "Een rooibok (trofeefooi ingesluit)",
            "Ammunisie vir die skietbaan",
        ],
        ["Bykomende diere", "Vervoer na die plaas"],
        400,
    ),
    "Vorige seisoen se Oos-Kaap jag": (
        "Verlede seisoen se bergjag in die Sneeuberge. Hierdie pakket is verval en word net vir die rekord bewaar.",
        ["PH en spoorsnyer", "3 nagte volle losies", "Een springbok"],
        _STANDARD_EXCLUSIONS,
        800,
    ),
    "Vorige Kalahari winterjag": (
        "Verlede winter se gemsbokjag op Kameelpan. Hierdie pakket is verval en word net vir die rekord bewaar.",
        ["PH en spoorsnyer", "4 nagte volle losies", "Een gemsbok"],
        _STANDARD_EXCLUSIONS,
        900,
    ),
}

# advert title -> picture under tools/demo/assets/ads/
AD_IMAGES: dict[str, str] = {
    "Karoo Optics veldverkyker": "karoo-optics-veldverkyker.jpg",
    "Karoo Optics winteraanbod": "karoo-optics-winteraanbod.jpg",
    "Bosveld Taksidermie": "bosveld-taksidermie.jpg",
    "Kalahari Lodge": "kalahari-lodge.jpg",
    "Veldspoor Jagdrag": "veldspoor-jagdrag.jpg",
    "Wildvang SA": "wildvang-sa.jpg",
    "Safari-kluise": "safari-kluise.jpg",
    "Oesfees 2025": "oesfees-2025.jpg",
}
