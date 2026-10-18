using MobileApi.Models.RowlandWard;

namespace MobileApi.Services;

public interface IRowlandWardCatalogueService
{
    RowlandWardSourceVersion SourceVersion { get; }
    IReadOnlyList<RowlandWardRecordClass> RecordClasses { get; }
    IReadOnlyList<RowlandWardCategorySummary> GetCategories();
    IReadOnlyList<RowlandWardRecordClassSummary> GetRecordClasses(string? categoryId = null);
    RowlandWardRecordClass? GetRecordClass(string classId);
    RowlandWardMeasurementRequirementsResponse? GetMeasurementRequirements(string classId);
}
