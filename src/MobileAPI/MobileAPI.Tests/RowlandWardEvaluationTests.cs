using System.Text.Json;
using MobileApi.Models.RowlandWard;
using MobileApi.Services;

namespace MobileAPI.Tests;

public sealed class RowlandWardEvaluationTests
{
    private static readonly Lazy<IRowlandWardCatalogueService> Catalogue = new(CreateCatalogue);
    private static readonly Lazy<IRowlandWardEvaluationService> Evaluator = new(
        () => new RowlandWardEvaluationService(Catalogue.Value)
    );

    [Fact]
    public void EveryApprovedProductionClass_HasAnExecutableExactMinimumEvaluation()
    {
        foreach (var recordClass in Catalogue.Value.RecordClasses)
        {
            var evaluation = Evaluator.Value.Evaluate(CreateAtMinimumRequest(recordClass.Id));

            Assert.Equal(RowlandWardQualificationStates.MinimumReached, evaluation.QualificationState);
            Assert.True(evaluation.MinimumReached);
            Assert.Equal(recordClass.MethodCode, evaluation.MethodCode);
        }
    }

    [Theory]
    [InlineData("blesbok", "7-a", 33, 2)]
    [InlineData("south_african_bushbuck", "8", 15, 1)]
    [InlineData("cape_buffalo", "12-a", 64, 1)]
    [InlineData("brindled_gnu_or_blue_wildebeest", "13-a", 57, 2)]
    [InlineData("white_tailed_gnu_or_black_wildebeest", "13-b", 23, 1)]
    [InlineData("savanna_elephant_one_tusk", "17", 70, 1)]
    [InlineData("savanna_elephant_both_tusks", "17", 120, 1)]
    [InlineData("african_leopard", "18", 61, 4)]
    [InlineData("nile_crocodile_body", "19", 27, 2)]
    public void ApprovedMethod_UsesVerifiedExpressionAndQualifiesExactlyAtMinimum(
        string classId,
        string methodCode,
        long minimumNumerator,
        long minimumDenominator)
    {
        var evaluation = Evaluator.Value.Evaluate(CreateAtMinimumRequest(classId));

        Assert.Equal(methodCode, evaluation.MethodCode);
        Assert.Equal(RowlandWardQualificationStates.MinimumReached, evaluation.QualificationState);
        Assert.True(evaluation.MinimumReached);
        Assert.Equal("Rowland Ward", evaluation.QualificationMessageAf);
        AssertRational(
            evaluation.Score!.Canonical.Value,
            minimumNumerator,
            minimumDenominator
        );
        Assert.Equal(Catalogue.Value.SourceVersion.Id, evaluation.SourceVersionId);
        Assert.Equal(
            "31st Edition — Winter of 2026–2027",
            evaluation.EditionReleaseIdentity
        );
    }

    [Fact]
    public void CapeBuffalo_Method12a_SumsBothBossesAndGreatestSpreadOnly()
    {
        var request = CreateCompleteRequest("cape_buffalo");
        request.Measurements["boss_width_left"] = Rational(81, 8, "inch");
        request.Measurements["boss_width_right"] = Rational(79, 8, "inch");
        request.Measurements["greatest_spread"] = Rational(44, 1, "inch");
        request.Measurements["tip_to_tip"] = Rational(100, 1, "inch");

        var evaluation = Evaluator.Value.Evaluate(request);

        AssertRational(evaluation.Score!.Canonical.Value, 64, 1);
        Assert.True(evaluation.MinimumReached);
    }

    [Fact]
    public void BlueWildebeest_Method13a_UsesGreatestSpreadGWithoutTypicality()
    {
        var request = CreateCompleteRequest("brindled_gnu_or_blue_wildebeest");
        request.Measurements["base_circumference_left"] = Rational(100, 1, "inch");
        request.Measurements["base_circumference_right"] = Rational(100, 1, "inch");
        request.Measurements["greatest_spread"] = Rational(57, 2, "inch");

        var evaluation = Evaluator.Value.Evaluate(request);

        Assert.Equal("Blouwildebees", evaluation.DisplayNameAf);
        AssertRational(evaluation.Score!.Canonical.Value, 57, 2);
        Assert.DoesNotContain(evaluation.Measurements, item => item.Kind == "boolean");
    }

