namespace MobileApi.Models;

public class MobileBookingResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FarmName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string HunterName { get; set; } = string.Empty;
    public int HuntGuests { get; set; }
    public int NonHuntGuests { get; set; }

    public int? FarmId { get; set; }
    public decimal? FarmAverageRating { get; set; }
    public int FarmReviewCount { get; set; }
    public bool CanRateFarm { get; set; }
    public int? MyFarmRatingStars { get; set; }
    public string? MyFarmRatingDescription { get; set; }

    /// <summary>
    /// User id of the farm owner, so the app can start a chat about the
    /// booking. Null when the package isn't linked to a farmer.
    /// </summary>
    public int? FarmOwnerUserId { get; set; }

    /// <summary>
    /// True when the caller booked (and pays for) this trip; false when a main hunter
    /// added them to it - HunterName is then the main hunter's name ("Saam met ...").
    /// </summary>
    public bool IsMainHunter { get; set; } = true;

    /// <summary>The booking's hunter count, main hunter included.</summary>
    public int MaxHunters { get; set; }

    /// <summary>Hunters on the booking so far: the main hunter plus added (or invited) hunters.</summary>
    public int HunterCount { get; set; }

    /// <summary>The caller is the main hunter and the booking is confirmed, so they may add/remove hunters.</summary>
    public bool CanManageHunters { get; set; }
}
