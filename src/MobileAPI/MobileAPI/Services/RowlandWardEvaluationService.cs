using MobileApi.Domain;
using MobileApi.Models.RowlandWard;

namespace MobileApi.Services;

public sealed class RowlandWardEvaluationService : IRowlandWardEvaluationService
{
    private const string PublishedState = "published";
    private const string KilogramUnit = "kilogram";
    private const string PoundUnit = "pound";
    private static readonly ExactRational KilogramsPerPound =
        new(45_359_237, 100_000_000);

    private readonly IRowlandWardCatalogueService _catalogue;

    public RowlandWardEvaluationService(IRowlandWardCatalogueService catalogue)
    {
        _catalogue = catalogue;
    }

    public RowlandWardEvaluationSnapshot Evaluate(
        RowlandWardEvaluationRequest request,
        DateTime? evaluatedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(request.ClassId))
        {
            throw new RowlandWardEvaluationException(
                "Kies asseblief 'n geldige Rowland Ward-wildspesie."
            );
        }

        var recordClass = _catalogue.GetRecordClass(request.ClassId.Trim())
            ?? throw new RowlandWardEvaluationException(
                "Geen goedgekeurde Rowland Ward-rekordklas is gevind nie."
            );

        if (!string.IsNullOrWhiteSpace(request.SourceVersionId) &&
            !string.Equals(
                request.SourceVersionId.Trim(),
                recordClass.SourceVersionId,
                StringComparison.Ordinal))
        {
            throw new RowlandWardEvaluationException(
                "Die gekose Rowland Ward-standaardweergawe is nie beskikbaar nie."
            );
        }

        var requirements = _catalogue.GetMeasurementRequirements(recordClass.Id)
            ?? throw new RowlandWardEvaluationException(
                "Geen Rowland Ward-metings is vir hierdie rekordklas beskikbaar nie."
            );
        var profile = requirements.Profile;
        var submitted = request.Measurements ?? new Dictionary<string, RowlandWardMeasurementInput>();
        var fieldsById = profile.Fields.ToDictionary(field => field.Id, StringComparer.Ordinal);

        var unexpectedField = submitted.Keys.FirstOrDefault(fieldId => !fieldsById.ContainsKey(fieldId));
        if (unexpectedField is not null)
        {
            throw new RowlandWardEvaluationException(
                $"Die meting '{unexpectedField}' word nie vir hierdie wildspesie gebruik nie."
            );
        }

        var measurements = new List<RowlandWardMeasurementSnapshot>();
        var scoringValues = new Dictionary<string, ExactRational>(StringComparer.Ordinal);

        foreach (var field in profile.Fields)
        {
            if (!submitted.TryGetValue(field.Id, out var input))
            {
                continue;
            }

            measurements.Add(ProcessMeasurement(field, input, scoringValues));
        }

        var missingFields = profile.Fields
            .Where(field => field.Role.StartsWith("required_", StringComparison.Ordinal))
            .Where(field => !submitted.ContainsKey(field.Id))
            .Select(field => field.Id)
            .ToList();

        var minimumSnapshot = CreateReferenceSnapshot(recordClass.Minimum);
        var worldRecordSnapshot = CreateReferenceSnapshot(recordClass.WorldRecord);
        var evaluation = new RowlandWardEvaluationSnapshot
        {
            ClassId = recordClass.Id,
            DisplayNameAf = recordClass.DisplayNameAf,
            MethodCode = recordClass.MethodCode,
            MeasurementProfileId = recordClass.MeasurementProfileId,
            SourceVersionId = recordClass.SourceVersionId,
            EditionReleaseIdentity = _catalogue.SourceVersion.EditionReleaseIdentity,
            SourceMasterSha256 = _catalogue.SourceVersion.MasterSha256,
            EvaluatedAtUtc = (evaluatedAtUtc ?? DateTime.UtcNow).ToUniversalTime(),
            MinimumAtEvaluation = minimumSnapshot,
            WorldRecordAtEvaluation = worldRecordSnapshot,
            ScoreExpression = CloneExpression(profile.ScoreExpression),
            MinimumComparisonExpression = CloneExpression(profile.MinimumComparisonExpression),
            MinimumComparisonOperator = profile.MinimumComparisonOperator,
            Measurements = measurements,
            MissingMeasurementFieldIds = missingFields
        };

        if (missingFields.Count > 0)
        {
            evaluation.QualificationState =
                RowlandWardQualificationStates.RequiredMeasurementsMissing;
            evaluation.QualificationMessageAf = "Vul asseblief al die vereiste Rowland Ward-metings in.";
            AttachCurrentWorldRecord(evaluation);
            return evaluation;
        }

        var score = EvaluateExpression(profile.ScoreExpression, scoringValues);
        var comparisonValue = EvaluateExpression(
            profile.MinimumComparisonExpression,
            scoringValues
        );
        evaluation.Score = CreateEvaluatedQuantity(score, profile.ScoreUnitCode);