    [Fact]
    public void BlackWildebeest_Method13b_UsesTheLongerHorn()
    {
        var request = CreateCompleteRequest("white_tailed_gnu_or_black_wildebeest");
        request.Measurements["horn_length_left"] = Rational(181, 8, "inch");
        request.Measurements["horn_length_right"] = Rational(23, 1, "inch");

        var evaluation = Evaluator.Value.Evaluate(request);

        AssertRational(evaluation.Score!.Canonical.Value, 23, 1);
        Assert.True(evaluation.MinimumReached);
    }

    [Fact]
    public void ExactBoundary_JustBelowHasNoNegativeMessage_AndJustAboveQualifies()
    {
        var below = CreateCompleteRequest("brindled_gnu_or_blue_wildebeest");
        below.Measurements["greatest_spread"] = Rational(227, 8, "inch");
        var above = CreateCompleteRequest("brindled_gnu_or_blue_wildebeest");
        above.Measurements["greatest_spread"] = Rational(229, 8, "inch");

        var belowResult = Evaluator.Value.Evaluate(below);
        var aboveResult = Evaluator.Value.Evaluate(above);

        Assert.Equal(RowlandWardQualificationStates.MinimumNotReached, belowResult.QualificationState);
        Assert.False(belowResult.MinimumReached);
        Assert.Null(belowResult.QualificationMessageAf);
        Assert.Equal(RowlandWardQualificationStates.MinimumReached, aboveResult.QualificationState);
        Assert.True(aboveResult.MinimumReached);
    }

    [Fact]
    public void FractionIncrement_IsValidatedExactlyWithoutFloatingPoint()
    {
        var request = CreateCompleteRequest("blesbok");
        request.Measurements["horn_length_left"] = Rational(265, 16, "inch");

        var exception = Assert.Throws<RowlandWardEvaluationException>(
            () => Evaluator.Value.Evaluate(request)
        );

        Assert.Contains("stappe van 1/8 duim", exception.Message);
    }

    [Fact]
    public void MissingRequiredMeasurement_ReturnsExplicitIncompleteState()
    {
        var request = CreateAtMinimumRequest("cape_buffalo");
        request.Measurements.Remove("tip_to_tip");

        var evaluation = Evaluator.Value.Evaluate(request);

        Assert.Equal(
            RowlandWardQualificationStates.RequiredMeasurementsMissing,
            evaluation.QualificationState
        );
        Assert.Null(evaluation.Score);
        Assert.Null(evaluation.MinimumReached);
        Assert.Contains("tip_to_tip", evaluation.MissingMeasurementFieldIds);
    }

    [Fact]
    public void ElephantKgInput_IsConvertedExactlyAndRoundedHalfUpAtBoundary()
    {
        var belowRequest = CreateCompleteRequest("savanna_elephant_one_tusk");
        belowRequest.Measurements["tusk_weight_left"] = PoundsAsKilograms(200_999, 2_000);
        belowRequest.Measurements["tusk_weight_right"] = PoundsAsKilograms(0, 1);
        var halfRequest = CreateCompleteRequest("savanna_elephant_one_tusk");
        halfRequest.Measurements["tusk_weight_left"] = PoundsAsKilograms(201, 2);
        halfRequest.Measurements["tusk_weight_right"] = PoundsAsKilograms(0, 1);

        var below = Evaluator.Value.Evaluate(belowRequest);
        var half = Evaluator.Value.Evaluate(halfRequest);

        AssertRational(below.Score!.Canonical.Value, 100, 1);
        AssertRational(half.Score!.Canonical.Value, 101, 1);
        var weight = half.Measurements.Single(item => item.FieldId == "tusk_weight_left");
        Assert.Equal("kilogram", weight.InputQuantity!.UnitCode);
        AssertRational(weight.CanonicalQuantity!.Value, 201, 2);
        AssertRational(weight.ScoringQuantity!.Value, 101, 1);
        Assert.Equal("kg", half.Score.Display.UnitLabelAf);
        AssertRational(half.Score.Display.Value, 4_581_282_937, 100_000_000);
    }

