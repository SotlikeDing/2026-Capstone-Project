# Bruikbaarheidstudie: MikPunt (webwerf + app)

*Weergawe 2, 13 Oktober 2026. Gebaseer op die end-tot-end-toetse op die staging-bediener van 12–13 Oktober (vier toetsrondes). Weergawe 1 (12 Okt) se uitslae word as "voor" gewys, sodat die verbetering sigbaar is. Die tegniese foutverslag is in [`staging_toetsverslag.md`](staging_toetsverslag.md); oop besluite is in [`beslissings_nodig.md`](beslissings_nodig.md).*

---

## 1. Doel

Bepaal hoe maklik en suksesvol elke tipe gebruiker die kerntake op MikPunt kan voltooi:

- jagter, boer, uitruster (PJ), professionele jagter (PH), videograaf (VG), adverteerder (AP) en admin
- op die webwerf en in die mobiele app

Die studie wys ook waar gebruikers vasval, verward raak of 'n fout maak, en meet of die regstellings van 12–13 Oktober die ervaring werklik verbeter het.

## 2. Metode

| Aspek | Hoe dit gedoen is |
|---|---|
| **Omgewing** | Die regte staging-stelsel (Docker-gasheer + SQL Server), met dieselfde weergawe as `main_dev`. Rondte 1 op `86b07b95`; die laaste rondtes op `e9fd7ab2` (met al die regstellings). |
| **Toetsgebruikers** | Elke rondte skep 7 nuwe rekeninge deur die gewone registrasieproses: 2× jagter, boer, PJ, PH, VG en AP. Plus die bestaande admin. |
| **Taakscenario's** | 18 realistiese take oor die hele lewensiklus: registreer → dokumente → plaas/pakket → bespreking → betaling → jag → trofee → storie → gradering. Elke taak word end-tot-end uitgevoer met dieselfde versoeke wat die webwerf en app self stuur. |
| **Kognitiewe deurloop** | Elke taak is stap vir stap nagegaan. Weet die gebruiker wat om te doen? Sien hy die regte terugvoer? Verstaan hy die foutboodskap? |
| **Heuristiese evaluasie** | Die bevindinge is teen Nielsen se 10 bruikbaarheidsheuristieke gemeet. |
| **Skermdekking** | 88 bladsye word outomaties in 'n blaaier oopgemaak, anoniem en as elke rol. Daar word gekyk vir foutbanners, "Kon nie laai nie"-boodskappe, konsolefoute en verkeerde toegang. Skermkiekies is van die belangrikste bladsye geneem. |
| **Omvang** | ±1 090 outomatiese kontroles per rondte, nou deel van die repo (`tests/e2e`) en van die CI-pyplyn. |

