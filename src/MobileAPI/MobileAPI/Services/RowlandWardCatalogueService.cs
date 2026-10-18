using System.Text.Json;
using Microsoft.Extensions.Options;
using MobileApi.Models.RowlandWard;
using MobileApi.Options;

namespace MobileApi.Services;

public sealed class RowlandWardCatalogueService : IRowlandWardCatalogueService
{
    private const string ApprovedStatus = "APPROVED";
    private const string BlueNormalId = "brindled_gnu_or_blue_wildebeest";
    private static readonly HashSet<string> RequiredExclusions = new(StringComparer.Ordinal)
    {
        "addax",
        "rusa_deer_introduced",
        "barbary_red_deer",
        "brindled_gnu_or_bw_non_typical"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyDictionary<string, RowlandWardRecordClass> _recordClassesById;
    private readonly IReadOnlyDictionary<string, RowlandWardMeasurementProfile> _profilesById;
    private readonly IReadOnlyList<RowlandWardCategorySummary> _categorySummaries;

    public RowlandWardCatalogueService(
        IOptions<RowlandWardOptions> options,
        IWebHostEnvironment environment)
        : this(
            ResolvePath(environment.ContentRootPath, options.Value.CataloguePath),
            ResolvePath(environment.ContentRootPath, options.Value.MeasurementMethodsPath))
    {
    }

    private RowlandWardCatalogueService(string cataloguePath, string measurementMethodsPath)
    {
        var catalogue = Deserialize<RowlandWardCatalogueDocument>(cataloguePath);
        var methods = Deserialize<RowlandWardMethodsDocument>(measurementMethodsPath);
        Validate(catalogue, methods);

        SourceVersion = catalogue.SourceVersion;
        RecordClasses = catalogue.RecordClasses.AsReadOnly();
        _recordClassesById = catalogue.RecordClasses.ToDictionary(item => item.Id, StringComparer.Ordinal);
        _profilesById = methods.Methods
            .SelectMany(method => method.MeasurementProfiles)
            .ToDictionary(profile => profile.Id, StringComparer.Ordinal);
        _categorySummaries = catalogue.Categories
            .Select(category => new RowlandWardCategorySummary(
                category.Id,
                category.DisplayNameAf,
                catalogue.RecordClasses.Count(recordClass => recordClass.CategoryId == category.Id)))
            .OrderBy(category => category.DisplayNameAf, StringComparer.OrdinalIgnoreCase)
            .ThenBy(category => category.Id, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }

    public RowlandWardSourceVersion SourceVersion { get; }
    public IReadOnlyList<RowlandWardRecordClass> RecordClasses { get; }

    public static RowlandWardCatalogueService CreateForFiles(
        string cataloguePath,
        string measurementMethodsPath) => new(cataloguePath, measurementMethodsPath);

    public IReadOnlyList<RowlandWardCategorySummary> GetCategories() => _categorySummaries;

    public IReadOnlyList<RowlandWardRecordClassSummary> GetRecordClasses(string? categoryId = null)
    {
        IEnumerable<RowlandWardRecordClass> records = RecordClasses;
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            records = records.Where(record => string.Equals(
                record.CategoryId,
                categoryId.Trim(),
                StringComparison.Ordinal));
        }

        return records
            .OrderBy(record => record.DisplayNameAf, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.Id, StringComparer.Ordinal)
            .Select(RowlandWardRecordClassSummary.From)
            .ToList()
            .AsReadOnly();
    }

    public RowlandWardRecordClass? GetRecordClass(string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
        {
            return null;
        }

        return _recordClassesById.GetValueOrDefault(classId.Trim());
    }

    public RowlandWardMeasurementRequirementsResponse? GetMeasurementRequirements(string classId)
    {
        var recordClass = GetRecordClass(classId);
        if (recordClass is null || !_profilesById.TryGetValue(recordClass.MeasurementProfileId, out var profile))
        {
            return null;
        }

        return new RowlandWardMeasurementRequirementsResponse(
            recordClass.Id,
            recordClass.DisplayNameAf,
            recordClass.MethodCode,
            profile);
    }

    private static string ResolvePath(string contentRootPath, string configuredPath)
    {
        if (Path.IsPathFullyQualified(configuredPath))
        {
            return configuredPath;
        }

        return Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
    }

    private static T Deserialize<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Rowland Ward reference data is missing: {path}");
        }

        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Rowland Ward reference data is invalid: {path}");
    }

    private static void Validate(
        RowlandWardCatalogueDocument catalogue,
        RowlandWardMethodsDocument methods)
    {
        Require(catalogue.SchemaVersion == 1, "Unsupported Rowland Ward catalogue schema.");
        Require(catalogue.Status == ApprovedStatus, "The Rowland Ward catalogue is not approved.");
        Require(catalogue.SourceVersion.Active, "The Rowland Ward source version is not active.");
        Require(catalogue.SourceVersion.Id == catalogue.ActiveSourceVersionId, "The active source version is inconsistent.");
        Require(catalogue.RecordClassCount == 59, "The approved catalogue must contain 59 classes.");
        Require(catalogue.RecordClasses.Count == catalogue.RecordClassCount, "The catalogue class count is inconsistent.");
        Require(methods.Status == ApprovedStatus, "The Rowland Ward method catalogue is not approved.");
        Require(methods.SourceVersionId == catalogue.SourceVersion.Id, "The method source version is inconsistent.");

        Require(
            catalogue.RecordClasses.Select(record => record.Id).Distinct(StringComparer.Ordinal).Count()
                == catalogue.RecordClasses.Count,
            "Rowland Ward class identifiers must be unique.");
        Require(
            catalogue.RecordClasses.Select(record => record.DisplayNameAf).Distinct(StringComparer.Ordinal).Count()
                == catalogue.RecordClasses.Count,
            "Rowland Ward Afrikaans display names must be unique.");
        Require(
            !catalogue.RecordClasses.Any(record => RequiredExclusions.Contains(record.Id)),
            "An excluded Rowland Ward class is present.");

        var categoryIds = catalogue.Categories.Select(category => category.Id).ToHashSet(StringComparer.Ordinal);
        var profiles = methods.Methods
            .SelectMany(method => method.MeasurementProfiles)
            .ToDictionary(profile => profile.Id, StringComparer.Ordinal);

        foreach (var record in catalogue.RecordClasses)
        {
            Require(!string.IsNullOrWhiteSpace(record.DisplayNameAf), $"{record.Id}: Afrikaans display name is missing.");
            Require(categoryIds.Contains(record.CategoryId), $"{record.Id}: category is missing.");
            Require(record.SourceVersionId == catalogue.SourceVersion.Id, $"{record.Id}: source version is inconsistent.");
            Require(profiles.TryGetValue(record.MeasurementProfileId, out var profile), $"{record.Id}: method profile is missing.");
            ValidatePublishedValue(record.Id, "minimum", record.Minimum, record.NoMinimum);
            ValidatePublishedValue(record.Id, "world record", record.WorldRecord, record.NoRecord);
            Require(record.Comparison.ScoreExpression is not null, $"{record.Id}: score expression is missing.");
            Require(record.Comparison.MinimumComparisonExpression is not null, $"{record.Id}: minimum comparison is missing.");
            Require(profile!.Fields.All(field => field.Visibility == "owner_only"), $"{record.Id}: component visibility is unsafe.");

            var fieldIds = profile.Fields.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
            Require(
                record.Comparison.ScoreExpression!.Fields.All(fieldIds.Contains),
                $"{record.Id}: score expression references an unavailable field.");
            Require(
                record.Comparison.MinimumComparisonExpression!.Fields.All(fieldIds.Contains),
                $"{record.Id}: minimum comparison references an unavailable field.");
        }

        var blue = catalogue.RecordClasses.SingleOrDefault(record => record.Id == BlueNormalId);
        Require(blue is not null, "The retained Blue Wildebeest class is missing.");
        Require(blue!.DisplayNameAf == "Blouwildebees", "The Blue Wildebeest display name is incorrect.");
        Require(
            blue.Aliases.All(alias => !alias.Contains("tipies", StringComparison.OrdinalIgnoreCase)),
            "Typicality aliases must not enter the application catalogue.");
        Require(
            blue.Comparison.ScoreExpression?.Operator == "identity"
                && blue.Comparison.ScoreExpression.Fields.SequenceEqual(["greatest_spread"]),
            "The Blue Wildebeest score must be greatest spread G.");
        var blueProfile = profiles[blue.MeasurementProfileId];
        Require(
            blueProfile.Fields.All(field => field.Role != "required_classification"),
            "Method 13-a classification fields must not be executable requirements.");
    }

    private static void ValidatePublishedValue(
        string classId,
        string label,
        RowlandWardPublishedValue value,
        bool explicitMissingState)
    {
        if (value.State == "published")
        {
            Require(!explicitMissingState, $"{classId}: {label} state is inconsistent.");
            Require(value.Quantity is not null, $"{classId}: published {label} is missing.");
            ValidateRational(classId, label, value.Quantity!.Value);
            return;
        }

        Require(explicitMissingState, $"{classId}: missing {label} state is not explicit.");
        Require(value.Quantity is null, $"{classId}: absent {label} has a value.");
    }

    private static void ValidateRational(string classId, string label, RowlandWardRational value)
    {
        Require(value.Numerator > 0, $"{classId}: {label} numerator must be positive.");
        Require(value.Denominator > 0, $"{classId}: {label} denominator must be positive.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
