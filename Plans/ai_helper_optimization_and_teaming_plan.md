# Comprehensive Implementation Plan: WebApp AI Helper for Packet Optimization & Smart Teaming

> **Implementation status (2026-10-08).** Both features are implemented, differently from this plan in places:
> * The engine lives in the **WebAPI** (it needs DB access and the API key), not in a WebApp `AiHelperService`.
> * **Feature 1** uses a trained ONNX price model plus a hosted LLM API (Gemini / Groq / ... / Claude via an API key - nothing is
>   self-hosted) rather than rule-based tables. The "Profit Score" gauge became a price band with confidence, because no cost data
>   exists to compute profit. The page is also a **packet maker**: it loads the user's existing package / farm / ad and hands the
>   improved offer to the normal editor to save.
> * **Feature 2 (Smart Teaming)** uses the section 4.1 weights as a prior, blended with a logistic regression learned from real invite
>   outcomes; the LLM writes the reasons from anonymised facts. It is a tab on `/ai-helper` (no separate page).
> * Dashboards for all five roles link to it (`Components/Shared/AiHelperLinks.razor`). See `tools/ai/README.md`.

## 1. Executive Summary & Goals

This plan details the full design and implementation strategy for an **AI Helper / Business Assistant** integrated into the MikPunt WebApp (`src/WebApp`).

The AI Helper serves five core platform roles (**PJ, PH, VG, AP, Boer**) with two primary capabilities:
1. **Packet & Profit Optimization** (*Available to ALL 5 roles: PJ, PH, VG, AP, Boer*): Analyzes pricing, package inclusions, seasonal demand, and South African hunting market benchmarks to maximize sales and profit margins.
2. **Smart Teaming & Matchmaking** (*Available to PJ, PH, VG, Boer — EXCLUDING AP*): Recommends optimal field expedition team partners based on geographic proximity, credentials, ratings, rates, and current booking availability.

> [!NOTE]
> **AP Exclusion Policy**: Advertisers (AP) manage commercial marketing campaigns (e.g. taxidermists, gunsmiths, 4x4 accessories) on the marketplace and do not participate in field expedition teams. The AI Helper explicitly restricts teaming features for AP users while providing full ad campaign profit optimization.

---

## 2. Role-by-Role Feature Scope Matrix

| Role | Role Title | Packet / Offer Optimization | Smart Teaming / Matchmaking |
| :--- | :--- | :---: | :---: |
| **PJ** | Uitruster (Outfitter) |  Yes (Safari & Expedition Packages) |  Yes (Recruit PHs, VGs, Partner Farms) |
| **PH** | Professionele Jagter |  Yes (Daily Rates & Specialities) |  Yes (Find Outfitters & Partner Farms) |
| **VG** | Videograaf (Media) |  Yes (4K Video & Drone Packages) |  Yes (Find Outfitters & PHs) |
| **Boer** | Grondeienaar (Farm Owner) |  Yes (Species Quotas & Daily Rates) |  Yes (Find Outfitters & PHs) |
| **AP** | Adverteerder (Advertiser) |  Yes (Ad Campaigns & Placements) | ❌ **Excluded** (Commercial Ads Only) |

---

## 3. AI Optimization Engine Architecture (Feature 1)

### 3.1 Role-Specific Profit & Sales Optimization Strategies

* **Uitruster (PJ)**:
  * Analyzes all-inclusive package pricing against regional benchmarks (Limpopo, Karoo, Kalahari).
  * Recommends high-yielding species combinations (e.g., Kudu + Impala + Warthog + 5 Nights Lodge).
  * Suggests add-on bundles (e.g., 4K drone footage, SAPS 520 customs import handling).
  * Provides dynamic seasonal pricing recommendations (peak hunting months: May–August).

* **Professionele Jagter (PH)**:
  * Recommends optimal daily guide rates based on qualifications (Rowland Ward accreditation, Dangerous Game licensing).
  * Calculates dangerous game surcharges (.375+ caliber requirements, tracking risks).
  * Suggests schedule-filling strategies during off-peak months.

* **Videograaf (VG)**:
  * Recommends multi-tier video production packages (Highlight Reel vs Full 4K Documentary).
  * Identifies upsell opportunities (60-second social media teasers delivered within 48 hours in camp).
  * Recommends retainer pricing for exclusive outfitter partnerships.

* **Grondeienaar (Boer)**:
  * Optimizes trophy vs non-trophy species inventory pricing (Male, Female, Trophy).
  * Recommends low-season discounts on female game to balance herd numbers and maintain high off-season occupancy.
  * Recommends lodge daily rates based on amenities (catering, cold rooms, slaughter facilities, swimming pool).