        if (recordClass.NoMinimum || recordClass.Minimum.State != PublishedState)
        {
            evaluation.QualificationState = RowlandWardQualificationStates.NoPublishedMinimum;
            evaluation.MinimumReached = null;
            evaluation.QualificationMessageAf = null;
        }
        else
        {
            var minimum = ExactRational.From(recordClass.Minimum.Quantity!.Value);
            var reached = Compare(
                comparisonValue,
                minimum,
                profile.MinimumComparisonOperator
            );
            evaluation.MinimumReached = reached;
            evaluation.QualificationState = reached
                ? RowlandWardQualificationStates.MinimumReached
                : RowlandWardQualificationStates.MinimumNotReached;
            evaluation.QualificationMessageAf = reached
                ? "Rowland Ward"
                : null;
        }

        AttachCurrentWorldRecord(evaluation);
        return evaluation;
    }

    public void AttachCurrentWorldRecord(RowlandWardEvaluationSnapshot evaluation)
    {
        var currentClass = _catalogue.GetRecordClass(evaluation.ClassId);
        if (currentClass is null || currentClass.NoRecord ||
            currentClass.WorldRecord.State != PublishedState)
        {
            evaluation.CurrentWorldRecord = new RowlandWardReferenceValueSnapshot
            {
                State = currentClass?.WorldRecord.State ?? "not_available"
            };
            evaluation.CurrentWorldRecordSourceVersionId = currentClass?.SourceVersionId;
            return;
        }

        evaluation.CurrentWorldRecord = CreateReferenceSnapshot(
            currentClass.WorldRecord
        );
        evaluation.CurrentWorldRecordSourceVersionId = currentClass.SourceVersionId;
    }

    public RowlandWardPublicEvaluation ToPublicEvaluation(
        RowlandWardEvaluationSnapshot evaluation)
    {
        AttachCurrentWorldRecord(evaluation);

        return new RowlandWardPublicEvaluation
        {
            ClassId = evaluation.ClassId,
            DisplayNameAf = evaluation.DisplayNameAf,
            MethodCode = evaluation.MethodCode,
            SourceVersionId = evaluation.SourceVersionId,
            Score = evaluation.Score?.Display,
            QualificationState = evaluation.QualificationState,
            MinimumReached = evaluation.MinimumReached == true,
            QualificationMessageAf = evaluation.MinimumReached == true
                ? "Rowland Ward"
                : null,
            WorldRecord = evaluation.CurrentWorldRecord?.Quantity?.Display,
            WorldRecordSourceVersionId = evaluation.CurrentWorldRecord?.Quantity is null
                ? null
                : evaluation.CurrentWorldRecordSourceVersionId
        };
    }

    private static RowlandWardMeasurementSnapshot ProcessMeasurement(
        RowlandWardMeasurementField field,
        RowlandWardMeasurementInput input,
        IDictionary<string, ExactRational> scoringValues)
    {
        if (field.Kind == "rational")
        {
            return ProcessRationalMeasurement(field, input, scoringValues);
        }

        if (field.Kind == "integer")
        {
            if (input.IntegerValue is null || input.IntegerValue < 0 ||
                input.Value is not null || input.BooleanValue is not null)
            {
                throw InvalidMeasurement(field);
            }

            return new RowlandWardMeasurementSnapshot
            {
                FieldId = field.Id,
                LabelAf = field.LabelAf,
                Kind = field.Kind,
                Role = field.Role,
                IntegerValue = input.IntegerValue
            };
        }

        if (field.Kind == "boolean")
        {
            if (input.BooleanValue is null || input.Value is not null ||
                input.IntegerValue is not null)
            {
                throw InvalidMeasurement(field);
            }

            return new RowlandWardMeasurementSnapshot
            {
                FieldId = field.Id,
                LabelAf = field.LabelAf,
                Kind = field.Kind,
                Role = field.Role,
                BooleanValue = input.BooleanValue
            };
        }

        throw new RowlandWardEvaluationException(
            $"Die meting '{field.LabelAf}' het 'n onbekende soort waarde."
        );
    }

    private static RowlandWardMeasurementSnapshot ProcessRationalMeasurement(
        RowlandWardMeasurementField field,
        RowlandWardMeasurementInput input,
        IDictionary<string, ExactRational> scoringValues)
    {
        if (input.Value is null || input.Value.Denominator <= 0 ||
            input.Value.Numerator < 0 || input.IntegerValue is not null ||
            input.BooleanValue is not null)
        {
            throw InvalidMeasurement(field);
        }

        var expectedInputUnit = RowlandWardUnitLabels.GetApplicationInputUnit(field) ?? field.UnitCode;
        if (string.IsNullOrWhiteSpace(expectedInputUnit) ||
            !string.Equals(input.UnitCode, expectedInputUnit, StringComparison.Ordinal))
        {
            throw new RowlandWardEvaluationException(
                $"Gebruik {RowlandWardUnitLabels.GetAfrikaans(expectedInputUnit)} vir '{field.LabelAf}'."
            );
        }

        var inputValue = ExactRational.From(input.Value);
        var canonicalValue = inputValue;
        var scoringValue = inputValue;
        var canonicalUnit = field.UnitCode!;

        if (expectedInputUnit == KilogramUnit && canonicalUnit == PoundUnit)
        {
            canonicalValue = inputValue / KilogramsPerPound;
            scoringValue = canonicalValue.RoundHalfUpToWhole();
        }
        else if (field.Increment is not null &&
                 !inputValue.IsWholeMultipleOf(ExactRational.From(field.Increment)))
        {
            throw new RowlandWardEvaluationException(
                $"'{field.LabelAf}' moet in stappe van {FormatIncrement(field.Increment)} {RowlandWardUnitLabels.GetAfrikaans(expectedInputUnit)} ingevul word."
            );
        }

        scoringValues[field.Id] = scoringValue;
        return new RowlandWardMeasurementSnapshot
        {
            FieldId = field.Id,
            LabelAf = field.LabelAf,
            Kind = field.Kind,
            Role = field.Role,
            InputQuantity = CreateQuantity(inputValue, expectedInputUnit),
            CanonicalQuantity = CreateQuantity(canonicalValue, canonicalUnit),
            ScoringQuantity = CreateQuantity(scoringValue, canonicalUnit)
        };
    }

    private static ExactRational EvaluateExpression(
        RowlandWardExpression? expression,
        IReadOnlyDictionary<string, ExactRational> values)
    {
        if (expression is null || expression.Fields.Count == 0)
        {
            throw new RowlandWardEvaluationException(
                "Die Rowland Ward-telmetode is nie beskikbaar nie."
            );
        }

        var operands = expression.Fields.Select(fieldId =>
        {
            if (!values.TryGetValue(fieldId, out var value))
            {
                throw new RowlandWardEvaluationException(
                    "Vul asseblief al die vereiste Rowland Ward-metings in."
                );
            }

            return value;
        }).ToList();

        return expression.Operator switch
        {
            "identity" when operands.Count == 1 => operands[0],
            "max" => operands.Aggregate((left, right) => left.CompareTo(right) >= 0 ? left : right),
            "sum" => operands.Aggregate(ExactRational.Zero, (total, value) => total + value),
            _ => throw new RowlandWardEvaluationException(
                "Die Rowland Ward-telmetode word nie ondersteun nie."
            )
        };
    }

    private static bool Compare(
        ExactRational value,
        ExactRational minimum,
        string comparisonOperator)
    {
        return comparisonOperator switch
        {
            "greater_than_or_equal" => value.CompareTo(minimum) >= 0,
            _ => throw new RowlandWardEvaluationException(
                "Die Rowland Ward-vergelykingsmetode word nie ondersteun nie."
            )
        };
    }

    private static RowlandWardReferenceValueSnapshot CreateReferenceSnapshot(
        RowlandWardPublishedValue source)
    {
        if (source.State != PublishedState || source.Quantity is null)
        {
            return new RowlandWardReferenceValueSnapshot { State = source.State };
        }

        var canonical = ExactRational.From(source.Quantity.Value);
        return new RowlandWardReferenceValueSnapshot
        {
            State = source.State,
            Quantity = CreateEvaluatedQuantity(canonical, source.Quantity.UnitCode)
        };
    }

    private static RowlandWardEvaluatedQuantity CreateEvaluatedQuantity(
        ExactRational canonicalValue,
        string canonicalUnit)
    {
        var displayValue = canonicalValue;
        var displayUnit = canonicalUnit;
        if (canonicalUnit == PoundUnit)
        {
            displayValue = canonicalValue * KilogramsPerPound;
            displayUnit = KilogramUnit;
        }

        return new RowlandWardEvaluatedQuantity
        {
            Canonical = CreateQuantity(canonicalValue, canonicalUnit),
            Display = CreateQuantity(displayValue, displayUnit)
        };
    }

    private static RowlandWardQuantitySnapshot CreateQuantity(
        ExactRational value,
        string unitCode) => new()
        {
            Value = value.ToContractValue(),
            UnitCode = unitCode,
            UnitLabelAf = RowlandWardUnitLabels.GetAfrikaans(unitCode)
        };

    private static string FormatIncrement(RowlandWardRational increment) =>
        increment.Denominator == 1
            ? increment.Numerator.ToString()
            : $"{increment.Numerator}/{increment.Denominator}";

    private static RowlandWardEvaluationException InvalidMeasurement(
        RowlandWardMeasurementField field) => new(
            $"Voer 'n geldige waarde vir '{field.LabelAf}' in."
        );

    private static RowlandWardExpression CloneExpression(RowlandWardExpression? expression) =>
        expression is null
            ? new RowlandWardExpression()
            : new RowlandWardExpression
            {
                Operator = expression.Operator,
                Fields = [.. expression.Fields]
            };
}
