# MikPunt-bruikbaarheidsdemo: een jag van begin tot einde — *gevind → reggestel*

**Totale tyd:** 13 minute · **Formaat:** een deurlopende scenario met kort rolwisselings
**Doel:** wys hoe die webwerf en app saamwerk, wat die studie gevind het, en hoe ons dit reggestel het.

*Weergawe 2 (13 Okt 2026). Gebou op die oorspronklike draaiboek, maar elke bevinding word nou getoon as "gevind" (skyfie of aanhaling) en "reggestel" (lewendig, met die demodata). Die data kom van `tools/demo/seed_demo.py` (demodatum 2026-10-20). Wagwoorde staan in `CLAUDE.local.md`, nie hier nie.*

## Rolverdeling (demorekeninge)

| Persoon | Rol | Rekening | Wat reeds gereed is |
|---|---|---|---|
| **Johan van Wyk** | Hoofjagter (app) | `johan.vanwyk@demo.mikpunt.co.za` | B-Team (betaal, span aanvaar), B-Hunt (jag klaar), 4 vorige jagte, 2 permitte, SAPS 520 |
| **Mia Botha** | Tweede jagter | `mia.botha@…` | Gekoppel aan B-Team |
| **Pieter Nel** | Uitruster (PJ), Nel Safaris | `pieter.nel@…` | Dokumente goedgekeur; pakket **"Waterberg Koedoe-safari, 4 dae"** (R48 500) |
| **Thabo Mokoena** | PH | `thabo.mokoena@…` | Een dokument eers afgekeur, toe vervang en goedgekeur |
| **Vusi Dlamini** | VG, Vusi Media | `vusi.dlamini@…` | Op B-Team en B-Hunt |
| **Anna Pretorius** | Boer, Kameeldoring Wildplaas | `anna.pretorius@…` | Plaas met wild en lodge |
| **Karoo Optics** | Adverteerder | `bemarking@…` | 1 betaalde (aktief) + 1 onbetaalde advertensie |
| **Lerato Khumalo** | Nuwe PJ | `lerato.khumalo@…` | Dokumente wag; pakkette versteek |
| **Admin** | Admin | `newadmin@mikpunt.com` | Moderasie, dispuut, ouditlog |

> **Aanbiedingswenk:** een persoon vertel, een beheer die skerm. Elke segment: eers die **gevind**-skyfie (een sin), dan die **reggestel**-skerm.

---

## 13-minuut-draaiboek

### 0:00–0:45 — Opening
**Wys:** titelskyfie "Een jag, sewe rolle, twee platforms".
**Sê:** "Ons studie vra: kan elke MikPunt-gebruiker sy kerntaak maklik en suksesvol voltooi? Ons het een jag deur die hele stelsel gevolg, gekyk waar mense vasval — en dit toe reggestel. Vandag wys ons albei: wat ons gevind het, en hoe dit nou werk."

### 0:45–1:25 — Hoe die studie gedoen is
**Wys:** opsommingskyfie — 7 toetsrolle, 17 take, 88 bladsye, ±1 090 outomatiese kontroles per rondte, Nielsen se 10 heuristieke.
**Sê:** "Ons het op die regte staging-omgewing getoets, met nuwe rekeninge deur die gewone registrasie. Die toetse loop nou ook outomaties ná elke ontplooiing. Dit was 'n kundige deurloop, nie nog 'n studie met regte gebruikers nie — dit kom volgende, met 'n SUS-telling."

### 1:25–2:30 — Registrasie, aanmelding en dokumente
| Gevind (skyfie) | Reggestel (lewendig) |
|---|---|
| E-poskodes het ná elke ontplooiing ongeldig geraak; uitteken het die sessie nie beëindig nie. | Kodes oorleef ontplooiings en sluit ná 5 verkeerde pogings; uitteken beëindig die sessie op die bediener. |
| Dokumentoplaai het elke keer misluk, sodat geen PJ/PH/VG goedgekeur kon word nie. | Meld aan as **Thabo** → dokumentlys: een dokument met **afkeuringsrede**, vervang, toe goedgekeur. Wys as **Admin** die dokumentwaglys: die dokument verskyn **binne die goedkeurvenster**. |

**Sê:** "Goeie skerms help nie as 'n databasisprobleem die taak blokkeer nie — dit was ons grootste les. Nou loop die hele vloei: oplaai, afkeur met rede, vervang, goedkeur."

