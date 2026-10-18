using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MobileApi.Controllers;
using MobileApi.Database;
using MobileApi.Models.RowlandWard;

namespace MobileAPI.Tests;

public class RowlandWardPreferenceTests
{
    [Fact]
    public async Task ExistingUserWithoutEnabledPreferenceReadsAsOff()
    {
        var repository = new FakeUserProfilePreferencesRepository
        {
            StoredValue = false
        };
        var controller = CreateController(repository, userId: 17);

        var result = await controller.GetRowlandWardPreference();

        var response = Assert.IsType<OkObjectResult>(result);
        var preference = Assert.IsType<RowlandWardPreferenceResponse>(response.Value);
        Assert.False(preference.RowlandWardEnabled);
        Assert.Equal(17, repository.LastReadUserId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuthenticatedUserCanUpdateOwnPreference(bool enabled)
    {
        var repository = new FakeUserProfilePreferencesRepository();
        var controller = CreateController(repository, userId: 23);

        var result = await controller.UpdateRowlandWardPreference(
            new UpdateRowlandWardPreferenceRequest
            {
                RowlandWardEnabled = enabled
            }
        );

        var response = Assert.IsType<OkObjectResult>(result);
        var preference = Assert.IsType<RowlandWardPreferenceResponse>(response.Value);
        Assert.Equal(enabled, preference.RowlandWardEnabled);
        Assert.Equal(23, repository.LastUpdatedUserId);
        Assert.Equal(enabled, repository.StoredValue);
    }

    [Fact]
    public async Task MissingPreferenceValueReturnsAfrikaansValidationError()
    {
        var controller = CreateController(
            new FakeUserProfilePreferencesRepository(),
            userId: 23
        );

        var result = await controller.UpdateRowlandWardPreference(
            new UpdateRowlandWardPreferenceRequest()
        );

        var response = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("aan of af", response.Value!.ToString());
    }

    [Fact]
    public async Task SaveFailureReturnsAfrikaansError()
    {
        var repository = new FakeUserProfilePreferencesRepository
        {
            ThrowOnUpdate = true
        };
        var controller = CreateController(repository, userId: 23);

        var result = await controller.UpdateRowlandWardPreference(
            new UpdateRowlandWardPreferenceRequest
            {
                RowlandWardEnabled = true
            }
        );

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, response.StatusCode);
        Assert.Contains("kon nie gestoor word nie", response.Value!.ToString());
    }

    [Fact]
    public async Task MissingAuthenticationClaimCannotReadPreference()
    {
        var repository = new FakeUserProfilePreferencesRepository();
        var controller = new ProfileController(repository)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.GetRowlandWardPreference();

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Null(repository.LastReadUserId);
    }

    private static ProfileController CreateController(
        IUserProfilePreferencesRepository repository,
        int userId)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) },
            "Toets"
        );

        return new ProfileController(repository)
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

    private sealed class FakeUserProfilePreferencesRepository
        : IUserProfilePreferencesRepository
    {
        public bool StoredValue { get; set; }
        public bool ThrowOnUpdate { get; set; }
        public int? LastReadUserId { get; private set; }
        public int? LastUpdatedUserId { get; private set; }

        public Task<bool?> GetRowlandWardEnabledAsync(int userId)
        {
            LastReadUserId = userId;
            return Task.FromResult<bool?>(StoredValue);
        }

        public Task<bool> UpdateRowlandWardEnabledAsync(int userId, bool enabled)
        {
            if (ThrowOnUpdate)
            {
                throw new InvalidOperationException("Simulated database failure.");
            }

            LastUpdatedUserId = userId;
            StoredValue = enabled;
            return Task.FromResult(true);
        }
    }
}