    [Fact]
    public void NoMinimumAndNoRecord_AreExplicitAndNeverProducePositiveQualification()
    {
        var evaluator = new RowlandWardEvaluationService(new MissingReferenceCatalogue());
        var evaluation = evaluator.Evaluate(new RowlandWardEvaluationRequest
        {
            ClassId = "synthetic_no_minimum",
            Measurements = new Dictionary<string, RowlandWardMeasurementInput>
            {
                ["score"] = Rational(10, 1, "inch")
            }
        });
        var publicValue = evaluator.ToPublicEvaluation(evaluation);
        var json = JsonSerializer.Serialize(publicValue, new JsonSerializerOptions(
            JsonSerializerDefaults.Web
        ));

        Assert.Equal(RowlandWardQualificationStates.NoPublishedMinimum, evaluation.QualificationState);
        Assert.Null(evaluation.MinimumReached);
        Assert.Null(evaluation.QualificationMessageAf);
        Assert.Equal("not_published", evaluation.CurrentWorldRecord!.State);
        Assert.False(publicValue.MinimumReached);
        Assert.DoesNotContain("worldRecord", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qualificationMessageAf", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PublicProjection_ExposesScoreRecordAndPositiveResultButNeverComponents()
    {
        var evaluation = Evaluator.Value.Evaluate(CreateAtMinimumRequest("cape_buffalo"));

        var publicValue = Evaluator.Value.ToPublicEvaluation(evaluation);
        var json = JsonSerializer.Serialize(publicValue, new JsonSerializerOptions(
            JsonSerializerDefaults.Web
        ));

        Assert.NotNull(publicValue.Score);
        Assert.NotNull(publicValue.WorldRecord);
        Assert.True(publicValue.MinimumReached);
        Assert.Contains("qualificationMessageAf", json);
        Assert.DoesNotContain("measurements", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("boss_width", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluation_IsIndependentOfDryingDatesAndShotDistance()
    {
        var evaluation = Evaluator.Value.Evaluate(CreateAtMinimumRequest("blesbok"));
        var json = JsonSerializer.Serialize(evaluation, new JsonSerializerOptions(
            JsonSerializerDefaults.Web
        ));

        Assert.True(evaluation.MinimumReached);
        Assert.DoesNotContain("dry", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shotDistance", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EarlierEvaluationObject_IsNotRecalculatedByASecondEvaluation()
    {
        var firstRequest = CreateCompleteRequest("brindled_gnu_or_blue_wildebeest");
        firstRequest.Measurements["greatest_spread"] = Rational(227, 8, "inch");
        var first = Evaluator.Value.Evaluate(firstRequest, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var second = Evaluator.Value.Evaluate(
            CreateAtMinimumRequest("brindled_gnu_or_blue_wildebeest"),
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        );

        Assert.Equal(RowlandWardQualificationStates.MinimumNotReached, first.QualificationState);
        AssertRational(first.Score!.Canonical.Value, 227, 8);
        Assert.Equal(RowlandWardQualificationStates.MinimumReached, second.QualificationState);
        Assert.NotEqual(first.EvaluatedAtUtc, second.EvaluatedAtUtc);
    }

    private static RowlandWardEvaluationRequest CreateAtMinimumRequest(string classId)
    {
        var request = CreateCompleteRequest(classId);
        var recordClass = Catalogue.Value.GetRecordClass(classId)!;
        var profile = Catalogue.Value.GetMeasurementRequirements(classId)!.Profile;
        var scoreFields = profile.ScoreExpression!.Fields;
        var minimum = recordClass.Minimum.Quantity!.Value;

        for (var index = 0; index < scoreFields.Count; index++)
        {
            var field = profile.Fields.Single(item => item.Id == scoreFields[index]);
            var value = index == 0 ? minimum : new RowlandWardRational { Numerator = 0, Denominator = 1 };
            request.Measurements[field.Id] = RationalForField(field, value.Numerator, value.Denominator);
        }

        return request;
    }

    private static RowlandWardEvaluationRequest CreateCompleteRequest(string classId)
    {
        var profile = Catalogue.Value.GetMeasurementRequirements(classId)!.Profile;
        var measurements = new Dictionary<string, RowlandWardMeasurementInput>(StringComparer.Ordinal);
        foreach (var field in profile.Fields.Where(item => item.Role.StartsWith("required_", StringComparison.Ordinal)))
        {
            measurements[field.Id] = field.Kind switch
            {
                "rational" => RationalForField(field, 0, 1),
                "integer" => new RowlandWardMeasurementInput { IntegerValue = 0 },
                "boolean" => new RowlandWardMeasurementInput { BooleanValue = true },
                _ => throw new InvalidOperationException($"Unknown field kind: {field.Kind}")
            };
        }

        return new RowlandWardEvaluationRequest
        {
            ClassId = classId,
            SourceVersionId = Catalogue.Value.SourceVersion.Id,
            Measurements = measurements
        };
    }

    private static RowlandWardMeasurementInput RationalForField(
        RowlandWardMeasurementField field,
        long canonicalNumerator,
        long canonicalDenominator)
    {
        if (field.InputUnitCode == "kilogram" && field.UnitCode == "pound")
        {
            return PoundsAsKilograms(canonicalNumerator, canonicalDenominator);
        }

        return Rational(
            canonicalNumerator,
            canonicalDenominator,
            field.InputUnitCode
        );
    }

    private static RowlandWardMeasurementInput PoundsAsKilograms(
        long poundNumerator,
        long poundDenominator) => Rational(
            checked(poundNumerator * 45_359_237),
            checked(poundDenominator * 100_000_000),
            "kilogram"
        );

    private static RowlandWardMeasurementInput Rational(
        long numerator,
        long denominator,
        string unitCode) => new()
        {
            UnitCode = unitCode,
            Value = new RowlandWardRational
            {
                Numerator = numerator,
                Denominator = denominator
            }
        };

    private static void AssertRational(
        RowlandWardRational actual,
        long numerator,
        long denominator)
    {
        Assert.Equal(numerator, actual.Numerator);
        Assert.Equal(denominator, actual.Denominator);
    }

    private static IRowlandWardCatalogueService CreateCatalogue()
    {
        var referenceDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Reference");
        return RowlandWardCatalogueService.CreateForFiles(
            Path.Combine(referenceDirectory, "rowland_ward_catalog.json"),
            Path.Combine(referenceDirectory, "rowland_ward_measurement_methods.json")
        );
    }

    private sealed class MissingReferenceCatalogue : IRowlandWardCatalogueService
    {
        private readonly RowlandWardMeasurementProfile _profile = new()
        {
            Id = "synthetic_profile",
            Fields =
            [
                new RowlandWardMeasurementField
                {
                    Id = "score",
                    LabelAf = "Telling",
                    Kind = "rational",
                    UnitCode = "inch",
                    Increment = new RowlandWardRational { Numerator = 1, Denominator = 8 },
                    Role = "required_scoring",
                    Visibility = "owner_only"
                }
            ],
            ScoreExpression = new RowlandWardExpression
            {
                Operator = "identity",
                Fields = ["score"]
            },
            ScoreUnitCode = "inch",
            MinimumComparisonExpression = new RowlandWardExpression
            {
                Operator = "identity",
                Fields = ["score"]
            },
            MinimumComparisonOperator = "greater_than_or_equal"
        };

        private readonly RowlandWardRecordClass _recordClass = new()
        {
            Id = "synthetic_no_minimum",
            DisplayNameAf = "Toetsklas",
            MethodCode = "test",
            MeasurementProfileId = "synthetic_profile",
            SourceVersionId = "test_version",
            NoMinimum = true,
            Minimum = new RowlandWardPublishedValue { State = "not_published" },
            NoRecord = true,
            WorldRecord = new RowlandWardPublishedValue { State = "not_published" }
        };

        public RowlandWardSourceVersion SourceVersion { get; } = new()
        {
            Id = "test_version",
            EditionReleaseIdentity = "Toetsweergawe"
        };

        public IReadOnlyList<RowlandWardRecordClass> RecordClasses => [_recordClass];

        public IReadOnlyList<RowlandWardCategorySummary> GetCategories() => [];

        public IReadOnlyList<RowlandWardRecordClassSummary> GetRecordClasses(
            string? categoryId = null) => [RowlandWardRecordClassSummary.From(_recordClass)];

        public RowlandWardRecordClass? GetRecordClass(string classId) =>
            classId == _recordClass.Id ? _recordClass : null;

        public RowlandWardMeasurementRequirementsResponse? GetMeasurementRequirements(
            string classId) => classId == _recordClass.Id
                ? new RowlandWardMeasurementRequirementsResponse(
                    _recordClass.Id,
                    _recordClass.DisplayNameAf,
                    _recordClass.MethodCode,
                    _profile
                )
                : null;
    }
}
