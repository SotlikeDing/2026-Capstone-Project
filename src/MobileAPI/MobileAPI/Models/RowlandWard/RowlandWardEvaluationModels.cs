using System.Text.Json.Serialization;

namespace MobileApi.Models.RowlandWard;

public static class RowlandWardQualificationStates
{
    public const string MinimumReached = "MINIMUM_REACHED";
    public const string MinimumNotReached = "MINIMUM_NOT_REACHED";
    public const string NoPublishedMinimum = "NO_PUBLISHED_MINIMUM";
    public const string RequiredMeasurementsMissing = "REQUIRED_MEASUREMENTS_MISSING";
}

public sealed class RowlandWardEvaluationRequest
{
    public string ClassId { get; set; } = string.Empty;
    public string? SourceVersionId { get; set; }
    public Dictionary<string, RowlandWardMeasurementInput> Measurements { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed class RowlandWardMeasurementInput
{
    public string? UnitCode { get; set; }
    public RowlandWardRational? Value { get; set; }
    public long? IntegerValue { get; set; }
    public bool? BooleanValue { get; set; }
}

public sealed class RowlandWardQuantitySnapshot
{
    public RowlandWardRational Value { get; set; } = new();
    public string UnitCode { get; set; } = string.Empty;
    public string UnitLabelAf { get; set; } = string.Empty;
}

public sealed class RowlandWardEvaluatedQuantity
{
    public RowlandWardQuantitySnapshot Canonical { get; set; } = new();
    public RowlandWardQuantitySnapshot Display { get; set; } = new();
}

public sealed class RowlandWardReferenceValueSnapshot
{
    public string State { get; set; } = string.Empty;
    public RowlandWardEvaluatedQuantity? Quantity { get; set; }
}

public sealed class RowlandWardMeasurementSnapshot
{
    public string FieldId { get; set; } = string.Empty;
    public string LabelAf { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public RowlandWardQuantitySnapshot? InputQuantity { get; set; }
    public RowlandWardQuantitySnapshot? CanonicalQuantity { get; set; }
    public RowlandWardQuantitySnapshot? ScoringQuantity { get; set; }
    public long? IntegerValue { get; set; }
    public bool? BooleanValue { get; set; }
}

public sealed class RowlandWardEvaluationSnapshot
{
    [JsonIgnore]
    public long EvaluationId { get; set; }

    public int SchemaVersion { get; set; } = 1;
    public string ClassId { get; set; } = string.Empty;
    public string DisplayNameAf { get; set; } = string.Empty;
    public string MethodCode { get; set; } = string.Empty;
    public string MeasurementProfileId { get; set; } = string.Empty;
    public string SourceVersionId { get; set; } = string.Empty;
    public string EditionReleaseIdentity { get; set; } = string.Empty;
    public string? SourceMasterSha256 { get; set; }
    public DateTime EvaluatedAtUtc { get; set; }
    public RowlandWardEvaluatedQuantity? Score { get; set; }
    public RowlandWardReferenceValueSnapshot MinimumAtEvaluation { get; set; } = new();
    public RowlandWardReferenceValueSnapshot WorldRecordAtEvaluation { get; set; } = new();
    public RowlandWardReferenceValueSnapshot? CurrentWorldRecord { get; set; }
    public string? CurrentWorldRecordSourceVersionId { get; set; }
    public string QualificationState { get; set; } = string.Empty;
    public bool? MinimumReached { get; set; }
    public string? QualificationMessageAf { get; set; }
    public List<string> MissingMeasurementFieldIds { get; set; } = [];
    public List<RowlandWardMeasurementSnapshot> Measurements { get; set; } = [];

    [JsonIgnore]
    public RowlandWardExpression ScoreExpression { get; set; } = new();

    [JsonIgnore]
    public RowlandWardExpression MinimumComparisonExpression { get; set; } = new();

    [JsonIgnore]
    public string MinimumComparisonOperator { get; set; } = string.Empty;
}

public sealed class RowlandWardPublicEvaluation
{
    public string ClassId { get; set; } = string.Empty;
    public string DisplayNameAf { get; set; } = string.Empty;
    public string MethodCode { get; set; } = string.Empty;
    public string SourceVersionId { get; set; } = string.Empty;
    public RowlandWardQuantitySnapshot? Score { get; set; }
    public string QualificationState { get; set; } = string.Empty;
    public bool MinimumReached { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QualificationMessageAf { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RowlandWardQuantitySnapshot? WorldRecord { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorldRecordSourceVersionId { get; set; }
}

public sealed class RowlandWardEvaluationException : Exception
{
    public RowlandWardEvaluationException(string message) : base(message)
    {
    }
}