### 2:30–3:35 — Plaas, pakket en advertensie
| Gevind | Reggestel |
|---|---|
| Pieter se pakket was "gestoor" maar onsigbaar, sonder verduideliking. | Meld aan as **Lerato** → *My pakkette*: **"Wag op goedkeuring — jou dokumente wag op goedkeuring deur die administrateur."** |
| — | **Anna** voeg lewendig **Springbok** by Kameeldoring Wildplaas (validasie: "Beskryf die plaas in ten minste 20 karakters"). |
| (Reeds goed) Advertensie gaan eers ná betaling lewendig. | **Karoo Optics**: een aktiewe betaalde advertensie, een **onbetaalde** wat nie op die webwerf verskyn nie. |

### 3:35–5:00 — Soek, bespreek en betaal
| Gevind | Reggestel |
|---|---|
| Markplein het **een pakket per bladsy** gewys. | Markplein wys **9 per bladsy**, alles in **rand**. Soek "Waterberg". |
| Datumkieser het datums in die verlede en einde-voor-begin aanvaar. | **Johan** probeer 'n verlede datum → duidelike Afrikaanse weiering. Bespreek dan geldige datums. |
| Betaling: *"Ongeldige terugkeer-adres."* — niemand kon betaal nie. | Johan se betaalblad gaan na **PayFast** (sandbox) met die bedrag in **rand**. |

**Sê (eerlik):** "Op staging kan PayFast nog nie terugbevestig nie, omdat die bediener nie van die internet af bereikbaar is nie. Daarom wys ons die volgende stap op 'n bespreking wat die admin reeds as betaal gemerk het."

### 5:00–6:20 — Bou die jagspan en kommunikeer (B-Team)
| Gevind | Reggestel |
|---|---|
| (Reeds goed) Status van "Wag op PH/VG" na "Goedgekeur". | **Pieter** → B-Team: Thabo en Vusi het **aanvaar**; **Mia** is as jagmaat gekoppel, sonder dat e-posadresse gewys word. |
| (Reeds goed) Verdagte woorde word stilweg gemerk. | Groepklets met 15 boodskappe; 2 gemerk ("kontant", "WhatsApp") — wys later by die admin. |
| Spanuitnodiging het die **ekspedisienaam en voorwaardes verloor**. | Pieter se hangende uitnodiging wys **ekspedisienaam en terme**. |

### 6:20–7:15 — Permitte en SAPS 520
| Gevind | Reggestel |
|---|---|
| SAPS 520 by die **verkeerde persoon** gestoor; tweede indiening = bedienerfout; permitbeursie laai nie; onvolledige vorm aanvaar. | **Johan**: permitbeursie met **2 permitte**, een **goedgekeurde** en een **hangende** SAPS 520. Probeer 'n onvolledige vorm → **duidelike validasiefout**. |

### 7:15–9:05 — Die jag: app, media en jagstorie (B-Hunt)
| Gevind | Reggestel |
|---|---|
| Webwerf: "media-oplaai nie opgestel nie". | **Vusi** op die webwerf → *Media & Gradering* werk. |
| Storiekaarte in **Engels** en **UTC**. | Die storie gebruik **Afrikaans** en **SA-tyd** (speel die vooraf gemaakte storie). |
| Release-APK kon nie oor HTTPS koppel nie. | Die app se adres (`https://mikpunt.ddns.net`) het nou 'n geldige sertifikaat. |

In die app: **Johan** se B-Hunt-roete (±7 km, 4 uur, 3 skootpenne), foto's en Vusi se grepe.

> Media: vrylisensie-foto's en twee grepe in `tools/demo/assets/` (bronne in `ATTRIBUTION.md`). Die seed laai hulle op en maak die storie; die reset herstel albei.

### 9:05–10:10 — Trofee en spanbeoordeling
| Gevind | Reggestel |
|---|---|
| (Reeds goed) Trofeevloei: indien → regstelling → herindien → goedkeur. | Johan se **Koedoe** is goedgekeur, publiek en Rowland Ward-gekoppel. Sy **Rooibok** wag op regstelling → Johan **herdien lewendig**, Thabo **keur goed**. |
| Gradering in die app "nie opgestel nie"; profiel: **"Voltooide jagte: 0"**. | Johan gradeer **Thabo en Vusi** (ook in die app). Profiel tel sy **5 voltooide jagte**. |

