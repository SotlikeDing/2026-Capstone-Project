using System.Text.Json;
using MobileApi.Models;
using MobileApi.Validation;

namespace MobileAPI.Tests;

public class TrophyShotDistanceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(185)]
    public void Validation_AllowsOptionalNonNegativeWholeMetres(int? shotDistanceMeters)
    {
        var request = CreateValidRequest(shotDistanceMeters);

        var error = TrophySaveRequestValidator.GetError(request);

        Assert.Null(error);
    }

    [Fact]
    public void Validation_RejectsNegativeShotDistanceInAfrikaans()
    {
        var request = CreateValidRequest(-1);

        var error = TrophySaveRequestValidator.GetError(request);

        Assert.Equal("Skootafstand kan nie negatief wees nie.", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(212)]
    public void SaveRequest_UsesNullableCamelCaseJsonField(int? shotDistanceMeters)
    {
        var request = CreateValidRequest(shotDistanceMeters);
        var json = JsonSerializer.Serialize(
            request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        );
        using var document = JsonDocument.Parse(json);

        var property = document.RootElement.GetProperty("shotDistanceMeters");

        if (shotDistanceMeters is null)
        {
            Assert.Equal(JsonValueKind.Null, property.ValueKind);
        }
        else
        {
            Assert.Equal(shotDistanceMeters, property.GetInt32());
        }
    }

    [Fact]
    public void PublicSummary_CarriesShotDistanceWithoutChangingRankingFields()
    {
        var summary = new LeaderboardTrophySummary
        {
            Id = 7,
            Species = "Koedoe",
            ShotDistanceMeters = 212,
            SelectedSortValue = 104.50m
        };

        Assert.Equal(212, summary.ShotDistanceMeters);
        Assert.Equal(104.50m, summary.SelectedSortValue);
    }

    private static TrophySaveRequest CreateValidRequest(int? shotDistanceMeters)
    {
        return new TrophySaveRequest
        {
            Species = "Koedoe",
            HarvestDate = new DateTime(2026, 6, 18),
            ShotDistanceMeters = shotDistanceMeters
        };
    }
}
