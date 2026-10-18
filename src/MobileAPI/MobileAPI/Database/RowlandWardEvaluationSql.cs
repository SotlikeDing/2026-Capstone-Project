using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using MobileApi.Models.RowlandWard;

namespace MobileApi.Database;

internal static class RowlandWardEvaluationSql
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string OwnerSelectColumns = """
        e.trophyRowlandWardEvaluationId AS rweEvaluationId,
        e.trweSchemaVersion AS rweSchemaVersion,
        e.trweClassId AS rweClassId,
        e.trweDisplayNameAf AS rweDisplayNameAf,
        e.trweMethodCode AS rweMethodCode,
        e.trweMeasurementProfileId AS rweMeasurementProfileId,
        e.trweSourceVersionId AS rweSourceVersionId,
        e.trweEditionReleaseIdentity AS rweEditionReleaseIdentity,
        e.trweSourceMasterSha256 AS rweSourceMasterSha256,
        e.trweEvaluatedAtUtc AS rweEvaluatedAtUtc,
        e.trweScoreCanonicalNumerator AS rweScoreCanonicalNumerator,
        e.trweScoreCanonicalDenominator AS rweScoreCanonicalDenominator,
        e.trweScoreCanonicalUnitCode AS rweScoreCanonicalUnitCode,
        e.trweScoreDisplayNumerator AS rweScoreDisplayNumerator,
        e.trweScoreDisplayDenominator AS rweScoreDisplayDenominator,
        e.trweScoreDisplayUnitCode AS rweScoreDisplayUnitCode,
        e.trweMinimumState AS rweMinimumState,
        e.trweMinimumCanonicalNumerator AS rweMinimumCanonicalNumerator,
        e.trweMinimumCanonicalDenominator AS rweMinimumCanonicalDenominator,
        e.trweMinimumCanonicalUnitCode AS rweMinimumCanonicalUnitCode,
        e.trweMinimumDisplayNumerator AS rweMinimumDisplayNumerator,
        e.trweMinimumDisplayDenominator AS rweMinimumDisplayDenominator,
        e.trweMinimumDisplayUnitCode AS rweMinimumDisplayUnitCode,
        e.trweWorldRecordState AS rweWorldRecordState,
        e.trweWorldRecordCanonicalNumerator AS rweWorldRecordCanonicalNumerator,
        e.trweWorldRecordCanonicalDenominator AS rweWorldRecordCanonicalDenominator,
        e.trweWorldRecordCanonicalUnitCode AS rweWorldRecordCanonicalUnitCode,
        e.trweWorldRecordDisplayNumerator AS rweWorldRecordDisplayNumerator,
        e.trweWorldRecordDisplayDenominator AS rweWorldRecordDisplayDenominator,
        e.trweWorldRecordDisplayUnitCode AS rweWorldRecordDisplayUnitCode,
        e.trweQualificationState AS rweQualificationState,
        e.trweMinimumReached AS rweMinimumReached,
        e.trweScoreExpressionJson AS rweScoreExpressionJson,
        e.trweMinimumExpressionJson AS rweMinimumExpressionJson,
        e.trweMinimumComparisonOperator AS rweMinimumComparisonOperator
        """;

    public static async Task<long> InsertEvaluationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int trophyId,
        RowlandWardEvaluationSnapshot evaluation)
    {
        const string sql = """
            INSERT INTO dbo.trophyRowlandWardEvaluations (
                trweTrophyId,
                trweIsCurrent,
                trweSchemaVersion,
                trweClassId,
                trweDisplayNameAf,
                trweMethodCode,
                trweMeasurementProfileId,
                trweSourceVersionId,
                trweEditionReleaseIdentity,
                trweSourceMasterSha256,
                trweEvaluatedAtUtc,
                trweScoreCanonicalNumerator,
                trweScoreCanonicalDenominator,
                trweScoreCanonicalUnitCode,
                trweScoreDisplayNumerator,
                trweScoreDisplayDenominator,
                trweScoreDisplayUnitCode,
                trweMinimumState,
                trweMinimumCanonicalNumerator,
                trweMinimumCanonicalDenominator,
                trweMinimumCanonicalUnitCode,
                trweMinimumDisplayNumerator,
                trweMinimumDisplayDenominator,
                trweMinimumDisplayUnitCode,
                trweWorldRecordState,
                trweWorldRecordCanonicalNumerator,
                trweWorldRecordCanonicalDenominator,
                trweWorldRecordCanonicalUnitCode,
                trweWorldRecordDisplayNumerator,
                trweWorldRecordDisplayDenominator,
                trweWorldRecordDisplayUnitCode,
                trweQualificationState,
                trweMinimumReached,
                trweScoreExpressionJson,
                trweMinimumExpressionJson,
                trweMinimumComparisonOperator
            )
            OUTPUT INSERTED.trophyRowlandWardEvaluationId
            VALUES (
                @TrophyId, 1, @SchemaVersion, @ClassId, @DisplayNameAf, @MethodCode,
                @MeasurementProfileId, @SourceVersionId, @EditionReleaseIdentity, @SourceMasterSha256,
                @EvaluatedAtUtc,
                @ScoreCanonicalNumerator, @ScoreCanonicalDenominator, @ScoreCanonicalUnitCode,
                @ScoreDisplayNumerator, @ScoreDisplayDenominator, @ScoreDisplayUnitCode,
                @MinimumState,
                @MinimumCanonicalNumerator, @MinimumCanonicalDenominator, @MinimumCanonicalUnitCode,
                @MinimumDisplayNumerator, @MinimumDisplayDenominator, @MinimumDisplayUnitCode,
                @WorldRecordState,
                @WorldRecordCanonicalNumerator, @WorldRecordCanonicalDenominator, @WorldRecordCanonicalUnitCode,
                @WorldRecordDisplayNumerator, @WorldRecordDisplayDenominator, @WorldRecordDisplayUnitCode,
                @QualificationState, @MinimumReached, @ScoreExpressionJson,
                @MinimumExpressionJson, @MinimumComparisonOperator
            );
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@TrophyId", SqlDbType.Int).Value = trophyId;
        command.Parameters.Add("@SchemaVersion", SqlDbType.Int).Value = evaluation.SchemaVersion;
        AddText(command, "@ClassId", 160, evaluation.ClassId);
        AddText(command, "@DisplayNameAf", 160, evaluation.DisplayNameAf);
        AddText(command, "@MethodCode", 20, evaluation.MethodCode);
        AddText(command, "@MeasurementProfileId", 80, evaluation.MeasurementProfileId);
        AddText(command, "@SourceVersionId", 120, evaluation.SourceVersionId);
        AddText(command, "@EditionReleaseIdentity", 160, evaluation.EditionReleaseIdentity);
        AddNullableText(command, "@SourceMasterSha256", 64, evaluation.SourceMasterSha256);
        command.Parameters.Add("@EvaluatedAtUtc", SqlDbType.DateTime2).Value = evaluation.EvaluatedAtUtc;
        AddEvaluatedQuantity(command, "Score", evaluation.Score);
        AddReferenceValue(command, "Minimum", evaluation.MinimumAtEvaluation);
        AddReferenceValue(command, "WorldRecord", evaluation.WorldRecordAtEvaluation);
        AddText(command, "@QualificationState", 60, evaluation.QualificationState);
        command.Parameters.Add("@MinimumReached", SqlDbType.Bit).Value =
            evaluation.MinimumReached ?? (object)DBNull.Value;
        AddText(
            command,
            "@ScoreExpressionJson",
            -1,
            JsonSerializer.Serialize(evaluation.ScoreExpression, JsonOptions)
        );
        AddText(
            command,
            "@MinimumExpressionJson",
            -1,
            JsonSerializer.Serialize(evaluation.MinimumComparisonExpression, JsonOptions)
        );
        AddText(
            command,
            "@MinimumComparisonOperator",
            40,
            evaluation.MinimumComparisonOperator
        );

        var evaluationId = (long)(await command.ExecuteScalarAsync())!;
        foreach (var measurement in evaluation.Measurements)
        {
            await InsertMeasurementAsync(
                connection,
                transaction,
                evaluationId,
                measurement
            );
        }

        return evaluationId;
    }

    public static RowlandWardEvaluationSnapshot? ReadOwnerEvaluation(SqlDataReader reader)
    {
        if (reader.IsDBNull(reader.GetOrdinal("rweEvaluationId")))
        {
            return null;
        }

        return new RowlandWardEvaluationSnapshot
        {
            EvaluationId = reader.GetInt64(reader.GetOrdinal("rweEvaluationId")),
            SchemaVersion = reader.GetInt32(reader.GetOrdinal("rweSchemaVersion")),
            ClassId = reader.GetString(reader.GetOrdinal("rweClassId")),
            DisplayNameAf = reader.GetString(reader.GetOrdinal("rweDisplayNameAf")),
            MethodCode = reader.GetString(reader.GetOrdinal("rweMethodCode")),
            MeasurementProfileId = reader.GetString(reader.GetOrdinal("rweMeasurementProfileId")),
            SourceVersionId = reader.GetString(reader.GetOrdinal("rweSourceVersionId")),
            EditionReleaseIdentity = reader.GetString(reader.GetOrdinal("rweEditionReleaseIdentity")),
            SourceMasterSha256 = reader.IsDBNull(reader.GetOrdinal("rweSourceMasterSha256"))
                ? null
                : reader.GetString(reader.GetOrdinal("rweSourceMasterSha256")),
            EvaluatedAtUtc = reader.GetDateTime(reader.GetOrdinal("rweEvaluatedAtUtc")),
            Score = ReadEvaluatedQuantity(reader, "rweScore"),
            MinimumAtEvaluation = ReadReferenceValue(reader, "rweMinimum"),
            WorldRecordAtEvaluation = ReadReferenceValue(reader, "rweWorldRecord"),
            QualificationState = reader.GetString(reader.GetOrdinal("rweQualificationState")),
            MinimumReached = reader.IsDBNull(reader.GetOrdinal("rweMinimumReached"))
                ? null
                : reader.GetBoolean(reader.GetOrdinal("rweMinimumReached")),
            QualificationMessageAf = reader.IsDBNull(reader.GetOrdinal("rweMinimumReached")) ||
                                     !reader.GetBoolean(reader.GetOrdinal("rweMinimumReached"))
                ? null
                : "Rowland Ward",
            ScoreExpression = DeserializeExpression(reader, "rweScoreExpressionJson"),
            MinimumComparisonExpression = DeserializeExpression(reader, "rweMinimumExpressionJson"),
            MinimumComparisonOperator = reader.GetString(
                reader.GetOrdinal("rweMinimumComparisonOperator")
            )
        };
    }

    public static RowlandWardEvaluationSnapshot? ReadPublicEvaluation(SqlDataReader reader)
    {
        if (reader.IsDBNull(reader.GetOrdinal("rweEvaluationId")))
        {
            return null;
        }

        return new RowlandWardEvaluationSnapshot
        {
            EvaluationId = reader.GetInt64(reader.GetOrdinal("rweEvaluationId")),
            ClassId = reader.GetString(reader.GetOrdinal("rweClassId")),
            DisplayNameAf = reader.GetString(reader.GetOrdinal("rweDisplayNameAf")),
            MethodCode = reader.GetString(reader.GetOrdinal("rweMethodCode")),
            SourceVersionId = reader.GetString(reader.GetOrdinal("rweSourceVersionId")),
            QualificationState = reader.GetString(reader.GetOrdinal("rweQualificationState")),
            MinimumReached = reader.IsDBNull(reader.GetOrdinal("rweMinimumReached"))
                ? null
                : reader.GetBoolean(reader.GetOrdinal("rweMinimumReached")),
            Score = ReadPublicScore(reader)
        };
    }

    public static (long EvaluationId, RowlandWardMeasurementSnapshot Measurement)
        ReadMeasurement(SqlDataReader reader)
    {
        var evaluationId = reader.GetInt64(reader.GetOrdinal("trwmEvaluationId"));
        return (
            evaluationId,
            new RowlandWardMeasurementSnapshot
            {
                FieldId = reader.GetString(reader.GetOrdinal("trwmFieldId")),
                LabelAf = reader.GetString(reader.GetOrdinal("trwmLabelAf")),
                Kind = reader.GetString(reader.GetOrdinal("trwmKind")),
                Role = reader.GetString(reader.GetOrdinal("trwmRole")),
                InputQuantity = ReadQuantity(reader, "trwmInput"),
                CanonicalQuantity = ReadQuantity(reader, "trwmCanonical"),
                ScoringQuantity = ReadQuantity(reader, "trwmScoring"),
                IntegerValue = reader.IsDBNull(reader.GetOrdinal("trwmIntegerValue"))
                    ? null
                    : reader.GetInt64(reader.GetOrdinal("trwmIntegerValue")),
                BooleanValue = reader.IsDBNull(reader.GetOrdinal("trwmBooleanValue"))
                    ? null
                    : reader.GetBoolean(reader.GetOrdinal("trwmBooleanValue"))
            }
        );
    }

    private static async Task InsertMeasurementAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        long evaluationId,
        RowlandWardMeasurementSnapshot measurement)
    {
        const string sql = """
            INSERT INTO dbo.trophyRowlandWardMeasurements (
                trwmEvaluationId,
                trwmFieldId,
                trwmLabelAf,
                trwmKind,
                trwmRole,
                trwmInputNumerator,
                trwmInputDenominator,
                trwmInputUnitCode,
                trwmCanonicalNumerator,
                trwmCanonicalDenominator,
                trwmCanonicalUnitCode,
                trwmScoringNumerator,
                trwmScoringDenominator,
                trwmScoringUnitCode,
                trwmIntegerValue,
                trwmBooleanValue
            )
            VALUES (
                @EvaluationId, @FieldId, @LabelAf, @Kind, @Role,
                @InputNumerator, @InputDenominator, @InputUnitCode,
                @CanonicalNumerator, @CanonicalDenominator, @CanonicalUnitCode,
                @ScoringNumerator, @ScoringDenominator, @ScoringUnitCode,
                @IntegerValue, @BooleanValue
            );
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@EvaluationId", SqlDbType.BigInt).Value = evaluationId;
        AddText(command, "@FieldId", 100, measurement.FieldId);
        AddText(command, "@LabelAf", 160, measurement.LabelAf);
        AddText(command, "@Kind", 20, measurement.Kind);
        AddText(command, "@Role", 60, measurement.Role);
        AddQuantity(command, "Input", measurement.InputQuantity);
        AddQuantity(command, "Canonical", measurement.CanonicalQuantity);
        AddQuantity(command, "Scoring", measurement.ScoringQuantity);
        command.Parameters.Add("@IntegerValue", SqlDbType.BigInt).Value =
            measurement.IntegerValue ?? (object)DBNull.Value;
        command.Parameters.Add("@BooleanValue", SqlDbType.Bit).Value =
            measurement.BooleanValue ?? (object)DBNull.Value;
        await command.ExecuteNonQueryAsync();
    }

    private static void AddReferenceValue(
        SqlCommand command,
        string prefix,
        RowlandWardReferenceValueSnapshot value)
    {
        AddText(command, $"@{prefix}State", 40, value.State);
        AddEvaluatedQuantity(command, prefix, value.Quantity);
    }

    private static void AddEvaluatedQuantity(
        SqlCommand command,
        string prefix,
        RowlandWardEvaluatedQuantity? value)
    {
        AddQuantity(command, $"{prefix}Canonical", value?.Canonical);
        AddQuantity(command, $"{prefix}Display", value?.Display);
    }

    private static void AddQuantity(
        SqlCommand command,
        string prefix,
        RowlandWardQuantitySnapshot? quantity)
    {
        command.Parameters.Add($"@{prefix}Numerator", SqlDbType.BigInt).Value =
            quantity?.Value.Numerator ?? (object)DBNull.Value;
        command.Parameters.Add($"@{prefix}Denominator", SqlDbType.BigInt).Value =
            quantity?.Value.Denominator ?? (object)DBNull.Value;
        var parameter = command.Parameters.Add($"@{prefix}UnitCode", SqlDbType.NVarChar, 20);
        parameter.Value = quantity is null ? DBNull.Value : quantity.UnitCode;
    }

    private static void AddText(SqlCommand command, string name, int size, string value)
    {
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value = value;
    }

    private static void AddNullableText(
        SqlCommand command,
        string name,
        int size,
        string? value)
    {
        command.Parameters.Add(name, SqlDbType.NVarChar, size).Value =
            string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static RowlandWardReferenceValueSnapshot ReadReferenceValue(
        SqlDataReader reader,
        string prefix) => new()
        {
            State = reader.GetString(reader.GetOrdinal($"{prefix}State")),
            Quantity = ReadEvaluatedQuantity(reader, prefix)
        };

    private static RowlandWardEvaluatedQuantity? ReadEvaluatedQuantity(
        SqlDataReader reader,
        string prefix)
    {
        var canonical = ReadQuantity(reader, $"{prefix}Canonical");
        var display = ReadQuantity(reader, $"{prefix}Display");
        return canonical is null || display is null
            ? null
            : new RowlandWardEvaluatedQuantity { Canonical = canonical, Display = display };
    }

    private static RowlandWardEvaluatedQuantity? ReadPublicScore(SqlDataReader reader)
    {
        var display = ReadQuantity(reader, "rweScoreDisplay");
        return display is null
            ? null
            : new RowlandWardEvaluatedQuantity { Display = display, Canonical = display };
    }

    private static RowlandWardQuantitySnapshot? ReadQuantity(
        SqlDataReader reader,
        string prefix)
    {
        var numeratorOrdinal = reader.GetOrdinal($"{prefix}Numerator");
        if (reader.IsDBNull(numeratorOrdinal))
        {
            return null;
        }

        var unit = reader.GetString(reader.GetOrdinal($"{prefix}UnitCode"));
        return new RowlandWardQuantitySnapshot
        {
            Value = new RowlandWardRational
            {
                Numerator = reader.GetInt64(numeratorOrdinal),
                Denominator = reader.GetInt64(reader.GetOrdinal($"{prefix}Denominator"))
            },
            UnitCode = unit,
            UnitLabelAf = RowlandWardUnitLabels.GetAfrikaans(unit)
        };
    }

    private static RowlandWardExpression DeserializeExpression(
        SqlDataReader reader,
        string column)
    {
        var json = reader.GetString(reader.GetOrdinal(column));
        return JsonSerializer.Deserialize<RowlandWardExpression>(json, JsonOptions)
            ?? new RowlandWardExpression();
    }
}
