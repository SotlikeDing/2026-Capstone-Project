using System.Text.Json.Serialization;
using MobileApi.Models.RowlandWard;

namespace MobileApi.Models;

public class PublicTrophySummary
{
    public int Id { get; set; }
    public string PhotoPath { get; set; } = string.Empty;
    public string Species { get; set; } = string.Empty;
    public decimal? AntlerSizeCm { get; set; }
    public decimal? CarcassWeightKg { get; set; }
    public int? ShotDistanceMeters { get; set; }
    public string FirearmType { get; set; } = string.Empty;
    public DateTime HarvestDate { get; set; }
    public string? OwnerDisplayName { get; set; }
    public RowlandWardPublicEvaluation? RowlandWard { get; set; }

    [JsonIgnore]
    public RowlandWardEvaluationSnapshot? StoredRowlandWardEvaluation { get; set; }
}

public class PublicTrophyDetail : PublicTrophySummary
{
    public string? Notes { get; set; }
    public DateTime CreatedTimestamp { get; set; }
}

public class LeaderboardTrophySummary : PublicTrophySummary
{
    public decimal? SelectedSortValue { get; set; }
}