### 10:10–10:55 — Adminbeheer en veiligheid
| Gevind | Reggestel |
|---|---|
| 'n Admin kon **homself skors**. | Self-skorsing word **geweier**. |
| — (nuut) | **Skors** 'n gebruiker → hy word **dadelik op webwerf en app afgemeld**; **Aktiveer** bring hom terug. |
| — | Moderasie: 2 gemerkte boodskappe, 1 graderingkommentaar, 1 **oop dispuut**; ouditlog. |
| Gesondheidspaneel het **vasgekodeerde** syfers gewys. | **Kenmerkvlae & Gesondheid**: regte status (webwerf-API, databasis, app-API), sessies, en vlae wat werklik werk (onderhoudmodus). |

### 10:55–11:45 — Wat die heuristieke vir ons sê
**Wys:** skyfie met vier groepe — status, foutvoorkoming, foutboodskappe, konsekwentheid — elkeen met **voor → ná**.
**Sê:** "Ons het van vier heuristieke met erns 'Hoog' na nul gegaan. Wat oorbly, is kleiner: kanselleringsreëls ná betaling, en in-app-hulp oor waarom dokumente nodig is."

### 11:45–12:25 — Die resultaat
**Wys:** twee kolomme.

| | 12 Okt (gevind) | 13 Okt (reggestel) |
|---|---|---|
| Maklik voltooi | 47 % | **78 %** |
| Met 'n probleem | 35 % | 22 % |
| Geblokkeer | 18 % | **0 %** |
| Voltooibaar | 82 % | **100 %** |

**Sê:** "Al drie sakekritieke blokkeerders — dokumente, betaling en permitte — is reggestel. Elke taak kan nou voltooi word."

### 12:25–13:00 — Wat nog oorbly en slot
**Wys:** drie punte.
1. **Infrastruktuur:** WebSockets op die proxy vir die webwerf; PayFast-terugbevestiging; YouTube herkoppel.
2. **Klein bruikbaarheid:** kanselleringsbeleid ná betaling, in-app-hulp by dokumente.
3. **Volgende studie:** 5 regte gebruikers per hoofrol, hardop dink, **SUS ≥ 68**.

**Sê:** "MikPunt se kernontwerp was nog altyd sterk. Wat gebruikers gekeer het, was opstelling en data-integrasie. Dit is nou reggestel, en ons toetse loop outomaties sodat dit so bly."

---

## Demo-operateur se kontrolelys

*Klik-vir-klik: sien `demo_draaiboek_operateur.md`.*

### Die dag voor (en ná elke oefenlopie)
```powershell
python tools/demo/reset_demo.py --demo-date 2026-10-20
python tools/demo/check_demo.py --demo-date 2026-10-20 --api      # alles moet [OK] wees
```
(Omgewingsveranderlikes: sien `CLAUDE.local.md` → *Demo cast*.)

### Voor die demo
- [x] Foto's en grepe in `tools/demo/assets/` (8 jagter, 4 PH, 2 VG ≤ 30 s, 1 koedoe-trofee, 8 plaas), bronne in `ATTRIBUTION.md`; opgelaai en storie gemaak.
- [ ] **WebSockets op die proxy** vir `webapp.mikpunt.ddns.net` aan (anders is die webwerf traag), of die noodplan in `beslissings_nodig.md` D13.
- [ ] Meld vooraf aan as Johan, Pieter, Thabo, Vusi, Lerato en Admin in **aparte blaaierprofiele**.
- [ ] **"Gevind"-skyfies**: die ou foute bestaan nie meer nie, so wys hulle as aanhalings/skermgrepe op skyfies (bv. *"Ongeldige terugkeer-adres."*, *"Voltooide jagte: 0"*, een pakket per bladsy).
- [ ] Rugsteunvideo van die volle demo.
- [ ] Moenie op die dag ontplooi nie.

### Tydbeheer
- Teen **5:00**: bespreking en betaling klaar. Teen **9:05**: app/media klaar. Teen **11:45**: heuristieke klaar.
- Laat die resultaat en slot nooit uit nie; sny eerder 'n lewendige stap uit en wys die skyfie.

## Een-sin-noodslot
> "Ons studie het drie sakekritieke blokkeerders gevind — dokumente, betaling en permitte — en ná die regstellings kan elke taak voltooi word, met 78 % sonder enige probleem."
