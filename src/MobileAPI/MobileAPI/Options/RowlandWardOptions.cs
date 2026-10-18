namespace MobileApi.Options;

public sealed class RowlandWardOptions
{
    public const string SectionName = "RowlandWard";

    public string CataloguePath { get; set; } = "Data/Reference/rowland_ward_catalog.json";
    public string MeasurementMethodsPath { get; set; } = "Data/Reference/rowland_ward_measurement_methods.json";
}
