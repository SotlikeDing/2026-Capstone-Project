# Demo-draaiboek vir die operateur: wat om waar te doen

*Klik-vir-klik-begeleiding by `demo_scenario_13min_gevind_reggestel.md`. Die verteller praat volgens daardie skrip; die operateur volg hierdie een. Webwerf: `http://webapp.mikpunt.ddns.net`. Alle adresse hieronder is relatief tot die webwerf. Die ou Engelse adresse (bv. `/login`) werk steeds, maar gebruik die Afrikaanse adresse.*

*Wagwoorde staan in `CLAUDE.local.md`, nie hier nie. Alle demorekeninge (behalwe Admin) gebruik dieselfde demowagwoord.*

---

## A. Die dag voor (en ná elke oefenlopie)

1. Koppel NetBird ("capstone access" moet *Selected* wees).
2. Stel die omgewingsveranderlikes in (sien `CLAUDE.local.md` → *Demo cast*) en voer uit:
   ```powershell
   python tools/demo/reset_demo.py --demo-date 2026-10-20
   python tools/demo/check_demo.py --demo-date 2026-10-20 --api
   ```
   - Die reset herstel die databasis **en** die B-Hunt-media: oefenoplaaie word versteek, ontbrekende demofoto's word weer opgelaai, die 5 storiefoto's word weer ingesluit, en 'n nuwe jagstorie word gemaak as die stel verander het (dit kan tot 3 minute neem).
   - `check_demo` moet **61/61** wys. Enige `[FAIL]` → herseed: `python tools/demo/seed_demo.py --demo-date 2026-10-20`, dan weer reset en check.
3. Moenie ná hierdie punt na `main_dev` stoot nie (dit ontplooi en herbegin die bedieners).

## B. 30 minute voor die demo: blaaieropstelling

Maak **ses aparte blaaierprofiele** (Chrome → profiel byvoeg) sodat elke rol aangemeld bly. Meld in elkeen aan by `/teken-in` en laat die oortjie op die beginblad hieronder:

| Profiel | Rekening | Beginblad |
|---|---|---|
| 1 Johan (jagter) | `johan.vanwyk@demo.mikpunt.co.za` | `/markplein` |
| 2 Pieter (PJ) | `pieter.nel@demo.mikpunt.co.za` | `/pj/besprekings` |
| 3 Thabo (PH) | `thabo.mokoena@demo.mikpunt.co.za` | `/profiel` |
| 4 Vusi (VG) | `vusi.dlamini@demo.mikpunt.co.za` | `/vg/besprekings` |
| 5 Anna (boer) | `anna.pretorius@demo.mikpunt.co.za` | `/boer/plase` |
| 6 Admin | `newadmin@mikpunt.com` | `/administrasie` |

Lerato en Karoo Optics: gebruik 'n **incognito-venster** en meld aan wanneer nodig (kort segmente).

**Foon:** Jagbestuur-app oop, aangemeld as **Johan** (rol *Jagter*), op die B-Hunt-bespreking.
**Skyfies:** oop in 'n aparte venster, op die titelskyfie.

> Die webwerf is traag deur die proxy (geen WebSockets). Laai elke blad een keer vooraf; wag ná 'n klik tot die skerm klaar is voor jy weer klik.

---

## C. Tydens die demo, segment vir segment

### 0:00–1:25: Opening en studie
- **Skyfies** 1–2. Geen blaaierwerk nie.

### 1:25–2:30: Registrasie en dokumente (Thabo → Admin)
1. *Skyfie "gevind"*: e-poskodes / dokumentoplaai.
2. **Profiel 3 (Thabo)** → `/profiel` → rol **PH** onder *Jou Aktiewe Rolle* → dokumentlys.
   Wys die dokument met die **afkeuringsrede**, en die vervangde dokument wat **goedgekeur** is.
3. **Profiel 6 (Admin)** → `/administrasie` → oortjie **Rolverifikasie Wagtoring** → afdeling **Dokumentverifikasie Wagtoring**.
   Klik **Bekyk** op 'n hangende dokument: dit maak **binne die goedkeurvenster** oop. Maak toe **sonder** om goed te keur (dit is Lerato s'n; sy moet hangend bly vir 2:30).