> **Beperking:** daar was **geen regte eindgebruikers** by nie. Dit is 'n kundige deurloop plus 'n heuristiese evaluasie op grond van die toetsuitslae. Dit wys **waar** die probleme is en of hulle opgelos is, maar meet nie tevredenheid nie (soos 'n SUS-telling). Afdeling 8 beskryf die opvolgstudie met regte gebruikers.

## 3. Rolle en take: voor en ná die regstellings

Simbole: ✅ = maklik voltooi · ⚠️ = voltooi, maar met 'n bruikbaarheidsprobleem · ❌ = gebruiker kan nie voltooi nie

| # | Taak | Rol | 12 Okt | 13 Okt | Waarneming (13 Okt) |
|---|---|---|---|---|---|
| T1 | Registreer en verifieer e-pos | Almal | ⚠️ | ✅ | Kodes oorleef nou 'n ontplooiing. Ná 5 verkeerde pogings word die kode gesluit, met 'n duidelike boodskap. Validasie is in Afrikaans en spesifiek. |
| T2 | Teken in en uit | Almal | ✅ | ✅ | Uitteken beëindig nou ook die sessie op die bediener. 'n Geskorste gebruiker word dadelik uitgeskop. |
| T3 | Laai rol-dokumente op | PJ, PH, VG, boer | ❌ | ✅ | Oplaai, afkeuring met rede, vervanging en goedkeuring werk. Die databasisregstelling is nou in die kode, nie net op staging nie. |
| T4 | Skep 'n plaas met wild | Boer | ✅ | ✅ | Goeie validasie ("Beskryf die plaas in ten minste 20 karakters."). |
| T5 | Skep en publiseer 'n pakket | PJ | ⚠️ | ⚠️ | **Nog oop:** die pakket bly onsigbaar op die markplein totdat die admin die PJ se dokumente goedgekeur het, en die PJ kry steeds geen boodskap wat dit verduidelik nie. |
| T6 | Plaas 'n advertensie | AP | ✅ | ✅ | Gaan eers ná betaling lewendig. Korrek, en kan nie omseil word nie. |
| T7 | Soek en bespreek 'n jag | Jagter | ⚠️ | ✅ | Datums in die verlede, 'n einde voor die begin en 0 jagters word nou geweier, met 'n verstaanbare boodskap. |
| T8 | Ken PH/VG toe; personeel aanvaar | PJ, PH, VG | ✅ | ✅ | Duidelike statusse; weiering met rede gaan terug na die PJ. |
| T9 | Betaal vir 'n bespreking | Jagter | ❌ | ⚠️ | Die jagter kom nou by PayFast uit (die "Ongeldige terugkeer-adres" is weg). **Maar** op staging kan PayFast nie terugbevestig nie, so die bespreking word nie outomaties "Betaal" nie (besluit D2). |
| T10 | Voeg ekstra jagters by | Hoofjagter | ✅ | ✅ | Duidelike limiet. Ander jagters sien die lys sonder e-posadresse (privaatheid). |
| T11 | Klets met die span | Almal | ✅ | ✅ | Werk tussen app en webwerf. 'n Gesprek met 'n onbekende gebruiker gee nou 'n gewone "nie gevind nie" in plaas van 'n bedienerfout. |
| T12 | Nooi 'n PH na jou span | PJ, PH | ⚠️ | ✅ | Die ekspedisienaam en voorwaardes word nou gestoor en aan die PH gewys, sodat hy weet waarvoor hy ja sê. |
| T13 | Doen aansoek om permit / SAPS 520 | Jagter | ❌ | ✅ | Gestoor by die regte persoon; meer as een indiening werk. 'n Onvolledige vorm word met 'n duidelike rede geweier, en die webwerf wys nou die fout in plaas van altyd "sukses". Die permitbeursie laai. |
| T14 | Dien 'n trofee in; PH keur goed | Jagter, PH | ✅ | ✅ | Duidelike vloei: indien → regstelling → herindien → goedkeur → trofeekamer. |
| T15 | Laai foto's/grepe vir die jagstorie op | VG | ⚠️ | ⚠️ | Die webwerf-paneel werk nou, en die storiekaarte is in Afrikaans met SA-tyd. **Nog oop:** die klaar storie kan nie na YouTube gepubliseer word nie, want die kanaal moet herkoppel word (besluit D3). Die storie self word wel gemaak. |
| T16 | Gradeer die span ná die jag | Jagter, PH, VG | ⚠️ | ✅ | Gradering werk nou ook in die app. "Voltooide jagte" tel afgehandelde jagte reg. |
| T17 | Modereer, hanteer dispute, skors | Admin | ✅ | ✅ | 'n Admin kan homself nie meer per ongeluk skors nie. Regstellings verskyn in die bespreking se geskiedenis. |
| T18 | Herstel 'n vergete wagwoord | Almal | — | ⚠️ | Nuut getoets. Werk, en verklap nie of 'n e-posadres bestaan nie. Ná 3 e-posse in 15 minute (of 5 verkeerde wagwoorde in die app) kry die gebruiker *"Te veel pogings. Wag asseblief 'n rukkie en probeer weer."* Dit is goed vir veiligheid, maar "'n rukkie" sê nie **hoe lank** nie (15 minute). |

### Opsomming van taaksukses

| Uitslag | 12 Okt (17 take) | 13 Okt (18 take) |
|---|---|---|
| ✅ Maklik voltooi | 8 (47 %) | **14 (78 %)** |
| ⚠️ Voltooi met 'n probleem | 6 (35 %) | 4 (22 %) |
| ❌ Kan nie voltooi nie | 3 (18 %) | **0 (0 %)** |
| **Voltooibaar (✅ + ⚠️)** | 14 / 17 (82 %) | **18 / 18 (100 %)** |

Geen taak is meer geblokkeer nie. Die oorblywende ⚠️-take hang hoofsaaklik af van **opstelling buite die kode**: PayFast se terugbevestiging (D2), die YouTube-herkoppeling (D3), en een ontbrekende boodskap vir die PJ (T5).

## 4. Bevindinge per Nielsen-heuristiek (13 Okt)

| # | Heuristiek | Wat goed is | Oorblywende probleme | Erns | Was |
|---|---|---|---|---|---|
| 1 | **Sigbaarheid van stelselstatus** | Besprekingstatusse is duidelik; 'n afgehandelde jag wys nou "Afgehandel" en tel by "Voltooide jagte"; die storie wys vordering in %. | 'n PJ se pakket is onsigbaar sonder verduideliking (T5). | Medium | Hoog |
| 2 | **Ooreenstemming met die werklike wêreld** | Afrikaanse jagterminologie; die storiekaarte is nou Afrikaans met SA-tyd. | Pryse is in `$`/USD, ook vir plaaslike jagters (besluit D9). | Medium | Medium |
| 3 | **Gebruikerbeheer en vryheid** | 'n Jagter kan 'n versoek kanselleer solank dit nog nie goedgekeur is nie; uitteken werk volledig. | Ná goedkeuring of betaling: *"Kontak asseblief die uitruster"*. Die gebruiker weet nie wat die kanselleringsreëls is nie (besluit D6). | Medium | Hoog |
| 4 | **Konsekwentheid en standaarde** | Rolportale lyk en werk eenders; elke provinsie verskyn nou net een keer. | Enkele Engelse boodskappe kan nog deurglip in randgevalle. | Laag | Medium |
| 5 | **Foutvoorkoming** | Besprekingsdatums, SAPS 520-velde, self-skorsing, duplikaat-e-posse (nou ook in die databasis afgedwing): alles word vooraf gekeer. | Geen noemenswaardige probleme nie. | — | Hoog |
| 6 | **Herkenning eerder as herroeping** | Vorige graderings word gewys; vereiste dokumente word gelys; wenk "stel die tyd" vir media sonder tyd. | Die VG moet steeds onthou wanneer media sonder tyd geneem is. | Laag | Laag |
| 7 | **Buigsaamheid en doeltreffendheid** | App en webwerf deel klets, besprekings en graderings; veelvuldige oplaai. | Die markplein se blaai-indeling is nog nie met regte inhoud herbeoordeel nie. | Laag | Medium |
| 8 | **Estetiese en minimalistiese ontwerp** | Skoon, konsekwente uitleg. Al 88 bladsye laai sonder foutbanners of konsolefoute, ook ná die nuwe sekuriteitsopskrifte. | — | — | — |
| 9 | **Help gebruikers om foute te herken en reg te stel** | Boodskappe is spesifiek en in Afrikaans. Bedienerfoute wys nou 'n vriendelike boodskap en 'n verwysingsnommer in plaas van SQL-besonderhede. | *"Wag 'n rukkie"* (T18) moet sê hoe lank. | Laag | Hoog |
| 10 | **Hulp en dokumentasie** | Die rollegids, privaatheidsbeleid en kontakvorm werk. | Geen in-app hulp oor waarom dokumente nodig is of hoe lank goedkeuring neem nie. | Laag | Laag |

**Gemiddelde erns het gedaal van drie "Hoog"-heuristieke na geen.**

## 5. Mobiele app: spesifieke waarnemings

- **Positief:** die rolkeuse by aanmelding, die vermoëns per rol (net die VG sien "Voeg 'n kort videogreep by"), en die trofee-indiening is duidelik en vergewingsgesind. Gradering ná die jag werk nou ook in die app.
- **Veiligheid sonder wrywing:** ná 5 verkeerde wagwoorde word daardie e-posadres 15 minute gesluit. Ander gebruikers merk niks daarvan nie.
- **Steeds kritiek:** 'n **release-bou** van die app kan nie aan die bediener koppel nie, omdat HTTPS op die proxy nog nie werk nie (besluit D1). 'n Regte gebruiker wat die APK installeer, sien net 'n verbindingsfout. Dit is nou die **grootste enkele bruikbaarheidsrisiko** van die projek.

## 6. Bewys van die verbetering

| Maatstaf | 12 Okt | 13 Okt |
|---|---|---|
| Take sonder probleme (✅) | 47 % | 78 % |
| Geblokkeerde take (❌) | 3 | 0 |
| Heuristieke met erns "Hoog" | 4 | 0 |
| Bladsye wat sonder foute laai | nie apart gemeet nie | 88 / 88 (ook met die nuwe sekuriteitsopskrifte) |
| Endpoints sonder bedienerfout | nie almal (bv. advertensies en plase het gecrash) | 557 / 557 |
| Outomatiese kontroles | 1 087 in rondte 1; die mislukkings is in die toetsverslag gelys | 1 070 / 1 073 in rondte 6; die 3 was toetsfoute, nie appfoute nie |

## 7. Aanbevelings (in volgorde van impak op die gebruiker)

1. **HTTPS op die proxy** (D1). Sonder dit kan geen regte gebruiker die app gebruik nie.
2. **PayFast-terugbevestiging op staging** (D2). Of skakel die betaalsimulator aan, sodat 'n betaling die bespreking werklik "Betaal" maak.
3. **Sê vir die PJ/boer waarom sy pakket of plaas nog nie sigbaar is nie:** *"Nog nie sigbaar nie: wag op dokumentgoedkeuring (gewoonlik binne 1 werksdag)."* (T5)
4. **Herkoppel YouTube** (D3), sodat die jagstorie gepubliseer kan word.
5. **Sê hoe lank om te wag** by "Te veel pogings": *"Probeer weer oor 15 minute."* (T18)
6. **Kanselleringsbeleid** ná goedkeuring of betaling (D6), sigbaar op die besprekingsblad.
7. **Geldeenheid** (D9): wys ZAR vir plaaslike gebruikers, of beide.
8. **In-app hulp** by dokumente: waarom elke dokument nodig is en hoe lank goedkeuring neem.

## 8. Voorgestelde opvolgstudie met regte gebruikers

Om tevredenheid te meet, wat hierdie studie nie kon doen nie:

- **Deelnemers:** 5 gebruikers per hoofrol (jagter, PJ, PH/VG), verkieslik regte jagters en 'n boer. Volgens Nielsen vind 5 gebruikers ±85 % van die probleme.
- **Take:** T5, T7, T9, T12, T15, T16 en T18 hierbo, met 'n hardop-dink-protokol.
- **Metings:**
  - taaksukses
  - tyd per taak
  - aantal foute
  - **SUS-vraelys** (System Usability Scale, 10 vrae, in Afrikaans vertaal); teiken ≥ 68
- **Wanneer:** ná aanbevelings 1–3, sodat die toets nie op bekende blokkeerders vasval nie.
- **Herhaal** ná die volgende regstellings, en rapporteer die SUS **voor en ná**. Dit is sterk bewys vir die verslag.

## 9. Toetsuitslae waarop hierdie weergawe gebaseer is

| Rondte | Weergawe | Uitslag |
|---|---|---|
| 1 (12 Okt) | `86b07b95` | 1 087 kontroles; die mislukkings staan in `staging_toetsverslag.md`, en die meeste is hierbo reggestel |
| 3 (13 Okt) | `84af406b` | 973 / 974 (die een mislukking was 'n fout in die toets self) |
| 5 (13 Okt) | `e9fd7ab2` | 754 / 754 kontroles wat kon loop, insluitend al 88 bladsye. 7 toetsstelle het nie voltooi nie omdat die VPN-verbinding twee keer uitgeval het; die bediener was heeltyd gesond. |
| 6 (13 Okt) | `e9fd7ab2` | **1 070 / 1 073**, insluitend al 88 bladsye en 8/8 sleutelvloeie in die blaaier. Die 3 mislukkings was in die toetsharnas (die herprobeerlogika en die tempo van die admin-oproepe), nie in die app nie. Ná die regstelling slaag daardie kontroles ook. |

## 10. Gevolgtrekking

MikPunt se **kernontwerp is gebruikersvriendelik**:

- duidelike rolportale
- konsekwente uitleg
- goeie Afrikaanse validasieboodskappe
- sterk vloei vir trofeë, klets, span en gradering

Op 12 Oktober was die grootste probleme **opstel- en databasisfoute** wat hele take geblokkeer het: betaling, dokumente en permitte. Ná die regstellings van 13 Oktober kan **elke taak voltooi word**, en 78 % van die take verloop sonder enige probleem (was 47 %).

Wat oorbly, is grotendeels **buite die kode**: HTTPS vir die app, PayFast-bevestiging en YouTube-herkoppeling. Daarby kom twee klein boodskapverbeterings (T5 en T18). Met dié reggestel behoort ±95 % van die take sonder probleme te verloop. Daarna is die stelsel gereed vir 'n studie met regte gebruikers en 'n SUS-meting.
