using System;
using System.Collections.Generic;

namespace WebApp.Models
{
    /// <summary>
    /// Client-side view of the signed-in (or looked-up) user. Note WebApp.Models.UserModel
    /// is a distinct, hand-shaped type from WebAPI.Models.User - the two are kept in sync
    /// only by MikpuntApiService's mapping code, not by sharing a DTO assembly.
    /// </summary>
    public class UserModel
    {
        public int UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string FullName => $"{FirstName} {LastName}".Trim();
        public string Phone { get; set; } = string.Empty;
        public string IdPassportNum { get; set; } = string.Empty;
        public string? ProfilePictureUrl { get; set; }
        public bool IsVerified { get; set; } = true;
        public string AccountStatus { get; set; } = "Active"; // 'Active', 'Suspended', 'Banned'
        public string? StatusReason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<UserRoleModel> Roles { get; set; } = new();
    }

    /// <summary>One role grant for a user (e.g. "PH", "Boer") with its own approval status - a user can hold several simultaneously.</summary>
    public class UserRoleModel
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string RoleCode { get; set; } = string.Empty; // 'Jagter', 'Boer', 'PJ', 'PH', 'VG', 'AP', 'Admin'
        public string RoleName { get; set; } = string.Empty;
        public string Status { get; set; } = "Approved"; // 'Pending', 'Approved', 'Rejected'
        public string CredentialsDetails { get; set; } = string.Empty;
        public DateTime AssignedDate { get; set; } = DateTime.UtcNow;
    }

    /// <summary>A farm listing. AvailableSpecies falls back to a hardcoded default species list when GameInventory hasn't been populated yet, so pages always have something to display.</summary>
    public class FarmModel
    {
        public int Id { get; set; }
        public int FarmerUserId { get; set; }
        public int FarmerUserRoleId { get; set; }
        public string FarmerName { get; set; } = "Boer Piet";
        public string Name { get; set; } = string.Empty;
        public string Province { get; set; } = "Limpopo";
        public string NearestTown { get; set; } = string.Empty;
        public double SizeHectares { get; set; }
        public decimal DailyRateHunter { get; set; }
        public decimal DailyRateObserver { get; set; }
        public string Description { get; set; } = string.Empty;
        public List<string> Amenities { get; set; } = new();
        public List<string> ImageUrls { get; set; } = new();
        public bool IsActive { get; set; } = true;
        public string Status { get; set; } = "Aktief";
        public List<GameSpeciesInventoryModel> GameInventory { get; set; } = new();
        public List<string> AvailableSpecies => GameInventory != null && GameInventory.Count > 0
            ? GameInventory.Select(g => g.SpeciesName).ToList()
            : new List<string> { "Koedoe", "Rooibok", "Vlakvark", "Blesbok", "Gemsbok", "Blouwildebees", "Sebra" };
    }

    /// <summary>Per-species stock/pricing line on a farm (male/female/trophy price tiers, count available).</summary>
    public class GameSpeciesInventoryModel
    {
        public int Id { get; set; }
        public int FarmId { get; set; }
        public string SpeciesName { get; set; } = string.Empty;
        public decimal PriceMale { get; set; }
        public decimal PriceFemale { get; set; }
        public decimal PriceTrophy { get; set; }
        public int AvailableCount { get; set; }
    }

    /// <summary>A PJ-organised hunting package/expedition offer. IncludedSpecies mirrors AvailableFarmSpecies when the latter is populated (species list is meant to come from the linked farm), otherwise falls back to a locally-set list via the private backing field.</summary>
    public class HuntingPackageModel
    {
        public int Id { get; set; }
        /// <summary>The PJ's own required documents are approved (else the marketplace hides the package).</summary>
        public bool OrganiserDocumentsApproved { get; set; } = true;
        /// <summary>The farm's farmer has every required document approved (else the marketplace hides the package).</summary>
        public bool FarmerDocumentsApproved { get; set; } = true;
        public bool IsMarketplaceVisible => OrganiserDocumentsApproved && FarmerDocumentsApproved;
        public int OrganiserUserId { get; set; }
        public string OrganiserName { get; set; } = string.Empty;
        public int FarmId { get; set; }
        public string FarmName { get; set; } = string.Empty;
        public string FarmerName { get; set; } = string.Empty;
        public string Province { get; set; } = "Limpopo";
        public string NearestTown { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int DurationDays { get; set; } = 4;
        public decimal PricePerHunter { get; set; }
        public decimal PricePerObserver { get; set; } = 1500;
        public int AvailableSlots { get; set; } = 4;
        public int? PhUserId { get; set; }
        public string? PhName { get; set; }
        public int? VgUserId { get; set; }
        public string? VgName { get; set; }
        public List<string> AvailableFarmSpecies { get; set; } = new();
        public List<string> IncludedSpecies 
        { 
            get => AvailableFarmSpecies.Count > 0 ? AvailableFarmSpecies : _includedSpecies; 
            set { _includedSpecies = value; if (AvailableFarmSpecies.Count == 0) AvailableFarmSpecies = value; } 
        }
        private List<string> _includedSpecies = new();
        public List<string> IncludedServices { get; set; } = new();
        public List<string> ExcludedServices { get; set; } = new();
        public bool IsPublished { get; set; } = true;
        public string ImageUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// A hunting trip booking, spanning its whole lifecycle from initial request through
    /// PH/VG staff acceptance, admin approval/pricing, payment, and completion. PriceBreakdown
    /// is a computed convenience view that deserializes PriceBreakdownJson on demand,
    /// returning null (not throwing) if the JSON is absent or malformed.
    /// </summary>
    public class BookingModel
    {
        public int Id { get; set; }
        public int HunterUserId { get; set; }
        public string HunterName { get; set; } = string.Empty;
        public string HunterEmail { get; set; } = string.Empty;
        public string HunterPhone { get; set; } = string.Empty;
        public int PjUserId { get; set; }
        public string PjName { get; set; } = string.Empty;
        public int? FarmId { get; set; }
        public string? FarmName { get; set; }
        public int? PackageId { get; set; }
        public string? PackageTitle { get; set; }
        public DateTime StartDate { get; set; } = DateTime.Today.AddDays(7);
        public DateTime EndDate { get; set; } = DateTime.Today.AddDays(11);
        public int HunterCount { get; set; } = 1;
        public int ObserverCount { get; set; } = 0;
        public bool WantsVideographer { get; set; } = false;
        public string FirearmOption { get; set; } = "Importing"; // "Importing" (SAPS 520) or "Rental" (Use PH Firearms)
        public string SpecialNotes { get; set; } = string.Empty;
        public int? AssignedPhUserId { get; set; }
        public string? AssignedPhName { get; set; }
        public int? AssignedVgUserId { get; set; }
        public string? AssignedVgName { get; set; }
        public string? PhAssignmentStatus { get; set; } // Pending/Accepted/Declined - null once locked in / no assignment yet
        public string? VgAssignmentStatus { get; set; }
        public string? PhDeclineReason { get; set; }
        public string? VgDeclineReason { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal DepositPaid { get; set; }
        public string Status { get; set; } = "Pending"; // 'Pending', 'PendingStaffAcceptance', 'Approved', 'Rejected', 'Paid', 'Completed'
        public List<string> BookedSpecies { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string PaymentReference { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
        public string? RejectionNotes { get; set; }
        public decimal? ConfirmedAccommodationPrice { get; set; }
        public string? PriceBreakdownJson { get; set; }
        public BookingPriceBreakdown? PriceBreakdown
        {
            get
            {
                if (string.IsNullOrWhiteSpace(PriceBreakdownJson)) return null;
                try
                {
                    return System.Text.Json.JsonSerializer.Deserialize<BookingPriceBreakdown>(PriceBreakdownJson);
                }
                catch
                {
                    return null;
                }
            }
        }
    }

    /// <summary>Admin's request to approve a booking while assigning PH/VG staff and locking in the confirmed price.</summary>
    public class ApproveWithStaffRequest
    {
        public int PhUserId { get; set; }
        public int? VgUserId { get; set; }
        public decimal? ConfirmedTotalPrice { get; set; }
        public decimal? ConfirmedAccommodationPrice { get; set; }
        public string? PriceBreakdownJson { get; set; }
    }

    /// <summary>One species line within a confirmed booking's price breakdown (how many of that species, at what per-animal price).</summary>
    public class ConfirmedSpeciesPriceItem
    {
        public string SpeciesName { get; set; } = string.Empty;
        public decimal PricePerAnimal { get; set; }
        public int AvailableCount { get; set; }
        public int SelectedCount { get; set; } = 1;
    }

    /// <summary>Itemized final pricing for an approved booking (accommodation + per-species game charges + PH/VG fees), stored server-side as JSON on the booking row (see BookingModel.PriceBreakdownJson) and surfaced to hunters/staff as a receipt-style breakdown.</summary>
    public class BookingPriceBreakdown
    {
        public int Days { get; set; }
        public int HunterCount { get; set; }
        public int ObserverCount { get; set; }
        public decimal DailyRateHunter { get; set; }
        public decimal DailyRateObserver { get; set; }
        public decimal AccommodationTotal { get; set; }
        public List<ConfirmedSpeciesPriceItem> ConfirmedSpecies { get; set; } = new();
        public decimal GameTotal { get; set; }
        public decimal PhTotal { get; set; }
        public decimal VgTotal { get; set; }
        public int VgPackageId { get; set; }
        public string? VgPackageTitle { get; set; }
        public decimal GrandTotal { get; set; }
        /// <summary>Quote currency; null when the quote didn't record one - then it is in the package currency.</summary>
        public string? Currency { get; set; }
        public decimal DepositPercentage { get; set; } = 50m;
        public decimal DepositAmount { get; set; }
        public decimal OutstandingBalance { get; set; }
        public string? ScheduleOverrideReason { get; set; }
        public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>One entry in a booking's audit trail (who did what, when) - shown to hunters/staff/admins reviewing a booking's history.</summary>
    public class BookingAuditLog
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public int PerformedByUserId { get; set; }
        public string PerformedByName { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Admin/staff request to reject a booking, with a structured reason code plus free-text description/notes.</summary>
    public class RejectBookingRequest
    {
        public string? ReasonCode { get; set; }
        public string? ReasonDescription { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>A PJ's invitation for a PH or VG to join a specific expedition/team, pending that person's accept/decline.</summary>
    public class TeamInviteModel
    {
        public int Id { get; set; }
        public int PjUserId { get; set; }
        public string PjName { get; set; } = string.Empty;
        public int TargetUserId { get; set; }
        public string TargetUserName { get; set; } = string.Empty;
        public string TargetRoleCode { get; set; } = "PH"; // 'PH' or 'VG'
        public string ExpeditionName { get; set; } = string.Empty;
        public string Terms { get; set; } = string.Empty;
        public DateTime Dates { get; set; } = DateTime.Today.AddDays(14);
        public string Status { get; set; } = "Pending"; // 'Pending', 'Accepted', 'Declined'
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>A hunter's game permit record.</summary>
    public class PermitModel
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string PermitNumber { get; set; } = string.Empty;
        public string PermitType { get; set; } = "Ordinary Game Permit";
        public DateTime IssueDate { get; set; } = DateTime.Today.AddMonths(-1);
        public DateTime ExpiryDate { get; set; } = DateTime.Today.AddMonths(11);
        public string DocumentUrl { get; set; } = string.Empty;
        public string Province { get; set; } = "Limpopo";
    }

    /// <summary>One firearm line item on a SAPS 520 form.</summary>
    public class FirearmItemModel
    {
        public string Type { get; set; } = "Rifle";
        public string Action { get; set; } = "Grendelaksie (Bolt Action)";
        public string Make { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Calibre { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string FrameReceiverSerialNumber { get; set; } = string.Empty;
        public string BarrelSerialNumber { get; set; } = string.Empty;
    }

    /// <summary>One ammunition line item on a SAPS 520 form.</summary>
    public class AmmunitionItemModel
    {
        public string Type { get; set; } = string.Empty;
        public int Quantity { get; set; } = 20;
    }

    /// <summary>Full data for a SAPS 520 (temporary firearm import/export permit) application, rendered to PDF by Saps520PdfService. Supports all official fillable fields from the SAPS 520 form.</summary>
    public class Saps520FormModel
    {
        public int Id { get; set; }
        public int UserId { get; set; }

        // Section 1: Personal Details / Persoonlike Besonderhede
        public string IdentificationType { get; set; } = "Passport"; // "Passport" or "SA_ID"
        public string Surname { get; set; } = string.Empty;
        public string FullNames { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public string IdNum { get; set; } = string.Empty;
        public string PassportNum { get; set; } = string.Empty;
        public DateTime Dob { get; set; } = DateTime.Today.AddYears(-30);
        public int? Age { get; set; }
        public string Gender { get; set; } = "Male";
        public string Profession { get; set; } = string.Empty;

        // Employment / Werkgewer & Beroep
        public bool IsSelfEmployed { get; set; } = false;
        public string SelfEmployedSpecify { get; set; } = string.Empty;
        public string EmployerName { get; set; } = string.Empty;
        public string BusinessAddress { get; set; } = string.Empty;
        public string BusinessPostalCode { get; set; } = string.Empty;

        // Section 2: Addresses & Contact / Woon- en Posadres & Kontak
        public string ResidentialAddress { get; set; } = string.Empty;
        public string ResidentialPostalCode { get; set; } = string.Empty;
        public string PostalAddress { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string Country { get; set; } = "United States";
        public string SaContactAddress { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public string HomeTelephone { get; set; } = string.Empty;
        public string WorkTelephone { get; set; } = string.Empty;
        public string CellPhone { get; set; } = string.Empty;
        public string Fax { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        // Marital Status & Partner / Huwelikstatus & Maat
        public string MaritalStatus { get; set; } = "Single"; // Single, Married, Divorced, Widow, Widower, Other
        public string MaritalStatusOtherSpecify { get; set; } = string.Empty;
        public string PartnerIdentificationType { get; set; } = "None"; // None, SA_ID, Passport
        public string PartnerIdNum { get; set; } = string.Empty;
        public string PartnerPassportNum { get; set; } = string.Empty;
        public string PartnerFullNameAndSurname { get; set; } = string.Empty;

        // Section 3: Travel & Purpose / Reis & Doeleindes
        public string CountryOfOrigin { get; set; } = string.Empty;
        public string CountryOfDestination { get; set; } = "South Africa";
        public string PortOfEntry { get; set; } = "O.R. Tambo International Airport (JNB)";
        public string PortOfExit { get; set; } = "O.R. Tambo International Airport (JNB)";
        public string ReasonForPermit { get; set; } = "Tydelike invoer vir jagdoeleindes (Hunting Safari)";
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public DateTime ArrivalDate { get; set; } = DateTime.Today.AddDays(14);
        public string FlightNumber { get; set; } = "SA204";
        public DateTime DepartureDate { get; set; } = DateTime.Today.AddDays(24);

        // Section 4: Firearm & Ammo (Line items)
        public string FirearmType { get; set; } = "Rifle";
        public string Calibre { get; set; } = ".30-06 Springfield";
        public string Make { get; set; } = "Mauser";
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = "M12345678";
        public int AmmoQuantity { get; set; } = 40;

        public List<FirearmItemModel> Firearms { get; set; } = new();
        public List<AmmunitionItemModel> Ammunition { get; set; } = new();

        // Section 5: Possession & Declaration / Verklaring
        public string PossessionName { get; set; } = string.Empty;
        public string PossessionPlace { get; set; } = string.Empty;
        public DateTime? PossessionDate { get; set; } = DateTime.Today;

        public string ApplicantDeclarationName { get; set; } = string.Empty;
        public string ApplicantDeclarationPlace { get; set; } = string.Empty;
        public DateTime? ApplicantDeclarationDate { get; set; } = DateTime.Today;

        public string Status { get; set; } = "Draft"; // Draft, Submitted, Verified
    }

    /// <summary>
    /// An AP-created advertisement. ImageUrl is a legacy single-image accessor kept for
    /// old call sites: reading it returns the first of ImageUrls (or a placeholder),
    /// writing it inserts into ImageUrls rather than replacing the whole list - new code
    /// should use ImageUrls directly. AdType is a plain alias for PlacementType (same
    /// backing value under two names).
    /// </summary>
    public class AdvertisementModel
    {
        public int Id { get; set; }
        public int AdvertiserUserId { get; set; }
        public string AdvertiserName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        
        public List<string> ImageUrls { get; set; } = new();
        public string ImageUrl
        {
            get => ImageUrls != null && ImageUrls.Count > 0 ? ImageUrls[0] : _imageUrl;
            set
            {
                _imageUrl = value ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    ImageUrls ??= new();
                    if (!ImageUrls.Contains(value)) ImageUrls.Insert(0, value);
                }
            }
        }
        private string _imageUrl = "images/savanna_sunrise.jpg";

        public string TargetUrl { get; set; } = string.Empty;
        public string PlacementType { get; set; } = "Banner";
        public string AdType { get => PlacementType; set => PlacementType = value; }
        public string? LongDescription { get; set; }
        public string AddressCity { get; set; } = string.Empty;
        public string AddressRegion { get; set; } = string.Empty;
        public string Category { get; set; } = "Gear";
        public string ContactNumber { get; set; } = string.Empty;
        public int DurationMonths { get; set; } = 1;
        public decimal MonthlyRate { get; set; } = 100m;
        public decimal TotalPaidAmount { get; set; } = 100m;
        public string PaymentStatus { get; set; } = "Pending Payment"; // "Pending Payment", "Betaal / Aktief", "Onaktief"
        public string? PaymentRef { get; set; }
        public DateTime? PaymentDate { get; set; }
        public DateTime? LastPaymentDate { get; set; }
        public DateTime StartDate { get; set; } = DateTime.Today;
        public DateTime EndDate { get; set; } = DateTime.Today.AddMonths(1);
        public bool IsActive { get; set; } = false;
        public int ClicksCount { get; set; } = 0;
        public int ImpressionsCount { get; set; } = 0;
        public int LastPaymentMonths { get; set; } = 1;
        public decimal LastPaymentAmount { get; set; } = 100m;
        public DateTime? LastPaymentStartDate { get; set; }
        public DateTime? LastPaymentEndDate { get; set; }
        public List<AdPaymentReceiptModel> PaymentHistory { get; set; } = new();
    }

    /// <summary>Local/UI-facing receipt entry for an ad's payment history (see AdPaymentRecord for the API's own DTO shape of the same concept).</summary>
    public class AdPaymentReceiptModel
    {
        public string PaymentRef { get; set; } = string.Empty;
        public DateTime PaymentDate { get; set; } = DateTime.Now;
        public int Months { get; set; } = 1;
        public decimal Amount { get; set; } = 100m;
        public DateTime PeriodStartDate { get; set; } = DateTime.Today;
        public DateTime PeriodEndDate { get; set; } = DateTime.Today.AddMonths(1);
    }

    /// <summary>API-shaped ad payment record (WebAPI's own DTO for the same data AdPaymentReceiptModel represents locally) - used when deserializing GetPaymentHistory responses.</summary>
    public class AdPaymentRecord
    {
        [System.Text.Json.Serialization.JsonPropertyName("paymentId")]
        public int PaymentId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("adId")]
        public int AdId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("paymentRef")]
        public string PaymentRef { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("paymentDate")]
        public DateTime PaymentDate { get; set; } = DateTime.UtcNow;

        [System.Text.Json.Serialization.JsonPropertyName("months")]
        public int Months { get; set; } = 1;

        [System.Text.Json.Serialization.JsonPropertyName("amount")]
        public decimal Amount { get; set; } = 100m;

        [System.Text.Json.Serialization.JsonPropertyName("periodStartDate")]
        public DateTime PeriodStartDate { get; set; } = DateTime.UtcNow;

        [System.Text.Json.Serialization.JsonPropertyName("periodEndDate")]
        public DateTime PeriodEndDate { get; set; } = DateTime.UtcNow.AddMonths(1);

        [System.Text.Json.Serialization.JsonPropertyName("paymentMethod")]
        public string PaymentMethod { get; set; } = "Simulated Gateway";

        [System.Text.Json.Serialization.JsonPropertyName("adTitle")]
        public string? AdTitle { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("advertiserName")]
        public string? AdvertiserName { get; set; }
    }

    /// <summary>Unused: no Razor page calls MikpuntApiService.GetMessagesAsync/SendMessageAsync(MessageModel) (Grep-verified). Legacy pre-chat-hub direct-message shape, superseded by ChatModels.Message + ChatHub/ChatsController.</summary>
    public class MessageModel
    {
        public int Id { get; set; }
        public int SenderUserId { get; set; }
        public string SenderName { get; set; } = string.Empty;
        public int ReceiverUserId { get; set; }
        public string ReceiverName { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; } = false;
        public string? AttachmentUrl { get; set; }
    }

    /// <summary>Generic PH/VG service-offering model (daily rate, region, included services) - a lighter-weight sibling of the role-specific pricing DTOs (PhDailyRatesModel etc).</summary>
    public class ServicePackageModel
    {
        public int Id { get; set; }
        public int ProviderUserId { get; set; }
        public string ProviderName { get; set; } = string.Empty;
        public string RoleCode { get; set; } = "PH";
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal DailyRate { get; set; }
        public string RegionCovered { get; set; } = "Limpopo";
        public List<string> ServicesIncluded { get; set; } = new();
        public string EquipmentProvided { get; set; } = string.Empty;
    }

    /// <summary>Public contact-form submission body, posted to WebAPI's ContactController.</summary>
    public class ContactRequestModel
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// POST body for WebAPI's UserController.Register - a single flattened DTO covering
    /// the base account fields plus every role-specific credential field for every role
    /// (PJ/PH/Boer/VG), since a registrant can select multiple roles at once. Uses
    /// snake_case JSON keys (unlike most other WebApp DTOs, which are camelCase) to match
    /// what the WebAPI endpoint expects here specifically.
    /// </summary>
    public class RegisterApiRequestModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("first_name")]
        public string FirstName { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("surname")]
        public string Surname { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("phone")]
        public string Phone { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("primary_role_code")]
        public string PrimaryRoleCode { get; set; } = "Jagter";

        [System.Text.Json.Serialization.JsonPropertyName("selected_roles")]
        public List<string> SelectedRoles { get; set; } = new();

        [System.Text.Json.Serialization.JsonPropertyName("terms_accepted")]
        public bool TermsAccepted { get; set; } = true;

        [System.Text.Json.Serialization.JsonPropertyName("profile_picture_url")]
        public string? ProfilePictureUrl { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("leaderboard_nickname")]
        public string LeaderboardNickname { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("passport_or_id_number")]
        public string PassportOrIdNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("pj_license_number")]
        public string PjLicenseNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("pj_license_expiry")]
        public string PjLicenseExpiry { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("pj_province_documents")]
        public string PjProvinceDocuments { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("cipc_reg_number")]
        public string CipcRegNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("tax_vat_number")]
        public string TaxVatNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("physical_address")]
        public string PhysicalAddress { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("ph_license_number")]
        public string PhLicenseNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("ph_issuing_province")]
        public string PhIssuingProvince { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("ph_license_expiry")]
        public string PhLicenseExpiry { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("dangerous_game_endorsement")]
        public string DangerousGameEndorsement { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("ph_association_name")]
        public string PhAssociationName { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("ph_association_member_number")]
        public string PhAssociationMemberNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("ph_association_document")]
        public string PhAssociationDocument { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("first_aid_cert_details")]
        public string FirstAidCertDetails { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("first_aid_expiry")]
        public string FirstAidExpiry { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("firearm_competency_details")]
        public string FirearmCompetencyDetails { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("drone_licenses")]
        public string DroneLicenses { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("media_qualifications")]
        public string MediaQualifications { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("farm_name")]
        public string FarmName { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("farm_region")]
        public string FarmRegion { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("farm_reg_number")]
        public string FarmRegNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("business_name")]
        public string BusinessName { get; set; } = string.Empty;
    }

    /// <summary>Response from Register - AccessToken is null when email verification is required before login (see WebAPI UserController.Register/RequiresVerification).</summary>
    public class RegisterApiResponseModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("success")]
        public bool Success { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }
    }

    /// <summary>POST body for WebAPI's UserController.SignIn.</summary>
    public class LoginApiRequestModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>Response from SignIn - carries both the access token and a refresh token, plus RequiresVerification when the account exists but hasn't confirmed its email yet.</summary>
    public class LoginApiResponseModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("success")]
        public bool Success { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("userId")]
        public int UserId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("roles")]
        public List<UserRoleModel>? Roles { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("requiresVerification")]
        public bool RequiresVerification { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }
    }

    /// <summary>POST body to trigger a password-reset email.</summary>
    public class ForgotPasswordRequestModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;
    }

    /// <summary>POST body to exchange a refresh token for a new access token.</summary>
    public class RefreshTokenRequestModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;
    }

    /// <summary>POST body to complete a password reset using the emailed token.</summary>
    public class ResetPasswordRequestModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("newPassword")]
        public string NewPassword { get; set; } = string.Empty;
    }

    /// <summary>Full profile as returned by WebAPI's GetProfile endpoint - the source AuthService.SyncUserFromProfile merges into CurrentUser.</summary>
    public class UserProfileApiResponseModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("userId")]
        public int UserId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("surname")]
        public string Surname { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("phone")]
        public string? Phone { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("idPassportNum")]
        public string? IdPassportNum { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("profilePictureUrl")]
        public string? ProfilePictureUrl { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("registered")]
        public DateTime Registered { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("roles")]
        public List<UserRoleModel> Roles { get; set; } = new();
    }

    /// <summary>Partial-update PATCH body for a profile - null fields are left unchanged server-side.</summary>
    public class UpdateProfileApiRequestModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("surname")]
        public string? Surname { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("phone")]
        public string? Phone { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("idPassportNum")]
        public string? IdPassportNum { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("profilePictureUrl")]
        public string? ProfilePictureUrl { get; set; }
    }

    /// <summary>POST body to add an additional role to an existing account (before the role-specific credential registration step).</summary>
    public class AddRoleApiRequestModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("roleCode")]
        public string RoleCode { get; set; } = string.Empty;
    }

    /// <summary>PJ (package organiser) role-credential registration body.</summary>
    public class RegisterPjApiRequestModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("businessName")]
        public string BusinessName { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("businessRegistrationNumber")]
        public string BusinessRegistrationNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("taxPinVat")]
        public string TaxPinVat { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("physicalAddress")]
        public string PhysicalAddress { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("provincialLicenses")]
        public string ProvincialLicenses { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("yearsOfExperience")]
        public int YearsOfExperience { get; set; } = 5;

        [System.Text.Json.Serialization.JsonPropertyName("bio")]
        public string Bio { get; set; } = string.Empty;
    }

    /// <summary>PH (professional hunter) role-credential registration body.</summary>
    public class RegisterPhApiRequestModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("provincialLicenses")]
        public string ProvincialLicenses { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("hasDangerousGameEndorsement")]
        public bool HasDangerousGameEndorsement { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dangerousGameDetails")]
        public string DangerousGameDetails { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("associationName")]
        public string AssociationName { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("associationMembershipNumber")]
        public string AssociationMembershipNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("firstAidCertification")]
        public string FirstAidCertification { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("firstAidExpiryDate")]
        public DateTime? FirstAidExpiryDate { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("firearmCompetencyLicenses")]
        public string FirearmCompetencyLicenses { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("yearsOfExperience")]
        public int YearsOfExperience { get; set; } = 5;

        [System.Text.Json.Serialization.JsonPropertyName("bio")]
        public string Bio { get; set; } = string.Empty;
    }

    /// <summary>Boer (farmer) role-credential registration body.</summary>
    public class RegisterBoerApiRequestModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("businessName")]
        public string BusinessName { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("businessRegistrationNumber")]
        public string BusinessRegistrationNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("region")]
        public string Region { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("bio")]
        public string Bio { get; set; } = string.Empty;
    }

    /// <summary>VG (videographer/drone operator) role-credential registration body.</summary>
    public class RegisterVgApiRequestModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("hasDrone")]
        public bool HasDrone { get; set; } = true;

        [System.Text.Json.Serialization.JsonPropertyName("sacaaRegistrationNumber")]
        public string SacaaRegistrationNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("rplLicenseNumber")]
        public string RplLicenseNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("rocCertificateNumber")]
        public string RocCertificateNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("droneLicence")]
        public string DroneLicence { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("companyName")]
        public string CompanyName { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("companyRegistrationNumber")]
        public string CompanyRegistrationNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("yearsOfExperience")]
        public int YearsOfExperience { get; set; } = 3;
    }

    /// <summary>Jagter (hunter) role-credential registration body.</summary>
    public class RegisterHunterApiRequestModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("country")]
        public string Country { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("idPassportNumber")]
        public string IdPassportNumber { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("bio")]
        public string Bio { get; set; } = string.Empty;
    }

    /// <summary>Shape persisted into browser storage by AuthService.SaveSessionToStorageAsync/read back by InitializeAsync.</summary>
    public class UserSessionModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("user")]
        public UserModel? User { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;
    }

    /// <summary>Full advertisement record as returned by WebAPI's AdsController - the API-shaped counterpart to AdvertisementModel.</summary>
    public class AdResponseModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("apId")]
        public int ApId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("userId")]
        public int? UserId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("advertiserName")]
        public string? AdvertiserName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("advertiserEmail")]
        public string? AdvertiserEmail { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("advertiserPhone")]
        public string? AdvertiserPhone { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string? Description { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("longDescription")]
        public string? LongDescription { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string? Type { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("category")]
        public string? Category { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("price")]
        public decimal? Price { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("createdDate")]
        public DateTime CreatedDate { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("expiryDate")]
        public DateTime? ExpiryDate { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("views")]
        public int Views { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("clicks")]
        public int Clicks { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("imagesJson")]
        public string? ImagesJson { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("contactNumber")]
        public string? ContactNumber { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("targetUrl")]
        public string? TargetUrl { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("paymentRef")]
        public string? PaymentRef { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("lastPaymentMonths")]
        public int? LastPaymentMonths { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("lastPaymentAmount")]
        public decimal? LastPaymentAmount { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("lastPaymentDate")]
        public DateTime? LastPaymentDate { get; set; }
    }

    /// <summary>Response after creating an ad. Both Id and AdvertisementId carry the same new ad id (kept for call-site naming flexibility); Success confirms the create actually took.</summary>
    public class CreateAdResponseModel
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("advertisementId")]
        public int AdvertisementId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("success")]
        public bool Success { get; set; }
    }


    /// <summary>Response after creating a hunting package - just the new package id.</summary>
    public class CreatePackageResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }
    }

    /// <summary>POST body to create a hunting package (PJ role). "P"-prefixed property names mirror the WebAPI's own packet ("Pakket") column naming.</summary>
    public class CreatePackageRequest
    {
        public string PTitle { get; set; } = string.Empty;
        public string PDescription { get; set; } = string.Empty;
        public decimal PStockPrice { get; set; }
        public int PDurationDays { get; set; }
        public int PMaxParticipants { get; set; }
        public string PIncludedItems { get; set; } = string.Empty;
        public DateTime? PAvailableFrom { get; set; }
        public DateTime? PAvailableTill { get; set; }
        public int? PFarmId { get; set; }
        public int? PPjId { get; set; }
        public int? PMinExpeditionLength { get; set; }
        public int? PMinGuests { get; set; }
        public int? PMaxGuests { get; set; }
        public string PStatus { get; set; } = "Active";
        /// <summary>What the price includes, one item per line.</summary>
        public string? PInclusions { get; set; }
        /// <summary>What is paid separately, one item per line.</summary>
        public string? PExclusions { get; set; }
    }

    /// <summary>Partial-update PATCH body for a package - null fields are left unchanged server-side.</summary>
    public class UpdatePackageRequest
    {
        public string? PTitle { get; set; }
        public string? PDescription { get; set; }
        public decimal? PStockPrice { get; set; }
        public int? PDurationDays { get; set; }
        public int? PMaxParticipants { get; set; }
        public string? PIncludedItems { get; set; }
        public string? PStatus { get; set; }
        public DateTime? PAvailableFrom { get; set; }
        public DateTime? PAvailableTill { get; set; }
        public int? PMinExpeditionLength { get; set; }
        public int? PMinGuests { get; set; }
        public int? PMaxGuests { get; set; }
        public int? PFarmId { get; set; }
        public string? PInclusions { get; set; }
        public string? PExclusions { get; set; }
    }

    /// <summary>Full package record as returned by WebAPI (Package_db) - the API-shaped counterpart to HuntingPackageModel.</summary>
    public class PackageResponse
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal StockPrice { get; set; }
        public int DurationDays { get; set; }
        public int MaxParticipants { get; set; }
        public string IncludedItems { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime? AvailableFrom { get; set; }
        public DateTime? AvailableTill { get; set; }
        public int? FarmId { get; set; }
        public int? PjId { get; set; }
        public int? MinExpeditionLength { get; set; }
        public int? MinGuests { get; set; }
        public int? MaxGuests { get; set; }
        public string FarmName { get; set; } = string.Empty;
        public string Province { get; set; } = "Limpopo";
        public string Location { get; set; } = string.Empty;
        public string OrganiserName { get; set; } = string.Empty;
        public int OrganiserUserId { get; set; }
        public bool FarmerDocumentsApproved { get; set; } = true;
        public bool OrganiserDocumentsApproved { get; set; } = true;
        /// <summary>"Ingesluit": one item per line.</summary>
        public string Inclusions { get; set; } = string.Empty;
        /// <summary>"Nie ingesluit nie": one item per line.</summary>
        public string Exclusions { get; set; } = string.Empty;
    }

    /// <summary>One row in the "browse team roles" marketplace (public PH/VG/etc. listings hunters/PJs can search) - a blend of profile info, per-role pricing, and subscription/active-period status gating visibility.</summary>
    public class BrowseProfileResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("userRoleId")]
        public int UserRoleId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("userId")]
        public int UserId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("surname")]
        public string? Surname { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("email")]
        public string? Email { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("roleCode")]
        public string? RoleCode { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("roleName")]
        public string? RoleName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("businessName")]
        public string? BusinessName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("bio")]
        public string? Bio { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("yearsExperience")]
        public long YearsExperience { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("phone")]
        public string? Phone { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("location")]
        public string? Location { get; set; }

        public decimal? StandardDailyRate { get; set; }
        public decimal? DangerousGameDailyRate { get; set; }
        public bool HasDangerousGameAccreditation { get; set; } = true;
        public bool IsSubscriptionActive { get; set; } = true;
        public DateTime? SubscriptionExpiryDate { get; set; }
        public bool HasPaidActivePeriod => SubscriptionExpiryDate.HasValue && SubscriptionExpiryDate.Value > DateTime.UtcNow;
        public string SubscriptionTier { get; set; } = "Standard Advertiser";
    }

    /// <summary>A PayFast form the server priced and signed (api/payments/checkout) - posted as-is to ProcessUrl.</summary>
    public class PaymentCheckoutModel
    {
        public string ProcessUrl { get; set; } = string.Empty;
        public List<PaymentFieldModel> Fields { get; set; } = new();
        public decimal Amount { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int Months { get; set; }
        /// <summary>True only when the WebAPI runs in Development and allows api/payments/simulate.</summary>
        public bool SimulatorAvailable { get; set; }
    }

    /// <summary>One hidden field of a PayFast form.</summary>
    public class PaymentFieldModel
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>Response of api/payments/simulate.</summary>
    public class PaymentSimulationModel
    {
        public bool Success { get; set; }
        public bool AlreadyPaid { get; set; }
        public string? Reference { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>A role's paid-subscription state (PH/Boer/PJ/VG roles require an active subscription to appear publicly). HasPaidActivePeriod/DaysRemaining are computed from ExpiryDate, not stored separately.</summary>
    public class RoleSubscriptionModel
    {
        public int UserId { get; set; }
        public string RoleCode { get; set; } = "PH"; // PH, Boer, PJ, VG
        public bool IsActive { get; set; } = true;
        public decimal MonthlyFee { get; set; } = 299m;
        public string TierName { get; set; } = "Standard Advertiser";
        public DateTime ExpiryDate { get; set; } = DateTime.Today.AddMonths(1);
        public DateTime LastPaidDate { get; set; } = DateTime.Today;
        public string PaymentStatus { get; set; } = "Active";
        public int ActiveDurationMonths { get; set; } = 1;
        public decimal? LastPaidAmount { get; set; }
        public string? LastPaymentReference { get; set; }

        public bool HasPaidActivePeriod => ExpiryDate > DateTime.UtcNow;
        public int DaysRemaining => HasPaidActivePeriod ? Math.Max(0, (int)Math.Ceiling((ExpiryDate - DateTime.UtcNow).TotalDays)) : 0;
    }

    /// <summary>A PH's configured daily rates. StandardDailyRate/DangerousGameDailyRate being null means "not configured yet" and must never be defaulted to a fallback number (see field comment below).</summary>
    public class PhDailyRatesModel
    {
        public int UserId { get; set; }
        // Null means the PH has not configured this tariff yet - never substitute a
        // default here, the PH must actively set a real value.
        public decimal? StandardDailyRate { get; set; }
        public decimal? DangerousGameDailyRate { get; set; }
        public bool HasDangerousGameAccreditation { get; set; } = true;
        public string Currency { get; set; } = "ZAR";
    }

    /// <summary>API-shaped team invite record (counterpart to TeamInviteModel) as returned by TeamController's invite-listing endpoints.</summary>
    public class TeamInviteResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("inviteId")]
        public int InviteId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pjUserRoleId")]
        public int PjUserRoleId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pjUserId")]
        public int PjUserId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pjName")]
        public string? PjName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pjSurname")]
        public string? PjSurname { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pjEmail")]
        public string? PjEmail { get; set; }

        /// <summary>Expedition / season the PJ is recruiting for.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("expeditionName")]
        public string? ExpeditionName { get; set; }

        /// <summary>The offer (rates, dates, conditions) attached to the invite.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("terms")]
        public string? Terms { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("targetUserId")]
        public int TargetUserId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("targetName")]
        public string? TargetName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("targetRoleCode")]
        public string? TargetRoleCode { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("status")]
        public string? Status { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("inviteDate")]
        public DateTime InviteDate { get; set; }
    }

    /// <summary>POST body for a PJ sending a team invite to a PH/VG.</summary>
    public class SendInviteRequest
    {
        public int PjUserId { get; set; }
        public int MemberUserRoleId { get; set; }
        public int TargetUserId { get; set; }
        public string? ExpeditionName { get; set; }
        public string? Terms { get; set; }
    }

    /// <summary>One accepted team member (PH/VG) under a PJ, as shown on ManageTeam.razor.</summary>
    public class TeamMemberResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public int Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("memberUserRoleId")]
        public int MemberUserRoleId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("userId")]
        public int UserId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string? Name { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("surname")]
        public string? Surname { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("role")]
        public string? Role { get; set; }
    }

    /// <summary>Summary used to drive the header bell icon (badge count + whether notifications are enabled for this account).</summary>
    public class NotificationStatus
    {
        [System.Text.Json.Serialization.JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("unreadCount")]
        public int UnreadCount { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("lastNotified")]
        public DateTime LastNotified { get; set; }
    }

    /// <summary>Full booking record as returned by WebAPI's BookingsController - the API-shaped counterpart to BookingModel.</summary>
    public class BookingResponse
    {
        public int Id { get; set; }
        public int PakketId { get; set; }
        public string PakketName { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public DateTime BookingDate { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public int HuntGuests { get; set; }
        public int NonHuntGuests { get; set; }
        public string HunterEmail { get; set; } = string.Empty;
        public string HunterPhone { get; set; } = string.Empty;
        public int? AssignedPhUserId { get; set; }
        public string? AssignedPhName { get; set; }
        public int? AssignedVgUserId { get; set; }
        public string? AssignedVgName { get; set; }
        public string FarmName { get; set; } = string.Empty;
        public string Province { get; set; } = "Limpopo";
        public string? PaymentReference { get; set; }
        public string? RejectionReason { get; set; }
        public string? RejectionNotes { get; set; }
        public string? FirearmOption { get; set; }
        public string? SpecialNotes { get; set; }
        public int? FarmId { get; set; }
        public decimal? ConfirmedAccommodationPrice { get; set; }
        public string? PriceBreakdownJson { get; set; }
        public bool WantsVideographer { get; set; }
        public List<string> BookedSpecies { get; set; } = new();
        public int? PjUserId { get; set; }
        public string? PjName { get; set; }
        public string? PhAssignmentStatus { get; set; }
        public string? VgAssignmentStatus { get; set; }
        public string? PhDeclineReason { get; set; }
        public string? VgDeclineReason { get; set; }
    }

    /// <summary>PH/VG's response (accept/decline) to a staff-assignment offer on a booking.</summary>
    public class RespondStaffAssignmentRequest
    {
        public string Role { get; set; } = string.Empty;
        public bool Accept { get; set; }
        public string? DeclineReason { get; set; }
    }

    /// <summary>DTO sent to api/bookings/create.</summary>
    public class CreateBookingRequest
    {
        public int PakketId { get; set; }
        public int HunterId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int HuntGuests { get; set; } = 1;
        public int NonHuntGuests { get; set; } = 0;
        public string? FirearmOption { get; set; }
        public string? SpecialNotes { get; set; }
        public bool WantsVideographer { get; set; }
        public List<string>? BookedSpecies { get; set; }
    }

    /// <summary>PJ's request to remove a team member; ConfirmReassignment must be explicitly set once the caller has reviewed the conflicts a prior RemovalConflictCheckResponse surfaced.</summary>
    public class RemoveTeamMemberRequest
    {
        public int TeamId { get; set; }
        public string ReasonCode { get; set; } = string.Empty;
        public string? ReasonDescription { get; set; }
        public string? Notes { get; set; }
        public bool ConfirmReassignment { get; set; }
    }

    /// <summary>Pre-flight check before removing a team member: flags any active/upcoming bookings that member is assigned to, so the PJ can confirm reassignment before the removal proceeds.</summary>
    public class RemovalConflictCheckResponse
    {
        public int TeamId { get; set; }
        public int MemberUserId { get; set; }
        public string MemberName { get; set; } = string.Empty;
        public string MemberRole { get; set; } = string.Empty;
        public bool CanRemoveDirectly { get; set; }
        public bool HasActiveBookingConflict { get; set; }
        public bool HasUpcomingBookingConflict { get; set; }
        public List<BookingConflictDto> ActiveBookings { get; set; } = new();
        public List<BookingConflictDto> UpcomingBookings { get; set; } = new();
        public string? Message { get; set; }
    }

    /// <summary>One conflicting booking surfaced by RemovalConflictCheckResponse.</summary>
    public class BookingConflictDto
    {
        public int BookingId { get; set; }
        public string PackageTitle { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string HunterName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    /// <summary>One selectable reason code/description pair for the team-member-removal reason dropdown.</summary>
    public class RemovalReasonOptionDto
    {
        public string Code { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>Result of a completed team-member removal - lists any bookings that were left unassigned as a consequence.</summary>
    public class TeamRemovalResult
    {
        public bool Success { get; set; }
        public int TeamId { get; set; }
        public int RemovedMemberUserId { get; set; }
        public string RemovedMemberName { get; set; } = string.Empty;
        public int UnassignedBookingCount { get; set; }
        public List<int> UnassignedBookingIds { get; set; } = new();
        public string? Message { get; set; }
    }

    /// <summary>One in-app bell notification, as returned/updated by TeamController's notification endpoints.</summary>
    public class UserNotificationDto
    {
        public int NotificationId { get; set; }
        public int RecipientUserId { get; set; }
        public int? SenderUserId { get; set; }
        public string NotificationType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? ActionUrl { get; set; }
        public bool IsRead { get; set; }
        public DateTime CreatedTimestamp { get; set; } = DateTime.UtcNow;
    }
    /// <summary>A page of PackageResponse results (API-response shape) plus paging metadata.</summary>
    public class PaginatedPackageResponse
    {
        public List<PackageResponse> Data { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
    /// <summary>A page of HuntingPackageModel results (client-model shape) plus paging metadata - mapped from PaginatedPackageResponse by MikpuntApiService.</summary>
    public class PaginatedPackageModel
    {
        public List<HuntingPackageModel> Data { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    // --- System Administrator Module DTOs ---
    /// <summary>One entry in the admin audit log (every admin action - approvals, suspensions, corrections - is recorded here for accountability).</summary>
    public class AdminAuditLogModel
    {
        public int Id { get; set; }
        public int AdminUserId { get; set; }
        public string AdminUserName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string TargetEntityType { get; set; } = string.Empty;
        public string TargetEntityId { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>One pending role-credential verification request in the admin's approval queue (PH/Boer/VG role applications, distinct from the per-document verification queue - see PendingDocumentVerificationModel).</summary>
    public class RoleVerificationRequestModel
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string RequestedRoleCode { get; set; } = string.Empty; // 'PH', 'Boer', 'VG'
        public string RequestedRoleName { get; set; } = string.Empty;
        public string DocumentName { get; set; } = string.Empty;
        public string DocumentReference { get; set; } = string.Empty;
        public string? DocumentUrl { get; set; }
        public string Status { get; set; } = "Pending"; // 'Pending', 'Approved', 'Rejected'
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public string? RejectionReason { get; set; }
    }

    /// <summary>An admin-toggleable feature flag (system-wide on/off switch).</summary>
    public class FeatureFlagModel
    {
        public string Key { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; } = "System Admin";
    }

    /// <summary>Admin dashboard's system health panel data, populated from the real api/admin/health endpoint (backed by SqlServerHealthCheck); the property defaults here only apply to the local-fallback instance MikpuntApiService.GetSystemHealthAsync returns if that call throws.</summary>
    public class SystemHealthModel
    {
        /// <summary>False when the WebAPI didn't answer at all - every other value is then unknown.</summary>
        public bool Reachable { get; set; }
        public bool DbConnected { get; set; }
        public string DbStatus { get; set; } = "Onbekend";
        public int DbResponseTimeMs { get; set; }
        public bool ApiUptimeHealthy { get; set; }
        /// <summary>Website -> WebAPI round trip for this health request.</summary>
        public int ResponseTimeMs { get; set; }
        public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
        /// <summary>Website sign-ins with a live refresh token on this WebAPI instance (-1 = unknown).</summary>
        public int ActiveSessions { get; set; }
        /// <summary>Users signed in to the app (unexpired app refresh token; -1 = unknown).</summary>
        public int AppSessions { get; set; }
        public bool MobileApiHealthy { get; set; }
        public string MobileApiStatus { get; set; } = "Onbekend";
        public int MobileApiResponseMs { get; set; }
        public bool MaintenanceMode { get; set; }
        public bool RegistrationsOpen { get; set; } = true;
        public bool PayFastSandbox { get; set; } = true;
        public string Environment { get; set; } = string.Empty;
    }

    /// <summary>api/settings/status: what every visitor's page needs from the admin flags.</summary>
    public class SiteStatusModel
    {
        public bool Maintenance { get; set; }
        public bool RegistrationsOpen { get; set; } = true;
        public string Message { get; set; } = "MikPunt is tydelik in onderhoud. Probeer asseblief later weer.";
        [System.Text.Json.Serialization.JsonIgnore] public DateTime LoadedAt { get; set; }
    }

    /// <summary>One chat message flagged by moderation keywords, in the admin's moderation queue.</summary>
    public class FlaggedChatMessageModel
    {
        public int Id { get; set; }
        public int SenderUserId { get; set; }
        public string SenderUserName { get; set; } = string.Empty;
        public int RecipientUserId { get; set; }
        public string RecipientUserName { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
        public string FlaggedKeywords { get; set; } = string.Empty;
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "PendingReview"; // 'PendingReview', 'Dismissed', 'ActionTaken'
    }

    /// <summary>One rating comment flagged by moderation keywords, in the admin's moderation queue.</summary>
    public class FlaggedRatingCommentModel
    {
        public int Id { get; set; }
        public int RatingId { get; set; }
        public int RaterUserId { get; set; }
        public string RaterUserName { get; set; } = string.Empty;
        public int RateeUserId { get; set; }
        public string RateeUserName { get; set; } = string.Empty;
        public string CommentText { get; set; } = string.Empty;
        public string FlaggedKeywords { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = "PendingReview"; // 'PendingReview', 'Dismissed', 'ActionTaken'
    }

    /// <summary>One raw admin-editable key/value system setting row (see PublicSettingsModel for the strongly-typed, public-facing view built from these).</summary>
    public class SystemSettingModel
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsPublic { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        public string UpdatedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// Public/business pricing settings, admin-configurable, safe to show to any
    /// visitor deciding whether to advertise/subscribe/book. Built client-side from
    /// the raw api/settings/public rows so pages can bind to named properties instead
    /// of looking up keys by string everywhere.
    /// </summary>
    public class PublicSettingsModel
    {
        public decimal AdMonthlyPrice { get; set; } = 100m;
        public decimal DepositPercentage { get; set; } = 100m;
        /// <summary>Rand per US dollar: what a US$ quote is charged at by PayFast.</summary>
        public decimal UsdToZarRate { get; set; } = 18m;
        /// <summary>Package prices and new quotes in rand (admin setting PackagePricesInZar = 1).</summary>
        public bool PackagePricesInZar { get; set; }
        public Dictionary<string, decimal> SubscriptionPrices { get; set; } = new();

        public decimal SubscriptionPriceFor(string roleCode) =>
            SubscriptionPrices.TryGetValue(roleCode, out var price) ? price : 299m;
    }

    /// <summary>
    /// Real post-trip rating stats for a profile/dashboard card - average stars,
    /// how many ratings that's based on, and how many completed trips the user has
    /// (a fact about bookings, not derived from ratings). RatingCount == 0 means
    /// "no ratings yet", distinct from a real average of 0.
    /// </summary>
    public class RatingProfileStatsModel
    {
        public decimal AverageStars { get; set; }
        public int RatingCount { get; set; }
        public int CompletedTrips { get; set; }
    }

    /// <summary>One rating shown on a profile's recent-ratings list.</summary>
    public class RecentRatingModel
    {
        public int RatingId { get; set; }
        public string RaterName { get; set; } = string.Empty;
        public int Stars { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; }
        public string RoleContext { get; set; } = string.Empty;
    }

    /// <summary>One edit-history entry for a rating (rating comments/stars can be edited/disputed; this tracks each change).</summary>
    public class RatingHistoryEntryModel
    {
        public int HistoryId { get; set; }
        public string ChangeType { get; set; } = string.Empty;
        public int RatingId { get; set; }
        public int? PreviousStars { get; set; }
        public string? PreviousComment { get; set; }
        public int NewStars { get; set; }
        public string? NewComment { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    /// <summary>A rating disputed by its recipient, in the admin's dispute-review queue.</summary>
    public class RatingDisputeModel
    {
        public int DisputeId { get; set; }
        public int RatingId { get; set; }
        public int DisputedByUserId { get; set; }
        public string DisputedByUserName { get; set; } = string.Empty;
        public int RaterUserId { get; set; }
        public string RaterUserName { get; set; } = string.Empty;
        public int Stars { get; set; }
        public string? Comment { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = "PendingReview";
        public DateTime CreatedAt { get; set; }
        public int? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ResolutionNotes { get; set; }
    }

    /// <summary>Whether a given document type is required for a role (admin-configurable via roleDocumentRequirements), independent of any specific user's upload state.</summary>
    public class DocumentRequirementModel
    {
        public int DocumentTypeId { get; set; }
        public string DocumentTypeName { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
    }

    /// <summary>Full document record including the file itself - only ever fetched by the owner or an admin.</summary>
    public class UserDocumentModel
    {
        public int Id { get; set; }
        public int UserRoleId { get; set; }
        public int DocumentTypeId { get; set; }
        public string DocumentTypeName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileDataUrl { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        public int? UploadedByUserId { get; set; }
        public string UploadedByName { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public string Status { get; set; } = "Pending"; // 'Pending', 'Approved', 'Rejected'
        public DateTime? VerifiedAt { get; set; }
        public string? VerifiedByName { get; set; }
        public string? RejectionReason { get; set; }
    }

    /// <summary>Cross-role profile view: whether a credential exists, never the file itself.</summary>
    public class UserDocumentSummaryModel
    {
        public int DocumentTypeId { get; set; }
        public string DocumentTypeName { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public bool IsUploaded { get; set; }
        public DateTime? UploadedAt { get; set; }
        public string? Status { get; set; }
    }

    /// <summary>One entry in a document's audit trail (upload/verify/reject actions), shown on the document manager and admin verification views.</summary>
    public class DocumentAuditEntryModel
    {
        public int LogId { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public string DocumentTypeName { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public int ActorUserId { get; set; }
        public string ActorName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>One row in the admin's document-verification queue.</summary>
    public class PendingDocumentVerificationModel
    {
        public int Id { get; set; }
        public int UserRoleId { get; set; }
        public int OwnerUserId { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public string OwnerEmail { get; set; } = string.Empty;
        public string RoleCode { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public int DocumentTypeId { get; set; }
        public string DocumentTypeName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileDataUrl { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        public bool IsRequired { get; set; }
        public string Status { get; set; } = "Pending";
        public DateTime? VerifiedAt { get; set; }
        public string? RejectionReason { get; set; }
    }

    /// <summary>Admin request to approve/reject/suspend/ban a target user, with a mandatory reason recorded to the audit log.</summary>
    public class AdminUserActionRequest
    {
        public int TargetUserId { get; set; }
        public string ActionType { get; set; } = string.Empty; // 'Approve', 'Reject', 'Suspend', 'Ban'
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>Admin request to manually correct a disputed booking's price/guest counts/status - the dispute-resolution counterpart to the normal approve/reject flow.</summary>
    public class AdminCorrectBookingRequest
    {
        public int BookingId { get; set; }
        public decimal CorrectedTotalPrice { get; set; }
        public decimal? CorrectedAccommodationTotal { get; set; }
        public int CorrectedHunterCount { get; set; }
        public int CorrectedObserverCount { get; set; }
        public string Status { get; set; } = "Approved";
        public string Reason { get; set; } = string.Empty;
    }
}
