using System.Text.Json;

namespace MobileApi.Models.RowlandWard;

public sealed class RowlandWardCatalogueDocument
{
    public int SchemaVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ActiveSourceVersionId { get; set; } = string.Empty;
    public RowlandWardSourceVersion SourceVersion { get; set; } = new();
    public int RecordClassCount { get; set; }
    public List<RowlandWardCategory> Categories { get; set; } = [];
    public List<RowlandWardRecordClass> RecordClasses { get; set; } = [];
}

public sealed class RowlandWardSourceVersion
{
    public string Id { get; set; } = string.Empty;
    public string EditionReleaseIdentity { get; set; } = string.Empty;
    public int EditionNumber { get; set; }
    public string Region { get; set; } = string.Empty;
    public bool Active { get; set; }
    public string ApprovalStatus { get; set; } = string.Empty;
    public string HistoricalRecalculationPolicy { get; set; } = string.Empty;
    public string MasterDocumentId { get; set; } = string.Empty;
    public string MasterSha256 { get; set; } = string.Empty;
}

public sealed class RowlandWardCategory
{
    public string Id { get; set; } = string.Empty;
    public string DisplayNameAf { get; set; } = string.Empty;
}

public sealed class RowlandWardRecordClass
{
    public string Id { get; set; } = string.Empty;
    public string SourceVersionId { get; set; } = string.Empty;
    public string DisplayNameAf { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string LatinName { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string SourceCategory { get; set; } = string.Empty;
    public string CategoryAf { get; set; } = string.Empty;
    public string MethodCode { get; set; } = string.Empty;
    public string MeasurementProfileId { get; set; } = string.Empty;
    public RowlandWardPublishedValue Minimum { get; set; } = new();
    public bool NoMinimum { get; set; }
    public RowlandWardPublishedValue WorldRecord { get; set; } = new();
    public bool NoRecord { get; set; }
    public RowlandWardComparison Comparison { get; set; } = new();
    public List<string> Aliases { get; set; } = [];
    public JsonElement ApplicationDecision { get; set; }
    public JsonElement Provenance { get; set; }
}

public sealed class RowlandWardPublishedValue
{
    public string State { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
    public RowlandWardSourceQuantity? Quantity { get; set; }
    public RowlandWardSourceQuantity? SourceMetricQuantity { get; set; }
    public string? SourceText { get; set; }
}

public sealed class RowlandWardSourceQuantity
{
    public RowlandWardRational Value { get; set; } = new();
    public string UnitCode { get; set; } = string.Empty;
    public string SourceText { get; set; } = string.Empty;
}

public sealed class RowlandWardRational
{
    public long Numerator { get; set; }
    public long Denominator { get; set; }
}

public sealed class RowlandWardComparison
{
    public RowlandWardExpression? ScoreExpression { get; set; }
    public RowlandWardExpression? MinimumComparisonExpression { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string UnitCode { get; set; } = string.Empty;
}

public sealed class RowlandWardExpression
{
    public string Operator { get; set; } = string.Empty;
    public List<string> Fields { get; set; } = [];
}

public sealed class RowlandWardMethodsDocument
{
    public int SchemaVersion { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SourceVersionId { get; set; } = string.Empty;
    public List<RowlandWardMeasurementMethod> Methods { get; set; } = [];
}

public sealed class RowlandWardMeasurementMethod
{
    public string Code { get; set; } = string.Empty;
    public string RuntimeStatus { get; set; } = string.Empty;
    public string SourceTitle { get; set; } = string.Empty;
    public List<RowlandWardMeasurementProfile> MeasurementProfiles { get; set; } = [];
    public JsonElement ApplicationPolicy { get; set; }
    public JsonElement Provenance { get; set; }
}

public sealed class RowlandWardMeasurementProfile
{
    public string Id { get; set; } = string.Empty;
    public List<RowlandWardMeasurementField> Fields { get; set; } = [];
    public RowlandWardExpression? ScoreExpression { get; set; }
    public string ScoreUnitCode { get; set; } = string.Empty;
    public RowlandWardExpression? MinimumComparisonExpression { get; set; }
    public string MinimumComparisonOperator { get; set; } = string.Empty;
    public RowlandWardExpression? RankingExpression { get; set; }
    public string? RankingDirection { get; set; }
    public string? TieBreakRule { get; set; }
    public string? TieBreakStatus { get; set; }
    public string RoundingPolicy { get; set; } = string.Empty;
}

public sealed class RowlandWardMeasurementField
{
    public string Id { get; set; } = string.Empty;
    public string LabelAf { get; set; } = string.Empty;
    public string SourceSymbol { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? UnitCode { get; set; }
    public RowlandWardRational? Increment { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Visibility { get; set; } = string.Empty;
    public JsonElement? ApplicationInput { get; set; }

    public string InputUnitCode => RowlandWardUnitLabels.GetApplicationInputUnit(this)
        ?? UnitCode
        ?? string.Empty;

    public string InputUnitLabelAf => RowlandWardUnitLabels.GetAfrikaans(InputUnitCode);
}

public static class RowlandWardUnitLabels
{
    public static string GetAfrikaans(string? unitCode) => unitCode switch
    {
        "inch" => "duim",
        "kilogram" => "kg",
        "pound" => "pond",
        "foot" => "voet",
        "count" => "aantal",
        _ => unitCode ?? string.Empty
    };

    public static string? GetApplicationInputUnit(RowlandWardMeasurementField field)
    {
        var input = field.ApplicationInput;
        if (input is null || input.Value.ValueKind != JsonValueKind.Object ||
            !input.Value.TryGetProperty("unitCode", out var unit))
        {
            return null;
        }

        return unit.GetString();
    }
}

public sealed record RowlandWardCategorySummary(
    string Id,
    string DisplayNameAf,
    int RecordClassCount
);

public sealed record RowlandWardRecordClassSummary(
    string Id,
    string DisplayNameAf,
    string SourceName,
    string LatinName,
    string CategoryId,
    string CategoryAf,
    string MethodCode,
    string MeasurementProfileId,
    RowlandWardPublishedValue Minimum,
    bool NoMinimum,
    RowlandWardPublishedValue WorldRecord,
    bool NoRecord
)
{
    public static RowlandWardRecordClassSummary From(RowlandWardRecordClass recordClass) => new(
        recordClass.Id,
        recordClass.DisplayNameAf,
        recordClass.SourceName,
        recordClass.LatinName,
        recordClass.CategoryId,
        recordClass.CategoryAf,
        recordClass.MethodCode,
        recordClass.MeasurementProfileId,
        recordClass.Minimum,
        recordClass.NoMinimum,
        recordClass.WorldRecord,
        recordClass.NoRecord
    );
}

public sealed record RowlandWardSpeciesSuggestion(
    string ClassId,
    string DisplayNameAf,
    string LatinName,
    string CategoryId,
    string MethodCode,
    int Confidence
);

public sealed record RowlandWardSpeciesMatchResult(
    string Outcome,
    string NormalizedInput,
    RowlandWardRecordClassSummary? Match,
    IReadOnlyList<RowlandWardSpeciesSuggestion> Suggestions,
    bool RequiresSelection,
    string? MessageAf
);

public sealed record RowlandWardMeasurementRequirementsResponse(
    string ClassId,
    string DisplayNameAf,
    string MethodCode,
    RowlandWardMeasurementProfile Profile
);
