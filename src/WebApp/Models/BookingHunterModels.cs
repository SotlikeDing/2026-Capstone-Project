namespace WebApp.Models
{
    // Client-side mirrors of WebAPI/Models/BookingCompanionModels.cs (camelCase JSON, web defaults bind it).

    /// <summary>One hunter on a booking: the main hunter (pays) or an added hunter.</summary>
    public class BookingHunterModel
    {
        public int CompanionId { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>Only sent to the main hunter / an admin.</summary>
        public string? Email { get; set; }

        /// <summary>"Main", "Linked" or "Pending" (invited, not registered/verified yet).</summary>
        public string Status { get; set; } = "Linked";
        public bool IsMainHunter { get; set; }
        public bool IsYou { get; set; }
    }

    public class BookingHuntersModel
    {
        public int BookingId { get; set; }
        public string BookingStatus { get; set; } = string.Empty;
        public int MaxHunters { get; set; }
        public int OpenSlots { get; set; }
        public bool CanManage { get; set; }
        public bool CallerIsMainHunter { get; set; }
        public List<BookingHunterModel> Hunters { get; set; } = new();
    }

    /// <summary>A trip the user joins as an added hunter. Has no price/payment fields by design.</summary>
    public class CompanionTripModel
    {
        public int Id { get; set; }
        public string PackageTitle { get; set; } = string.Empty;
        public string FarmName { get; set; } = string.Empty;
        public string Province { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string MainHunterName { get; set; } = string.Empty;
        public int HuntGuests { get; set; }
        public int NonHuntGuests { get; set; }
        public string? FirearmOption { get; set; }
        public string? PhName { get; set; }
        public string? VgName { get; set; }
        public string? PjName { get; set; }
        public List<string> BookedSpecies { get; set; } = new();
    }
}