### 2:30–3:35: Plaas, pakket en advertensie (Lerato → Anna → Karoo)
1. **Incognito: Lerato** (`lerato.khumalo@demo.mikpunt.co.za`) → `/pj/pakkette`.
   Wys die geel etiket **"Wag op goedkeuring"** met *"jou dokumente wag op goedkeuring deur die administrateur."* Teken uit.
2. **Profiel 5 (Anna)** → `/boer/plase` → **Kameeldoring Wildplaas** → wysig (`/boer/wysig-plaas/26`).
   - Afdeling **3. Wildspesies Inventaris & Pryslyste** → **+ Voeg Spesie By Inventaris** → `Springbok`, vul prys en aantal in.
   - *(Validasie wys):* maak die beskrywing in **1. Plaas Besonderhede** tydelik korter as 20 karakters → **Stoor Alle Wysigings →** → foutboodskap. Herstel die beskrywing.
   - Blaai af na **4. Plaasfoto's**: die plaas se twee eie foto's (die eerste is die hooffoto op die markplein).
   - **Stoor Alle Wysigings →**: Springbok verskyn.
3. **Incognito: Karoo Optics** (`bemarking@demo.mikpunt.co.za`) → `/ap/paneelbord` → **Bestuur Veldtogte**.
   Wys een **aktiewe betaalde** en een **onbetaalde** advertensie. Open `/advertensies` in dieselfde venster: net die betaalde een verskyn. Teken uit.

### 3:35–5:00: Soek, bespreek en betaal (Johan)
1. *Skyfie "gevind"*: een pakket per bladsy.
2. **Profiel 1 (Johan)** → `/markplein`. Wys **9 pakkette per bladsy** en pryse in **R**. Tik `Waterberg` in die soekveld.
3. Op **"Waterberg Koedoe-safari, 4 dae"** (R48 500) → **Sien Besonderhede & Versoek**.
   Wys kortliks die plaasfoto, die dagprogram en die twee lyste **Wat Ingesluit Is** / **Wat Uitgesluit Is** → **Gaan Voort met Versoek**.
4. Kies 'n **begindatum in die verlede** → **Dien Versoek In** → Afrikaanse weiering verskyn.
5. Kies geldige datums (bv. 2026-12-01 tot 2026-12-04) → **Dien Versoek In** → sukses.
6. `/jagter/besprekings` → die nuwe versoek (status: wag op PJ). *(Vir betaal:)* klik **Betaal** op 'n goedgekeurde bespreking → `/betaling` wys die bedrag in **rand** en die **PayFast**-knoppie. Klik deur na die PayFast-sandbox en **stop daar** (moenie betaal nie).
7. Verteller sê die eerlike sin oor PayFast-terugbevestiging.

### 5:00–6:20: Jagspan en kommunikasie (Pieter, B-Team)
1. **Profiel 2 (Pieter)** → `/pj/besprekings` → filter **Betaal** → B-Team (2026-11-10 tot 14) → besonderhede.
   Wys: **Thabo en Vusi het aanvaar**, **Mia** as jagmaat, geen e-posadresse sigbaar nie.
2. `/boodskappe` → **"Waterberg jagspan"** (15 boodskappe). Wys die boodskappe met "kontant" en "WhatsApp" (die admin sien die vlae later).
3. `/pj/span` → hangende uitnodiging: wys die **ekspedisienaam** ("Waterberg Koedoe-safari, 4 dae") en die **terme**.
   *(Alternatief:)* die ontvanger se kant by `/uitnodigings`.

### 6:20–7:15: Permitte en SAPS 520 (Johan)
1. **Profiel 1 (Johan)** → `/jagter/permitte` → **2 permitte**: een **goedgekeurde** en een **hangende** SAPS 520.
2. `/jagter/e520` → vul net **Afdeling 1** in en laat **Afdeling 3** leeg → **Stoor & Dien In Vorm** → **duidelike validasiefout**. **Moenie** die volledige vorm indien nie.

