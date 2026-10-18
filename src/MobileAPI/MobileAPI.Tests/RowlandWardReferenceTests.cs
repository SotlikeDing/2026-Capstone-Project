using Microsoft.Extensions.Caching.Memory;
using MobileApi.Models.RowlandWard;
using MobileApi.Services;

namespace MobileAPI.Tests;

public sealed class RowlandWardReferenceTests
{
    private static readonly Lazy<IRowlandWardCatalogueService> Catalogue = new(CreateCatalogue);

    [Fact]
    public void RuntimeCatalogue_ContainsOnlyTheApprovedUniqueClasses()
    {
        var classes = Catalogue.Value.RecordClasses;

        Assert.Equal(59, classes.Count);
        Assert.Equal(59, classes.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(59, classes.Select(item => item.DisplayNameAf).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(classes, item => item.Id == "addax");
        Assert.DoesNotContain(classes, item => item.Id == "rusa_deer_introduced");
        Assert.DoesNotContain(classes, item => item.Id == "barbary_red_deer");
        Assert.DoesNotContain(classes, item => item.Id == "brindled_gnu_or_bw_non_typical");
    }

    [Theory]
    [InlineData("rooibok")]
    [InlineData("Rooibok")]
    [InlineData("ROOIBOK")]
    [InlineData("  rooibok  ")]
    public void Match_NormalizesCaseAndOuterWhitespace(string input)
    {
        var result = CreateResolver().Match(input, 5);

        Assert.Equal(RowlandWardSpeciesResolver.Exact, result.Outcome);
        Assert.Equal("southern_impala", result.Match?.Id);
        Assert.Equal("Rooibok", result.Match?.DisplayNameAf);
        Assert.False(result.RequiresSelection);
    }

    [Fact]
    public void Match_NormalizesRepeatedInternalWhitespace()
    {
        var result = CreateResolver().Match("  BLOU    DUIKER ", 5);

        Assert.Equal(RowlandWardSpeciesResolver.Exact, result.Outcome);
        Assert.Equal("blue_duiker", result.Match?.Id);
        Assert.Equal("Blou Duiker", result.Match?.DisplayNameAf);
    }

    [Fact]
    public void Match_ResolvesApprovedAliasWithoutChangingDisplayName()
    {
        var result = CreateResolver().Match("Aepyceros melampus melampus", 5);

        Assert.Equal(RowlandWardSpeciesResolver.Alias, result.Outcome);
        Assert.Equal("southern_impala", result.Match?.Id);
        Assert.Equal("Rooibok", result.Match?.DisplayNameAf);
    }

    [Fact]
    public void Search_PrefixIncludesRooibokAndOtherRelevantSpecies()
    {
        var results = CreateResolver().Search("roo", null, 20);

        Assert.Contains(results, item => item.ClassId == "southern_impala" && item.DisplayNameAf == "Rooibok");
        Assert.Contains(results, item => item.ClassId == "natal_or_red_duiker" && item.DisplayNameAf == "Rooiduiker");
    }

    [Fact]
    public void Match_ClearTypoReturnsHighConfidenceRooibokSuggestion()
    {
        var result = CreateResolver().Match("Roibok", 5);

        Assert.Equal(RowlandWardSpeciesResolver.HighConfidenceFuzzy, result.Outcome);
        Assert.Equal("southern_impala", result.Match?.Id);
        Assert.Equal("Rooibok", result.Match?.DisplayNameAf);
        Assert.True(result.Suggestions.Single().Confidence >= 84);
    }

    [Fact]
    public void Match_TransposedLettersUsesDeterministicDamerauLevenshteinDistance()
    {
        var result = CreateResolver().Match("Rooibko", 5);

        Assert.Equal(1, RowlandWardSpeciesResolver.DamerauLevenshteinDistance("rooibko", "rooibok"));
        Assert.Equal(RowlandWardSpeciesResolver.HighConfidenceFuzzy, result.Outcome);
        Assert.Equal("southern_impala", result.Match?.Id);
    }

    [Fact]
    public void Match_AmbiguousTypoReturnsMultipleSuggestionsAndRequiresSelection()
    {
        var result = CreateResolver().Match("Nylkrokodl", 5);

        Assert.Equal(RowlandWardSpeciesResolver.Ambiguous, result.Outcome);
        Assert.Null(result.Match);
        Assert.True(result.RequiresSelection);
        Assert.Equal("Kies asseblief die korrekte wildspesie.", result.MessageAf);
        Assert.Contains(result.Suggestions, item => item.ClassId == "nile_crocodile_body");
        Assert.Contains(result.Suggestions, item => item.ClassId == "nile_crocodile_skull");
    }

    [Fact]
    public void Match_NonsenseDoesNotInventAClass()
    {
        var result = CreateResolver().Match("xqzvplm", 5);

        Assert.Equal(RowlandWardSpeciesResolver.NoMatch, result.Outcome);
        Assert.Null(result.Match);
        Assert.Empty(result.Suggestions);
        Assert.Equal("Geen goedgekeurde wildspesie stem met jou invoer ooreen nie.", result.MessageAf);
    }

    [Theory]
    [InlineData("Koedoe", "southern_greater_kudu")]
    [InlineData("Buffel", "cape_buffalo")]
    [InlineData("Vlakvark", "warthog")]
    public void Match_WorksForMultipleSpeciesBeyondRooibok(string input, string expectedClassId)
    {
        var result = CreateResolver().Match(input, 5);

        Assert.Equal(RowlandWardSpeciesResolver.Exact, result.Outcome);
        Assert.Equal(expectedClassId, result.Match?.Id);
    }

    [Theory]
    [InlineData("Addax")]
    [InlineData("Rusa Deer")]
    [InlineData("Barbary Red Deer")]
    public void Search_ExcludedClassesNeverAppear(string query)
    {
        var results = CreateResolver().Search(query, null, 20);

        Assert.Empty(results);
    }

    [Fact]
    public void BlueWildebeest_ExposesOnlyTheRetainedNormalClassWithoutTypicalityFields()
    {
        var blueResults = CreateResolver().Search("Blouwildebees", null, 20);
        var blue = Assert.Single(blueResults);
        var recordClass = Catalogue.Value.GetRecordClass("brindled_gnu_or_blue_wildebeest");
        var requirements = Catalogue.Value.GetMeasurementRequirements("brindled_gnu_or_blue_wildebeest");

        Assert.Equal("brindled_gnu_or_blue_wildebeest", blue.ClassId);
        Assert.Equal("Blouwildebees", blue.DisplayNameAf);
        Assert.NotNull(recordClass);
        Assert.Equal(57L, recordClass!.Minimum.Quantity?.Value.Numerator);
        Assert.Equal(2L, recordClass.Minimum.Quantity?.Value.Denominator);
        Assert.NotNull(requirements);
        Assert.Equal("13-a", requirements!.MethodCode);
        Assert.Equal("greatest_spread", requirements.Profile.ScoreExpression?.Fields.Single());
        Assert.DoesNotContain(requirements.Profile.Fields, field => field.Role == "required_classification");
        Assert.DoesNotContain(requirements.Profile.Fields, field => field.Id == "tip_to_tip");
    }

    [Fact]
    public void ElephantRequirements_UseKgForApplicationInputAndPoundsForCanonicalScore()
    {
        var requirements = Catalogue.Value.GetMeasurementRequirements("savanna_elephant_one_tusk");

        Assert.NotNull(requirements);
        Assert.Equal("pound", requirements!.Profile.ScoreUnitCode);
        var weight = requirements.Profile.Fields.Single(field => field.Id == "tusk_weight_left");
        Assert.Equal("pound", weight.UnitCode);
        Assert.NotNull(weight.ApplicationInput);
        Assert.Equal("kilogram", weight.ApplicationInput!.Value.GetProperty("unitCode").GetString());
        Assert.Equal(
            "kilogramsExact * 100000000 / 45359237",
            weight.ApplicationInput.Value.GetProperty("conversionToCanonicalPounds").GetString());
        Assert.Equal(
            "round_half_up",
            weight.ApplicationInput.Value.GetProperty("scoringRoundingMode").GetString());
    }

    [Fact]
    public void Categories_OnlyCountApprovedRuntimeClasses()
    {
        var categories = Catalogue.Value.GetCategories();

        Assert.NotEmpty(categories);
        Assert.Equal(59, categories.Sum(category => category.RecordClassCount));
        Assert.DoesNotContain(categories, category => category.RecordClassCount == 0);
    }

    private static RowlandWardSpeciesResolver CreateResolver() => new(
        Catalogue.Value,
        new MemoryCache(new MemoryCacheOptions()));

    private static IRowlandWardCatalogueService CreateCatalogue()
    {
        var referenceDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Reference");
        return RowlandWardCatalogueService.CreateForFiles(
            Path.Combine(referenceDirectory, "rowland_ward_catalog.json"),
            Path.Combine(referenceDirectory, "rowland_ward_measurement_methods.json"));
    }
}