* **Adverteerder (AP)**:
  * Optimizes ad headline copy, promotional call-to-actions, and discount coupon codes.
  * Recommends target category placement (Taxidermy, Gear, Firearms, Vehicles).
  * Calculates estimated Click-Through-Rates (CTR) and conversion metrics.

---

## 4. AI Smart Teaming & Matchmaking Engine (Feature 2)

### 4.1 Matchmaking Compatibility Algorithm

For **PJ, PH, VG, and Boer**, the AI evaluates network profiles and calculates a **Match Score (0–100%)** using the following weighted parameters:

$$\text{Match Score} = (0.35 \times \text{Proximity}) + (0.25 \times \text{Credential Rating}) + (0.20 \times \text{Rate Compatibility}) + (0.20 \times \text{Historical Success})$$

* **Proximity / Province (35%)**: Prioritizes partners operating in the same or adjacent province (e.g., Limpopo, Northern Cape).
* **Credentials & Rating (25%)**: Verified license status, Rowland Ward accreditation, 4.5+ star review averages.
* **Rate Compatibility (20%)**: Ensures partner daily fees fit within the outfitter’s budget allocation.
* **Historical Success (20%)**: Number of successfully completed joint hunts and positive hunter reviews.

### 4.2 AP Exclusion Notice Behavior

When an AP user navigates to the Teaming tab, the UI displays a clear informational notification:
> *"Adverteerders (AP) bemark kommersiële produkte/dienste op die markplein en neem nie deel aan jagspan-teaming in die veld nie. Teaming is beskikbaar vir Uitrusters (PJ), Professionele Jagters (PH), Videograwe (VG) en Boere."*

---

## 5. Web Application Component Architecture

### 5.1 New WebApp Files Structure

* **`src/WebApp/Services/AiHelperService.cs`**:
  * `GetPacketOptimizationAsync(roleCode, price, province)`: Returns AI optimization scores, recommended prices, profit tips, and market insights.
  * `GetTeamRecommendationsAsync(roleCode, province)`: Returns matched partner profiles for PJ/PH/VG/Boer, or returns the AP exclusion flag.
* **`src/WebApp/Components/Pages/AiOptimizationHelper.razor`**:
  * Dedicated Blazor page (`@page "/ai-helper"`).
  * Tabbed layout: **Tab 1: AI Packet & Profit Optimizer** | **Tab 2: AI Smart Teaming**.
  * Role switcher buttons to test/view recommendations across PJ, PH, VG, AP, Boer.
* **`src/WebApp/Program.cs`**:
  * Registers `builder.Services.AddScoped<AiHelperService>();`.

---

## 6. Phased Implementation Roadmap

### Phase 1: Backend Service & Data Models
1. Create `AiHelperService.cs` with data models (`PacketOptimizationResult`, `TeamRecommendationResult`, `OptimizationTip`).
2. Integrate market benchmark pricing and rule-based inference logic for SA hunting regions.
3. Register service in `Program.cs`.

### Phase 2: AI Optimizer UI (All 5 Roles)
1. Build `AiOptimizationHelper.razor` with dynamic controls (Role selector, Province, Base Price input).
2. Implement score gauges (Profit Score %, Competitiveness Score %).
3. Display AI-generated titles, high-conversion inclusions, and profit improvement tips.

### Phase 3: Smart Teaming & AP Exclusion
1. Implement the matchmaking algorithm for PJ, PH, VG, and Boer.
2. Build the AP exclusion view with notice banner and redirect buttons.
3. Add one-click "Stuur Span-Uitnodiging" action buttons linking to `ManageTeam.razor`.

### Phase 4: Dashboard & Navigation Integration
1. Add "AI Helper" link to `HeaderNav.razor`.
2. Add quick-access AI recommendation widgets in `PjDashboard.razor`, `PhDashboard.razor`, `BoerDashboard.razor`, and `ApDashboard.razor`.

---

## 7. Verification & Success Criteria

- [x] All 5 roles (PJ, PH, VG, AP, Boer) can run packet & profit optimization.
- [x] Recommended prices update when parameters change (price band + % vs the median; no profit % - there is no cost data).
- [x] Smart teaming displays relevant partner recommendations for PJ, PH, VG, and Boer.
- [x] AP role cleanly displays the exclusion notice on the teaming tab.
- [x] WebApp builds cleanly with zero compilation errors (`dotnet build`).