### 7:15–9:05: Die jag (B-Hunt): app, media en storie
1. **Profiel 4 (Vusi)** → `/vg/besprekings` → B-Hunt (2026-10-15 tot 18, *Voltooi*) → **Media & ⭐ Gradering**.
   Wys die mediapaneel: 8 jagter- en 4 PH-foto's plus Vusi se 2 videogrepe (≤ 30 s). Vyf foto's is ingesluit vir die storie.
2. **Foon (Johan)**: B-Hunt → **roete** (±7 km, 4 uur, 3 skootpenne) → foto's en Vusi se grepe.
3. **Jagstorie**: speel die klaar gemaakte storie (20–25 s). Wys die **Afrikaanse** kaarte en **SA-tyd**.
4. *Skyfie "gevind"*: APK/HTTPS. Verteller sê die app se adres het nou 'n geldige sertifikaat.

### 9:05–10:10: Trofee en spanbeoordeling
1. **Foon (Johan)**: trofees → **Koedoe**: goedgekeur, publiek, Rowland Ward-gekoppel.
2. **Rooibok** (*Regstelling versoek*) → open → pas aan volgens die versoek → **herdien**.
3. **Thabo** keur dit goed (in die app as PH, of laat die verteller dit noem as die tyd min is).
4. **Profiel 1 (Johan)** → `/jagter/besprekings` → B-Hunt → **Gradeer u span** → gradeer **Thabo** en **Vusi**.
5. `/profiel` (of Johan se publieke profiel) → **5 voltooide jagte**.

### 10:10–10:55: Admin en veiligheid (Admin)
**Profiel 6 (Admin)** → `/administrasie`:
1. Oortjie **Meester Gebruikersgids** → soek `newadmin` → **Skors** op jouself → **geweier**.
2. Soek 'n demogebruiker (bv. **Mia Botha**) → **Skors** → reden → bevestig.
   *(Wys dadelik)*: as Mia êrens aangemeld was, word sy uitgeteken. Klik dan **Aktiveer** → status **Aktief**, net **Skors** sigbaar.
3. Oortjie **Oorsig & Statistieke** → moderasie: **2 gemerkte boodskappe**, **1 graderingkommentaar**.
4. Oortjie **Dispute & Besprekingsregstellings** → **1 oop dispuut** (net wys, nie oplos nie).
5. Oortjie **Onveranderlike Ouditrekord** → die skors/aktiveer van netnou staan bo-aan.
6. Oortjie **Kenmerkvlae & Gesondheid** → regte status (webwerf-API, databasis, app-API), sessies en vlae. **Moenie** onderhoudmodus aanskakel nie.

### 10:55–13:00: Heuristieke, resultaat en slot
- **Skyfies** 9–12. Geen blaaierwerk nie.

---

## D. Ná die demo / tussen oefenlopies

```powershell
python tools/demo/reset_demo.py --demo-date 2026-10-20
python tools/demo/check_demo.py --demo-date 2026-10-20 --api
```
Dit maak Johan se nuwe versoek, Anna se Springbok, Rooibok-herindiening, graderings, skorsings, sessies en oefenmedia ongedaan. Ouditlogs bly (hulle is onveranderlik).

## E. Noodplanne

| As… | Doen |
|---|---|
| 'n Blad laai nie binne 10 s nie | Herlaai een keer. Werk dit steeds nie, wys die skermgreep op die rugsteunskyfie en gaan aan. |
| Aanmelding misluk ("sessie verval") | Meld weer aan by `/teken-in`. Moet nie die wagwoord op die skerm wys nie. |
| Die jagstorie speel nie | Speel die rugsteunvideo van die storie. |
| Die app koppel nie | Wys dieselfde B-Hunt-media op die webwerf (Profiel 4, stap 7:15) en die roete-skermgreep. |
| Tyd raak min | Sny 2:30 (Karoo) en 9:05 stap 3; laat **nooit** die resultaat en slot uit nie. |
