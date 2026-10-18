using MobileApi.Models.RowlandWard;

namespace MobileApi.Models;

public class Trophy
{
    public int Id { get; set; }
    public int OwnerUserId { get; set; }
    public string Species { get; set; } = string.Empty;
    public string PhotoPath { get; set; } = string.Empty;
    public decimal? AntlerSizeCm { get; set; }
    public decimal? CarcassWeightKg { get; set; }
    public int? ShotDistanceMeters { get; set; }
    public string FirearmType { get; set; } = string.Empty;
    public DateTime HarvestDate { get; set; }
    public string LocationText { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsPublic { get; set; }
    public DateTime CreatedTimestamp { get; set; }
    public DateTime UpdatedTimestamp { get; set; }
    public RowlandWardEvaluationSnapshot? RowlandWard { get; set; }
}

public class TrophySaveRequest
{
    public string Species { get; set; } = string.Empty;
    public string PhotoPath { get; set; } = string.Empty;
    public decimal? AntlerSizeCm { get; set; }
    public decimal? CarcassWeightKg { get; set; }
    public int? ShotDistanceMeters { get; set; }
    public string FirearmType { get; set; } = string.Empty;
    public DateTime HarvestDate { get; set; }
    public string LocationText { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsPublic { get; set; }
    public RowlandWardEvaluationRequest? RowlandWard { get; set; }
}

