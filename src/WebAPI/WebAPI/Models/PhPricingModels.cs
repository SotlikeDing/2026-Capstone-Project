using System;

namespace WebAPI.Models
{
    /// <summary>Response shape for a PH pricing entry.</summary>
    public class PhPricingResponse
    {
        public int Id { get; set; }
        public string GameType { get; set; } = "";
        public int HunterCount { get; set; }
        public decimal Price { get; set; }
        public string Status { get; set; } = "Aktief";
    }

    /// <summary>Payload for creating a PH pricing entry - PhId is actually the PH's userId, not their ph table id (see PhPricing_db).</summary>
    public class CreatePhPricingRequest
    {
        public int PhId { get; set; }
        public string GameType { get; set; } = "";
        public int HunterCount { get; set; }
        public decimal Price { get; set; }
    }

    /// <summary>Response shape for a VG package.</summary>
    public class VgPackageResponse
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string RegionCovered { get; set; } = "";
        public string EquipmentProvided { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Price { get; set; }
        public string Status { get; set; } = "Aktief";
    }

    /// <summary>Payload for creating a VG package - VgId is actually the VG's userId, not their vg table id (see VgPackages_db).</summary>
    public class CreateVgPackageRequest
    {
        public int VgId { get; set; }
        public string Title { get; set; } = "";
        public string RegionCovered { get; set; } = "";
        public string EquipmentProvided { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal Price { get; set; }
    }

    /// <summary>A role's subscription/billing status (Profile_db.GetRoleSubscription/SaveRoleSubscription/ProcessSubscriptionPayment).</summary>
    public class RoleSubscriptionDto
    {
        public int UserId { get; set; }
        public string RoleCode { get; set; } = "PH";
        public bool IsActive { get; set; }
        public decimal MonthlyFee { get; set; } = 299m;
        public string TierName { get; set; } = "Standard Advertiser";
        public DateTime? ExpiryDate { get; set; }
        public DateTime? LastPaidDate { get; set; }
        public string PaymentStatus { get; set; } = "Unpaid";
        public int ActiveDurationMonths { get; set; }
        public decimal? LastPaidAmount { get; set; }
        public string? LastPaymentReference { get; set; }
        public bool HasPaidActivePeriod => ExpiryDate.HasValue && ExpiryDate.Value > DateTime.UtcNow;
    }

    /// <summary>A PH's configured day rates.</summary>
    public class PhDailyRatesDto
    {
        public int UserId { get; set; }
        // Null means the PH has not yet configured this rate - callers must not
        // silently substitute a default, the PH has to actively set it.
        public decimal? StandardDailyRate { get; set; }
        public decimal? DangerousGameDailyRate { get; set; }
        // Defaults true - both here and in Profile_db.GetPhDailyRates's NULL-column
        // fallback, a PH with no accreditation data on record reads as accredited.
        public bool HasDangerousGameAccreditation { get; set; } = true;
        public string Currency { get; set; } = "ZAR";
    }

    /// <summary>Payload for crediting a subscription payment (see Profile_db.ProcessSubscriptionPayment).</summary>
    public class ProcessSubscriptionPaymentRequest
    {
        public int UserId { get; set; }
        public string RoleCode { get; set; } = "PH";
        public int DurationMonths { get; set; } = 1;
        public decimal Amount { get; set; }
        public string PaymentReference { get; set; } = "";
    }
}
