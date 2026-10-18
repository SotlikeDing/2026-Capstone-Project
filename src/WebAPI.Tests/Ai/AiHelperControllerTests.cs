using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WebAPI.Controllers;
using WebAPI.Models;
using WebAPI.Services.Ai;

namespace WebAPI.Tests.Ai;

public class AiHelperControllerTests
{
    private readonly Mock<IPackageAdvisor> _advisor = new();
    private readonly FakeStore _store = new();

    private AiHelperController Controller(int userId, params string[] roles)
    {
        var c = new AiHelperController(_advisor.Object, _store, AiTestSupport.RealModel(), new NullLlmClient(),
            Microsoft.Extensions.Options.Options.Create(AiTestSupport.Options()));
        var claims = new List<Claim>();
        if (userId > 0) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } };
        return c;
    }

    private void AdvisorReturnsOk() =>
        _advisor.Setup(a => a.OptimizeAsync(It.IsAny<AiOptimizeRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AiOptimizeResponse { RoleCode = "PJ" });

    [Fact]
    public async Task UserWithTheRole_GetsAdvice_UsingTheirOwnIdFromTheToken()
    {
        AdvisorReturnsOk();
        var result = await Controller(42, "PJ", "HUNTER").Optimize(new AiOptimizeRequest { RoleCode = "PJ" }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        _advisor.Verify(a => a.OptimizeAsync(It.IsAny<AiOptimizeRequest>(), 42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UserWithoutTheRole_IsForbidden_AndTheAdvisorIsNeverCalled()
    {
        AdvisorReturnsOk();
        var result = await Controller(42, "PH").Optimize(new AiOptimizeRequest { RoleCode = "PJ" }, CancellationToken.None);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        _advisor.Verify(a => a.OptimizeAsync(It.IsAny<AiOptimizeRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BoerAndFarmer_AreTheSameRole()
    {
        AdvisorReturnsOk();
        var result = await Controller(7, "FARMER").Optimize(new AiOptimizeRequest { RoleCode = "Boer" }, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Admin_MayUseAnyAiHelperRole()
    {
        AdvisorReturnsOk();
        Assert.IsType<OkObjectResult>(await Controller(1, "ADMIN").Optimize(new AiOptimizeRequest { RoleCode = "VG" }, CancellationToken.None));
    }

    [Fact]
    public async Task HunterRole_IsNotAnAiHelperRole()
    {
        var result = await Controller(3, "HUNTER").Optimize(new AiOptimizeRequest { RoleCode = "HUNTER" }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task TokenWithoutAUserId_IsUnauthorized()
    {
        AdvisorReturnsOk();
        Assert.IsType<UnauthorizedResult>(await Controller(0, "PJ").Optimize(new AiOptimizeRequest { RoleCode = "PJ" }, CancellationToken.None));
    }

    [Fact]
    public async Task ValidationErrors_Become400_AndModelOutages_Become503()
    {
        _advisor.Setup(a => a.OptimizeAsync(It.IsAny<AiOptimizeRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AiValidationException("Maand moet tussen 1 en 12 wees."));
        Assert.IsType<BadRequestObjectResult>(await Controller(1, "PJ").Optimize(new AiOptimizeRequest { RoleCode = "PJ" }, CancellationToken.None));

        _advisor.Setup(a => a.OptimizeAsync(It.IsAny<AiOptimizeRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new AiModelUnavailableException("bundle ontbreek"));
        Assert.Equal(503, Assert.IsType<ObjectResult>(await Controller(1, "PJ").Optimize(new AiOptimizeRequest { RoleCode = "PJ" }, CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task Feedback_IsRecordedForTheCaller_OnlyForTheirOwnAdvice()
    {
        var ok = await Controller(11, "PJ").Feedback(new AiFeedbackRequest { AdviceId = 1, Helpful = true });
        Assert.IsType<OkResult>(ok);
        Assert.Equal((11, 1L, true), _store.Feedback);

        Assert.IsType<NotFoundObjectResult>(await Controller(11, "PJ").Feedback(new AiFeedbackRequest { AdviceId = 99, Helpful = false }));
        Assert.IsType<BadRequestObjectResult>(await Controller(11, "PJ").Feedback(new AiFeedbackRequest { AdviceId = 0 }));
    }

    [Fact]
    public void AdminEndpoints_AreRestrictedToAdmins_AndTheWholeControllerRequiresAuth()
    {
        var t = typeof(AiHelperController);
        Assert.NotNull(t.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).SingleOrDefault());

        foreach (var name in new[] { "Harvest", "ReloadModels", "UploadModel", "DryRun", "ExportObservations", "ExportAdviceLog" })
        {
            var attr = t.GetMethod(name)!.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().SingleOrDefault();
            Assert.True(attr?.Roles == "Admin", $"{name} must be [Authorize(Roles = \"Admin\")]");
        }
    }

    [Fact]
    public void Status_ReportsTheModelAndTheLlm()
    {
        var ok = Assert.IsType<OkObjectResult>(Controller(1, "PJ").Status());
        Assert.NotNull(ok.Value);
    }
}
