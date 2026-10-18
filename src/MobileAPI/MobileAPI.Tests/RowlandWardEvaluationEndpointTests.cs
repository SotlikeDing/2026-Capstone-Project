using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Controllers;
using MobileApi.Database;
using MobileApi.Models.RowlandWard;
using MobileApi.Services;

namespace MobileAPI.Tests;

public sealed class RowlandWardEvaluationEndpointTests
{
    [Fact]
    public async Task EvaluationRequiresTheAuthenticatedUsersOptInPreference()
    {
        var controller = CreateController(enabled: false);

        var result = await controller.Evaluate(CreateBlueRequest());

        var response = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Skakel eers", response.Value!.ToString());
        Assert.Contains("jou profiel", response.Value!.ToString());
    }

    [Fact]
    public async Task EnabledUserCanPreviewAnExactEvaluation()
    {
        var controller = CreateController(enabled: true);

        var result = await controller.Evaluate(CreateBlueRequest());

        var response = Assert.IsType<OkObjectResult>(result);
        var evaluation = Assert.IsType<RowlandWardEvaluationSnapshot>(response.Value);
        Assert.Equal("brindled_gnu_or_blue_wildebeest", evaluation.ClassId);
        Assert.Equal("Blouwildebees", evaluation.DisplayNameAf);
        Assert.Equal(RowlandWardQualificationStates.MinimumReached, evaluation.QualificationState);
    }

    private static RowlandWardController CreateController(bool enabled)
    {
        var referenceDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Reference");
        var catalogue = RowlandWardCatalogueService.CreateForFiles(
            Path.Combine(referenceDirectory, "rowland_ward_catalog.json"),
            Path.Combine(referenceDirectory, "rowland_ward_measurement_methods.json")
        );
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "42")],
            "Toets"
        );

        return new RowlandWardController(
            catalogue,
            new UnusedSpeciesResolver(),
            new RowlandWardEvaluationService(catalogue),
            new FixedPreferenceRepository(enabled)
        )
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private static RowlandWardEvaluationRequest CreateBlueRequest() => new()
    {
        ClassId = "brindled_gnu_or_blue_wildebeest",
        Measurements = new Dictionary<string, RowlandWardMeasurementInput>
        {
            ["base_circumference_left"] = Rational(1, 1),
            ["base_circumference_right"] = Rational(1, 1),
            ["greatest_spread"] = Rational(57, 2)
        }
    };

    private static RowlandWardMeasurementInput Rational(long numerator, long denominator) =>
        new()
        {
            UnitCode = "inch",
            Value = new RowlandWardRational
            {
                Numerator = numerator,
                Denominator = denominator
            }
        };

    private sealed class FixedPreferenceRepository(bool enabled)
        : IUserProfilePreferencesRepository
    {
        public Task<bool?> GetRowlandWardEnabledAsync(int userId) =>
            Task.FromResult<bool?>(enabled);

        public Task<bool> UpdateRowlandWardEnabledAsync(int userId, bool value) =>
            Task.FromResult(true);
    }

    private sealed class UnusedSpeciesResolver : IRowlandWardSpeciesResolver
    {
        public IReadOnlyList<RowlandWardSpeciesSuggestion> Search(
            string query,
            string? categoryId,
            int limit) => [];

        public RowlandWardSpeciesMatchResult Match(string input, int limit) =>
            throw new NotSupportedException();
    }
}
