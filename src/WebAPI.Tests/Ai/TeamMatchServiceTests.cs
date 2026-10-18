using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using WebAPI.Controllers;
using WebAPI.Database;
using WebAPI.Models;
using WebAPI.Services.Ai;

namespace WebAPI.Tests.Ai;

public class TeamMatchServiceTests
{
    private sealed class FakeTeamStore : ITeamMatchStore
    {
        public TeamGraph Graph { get; } = new();
        public int Loads { get; private set; }
        public Task<TeamGraph> LoadGraphAsync() { Loads++; return Task.FromResult(Graph); }

        public TeamProfile Add(int urId, int userId, string role, string name, string[]? provinces = null, double? rating = null, int ratings = 0,
            long years = 0, decimal? rate = null, int completed = 0, bool visible = true)
        {
            var p = new TeamProfile
            {
                UserRoleId = urId, UserId = userId, RoleCode = role, Name = name, YearsExperience = years, Visible = visible,
                RatingAverage = rating, RatingCount = ratings, DailyRate = rate, CompletedHunts = completed
            };
            foreach (var pr in provinces ?? Array.Empty<string>()) p.Provinces.Add(pr);
            Graph.ByUserRoleId[urId] = p;
            return p;
        }
    }

    private const string Reasons = """{"summary":"Kontak eers K1.","matches":[{"id":"K1","reason":"Werk in u gebied."},{"id":"K2","reason":"Verder weg."}]}""";

    private static TeamMatchService Service(FakeTeamStore store, ILlmClient? llm = null) =>
        new(store, llm ?? FakeLlm.Returning(Reasons), NullLogger<TeamMatchService>.Instance);

    /// <summary>A PJ in Limpopo, a strong local PH, a far-away unrated PH, a videographer and a few that must never be suggested.</summary>
    private static FakeTeamStore World()
    {
        var s = new FakeTeamStore();
        s.Add(1, 100, "PJ", "Piet Uitruster", new[] { "limpopo" });
        s.Add(2, 200, "PH", "Sannie Plaaslik", new[] { "limpopo" }, rating: 4.8, ratings: 12, years: 15, rate: 3000, completed: 9);
        s.Add(3, 300, "PH", "Koos Ver", new[] { "western_cape" }, years: 2, rate: 6000);
        s.Add(4, 400, "VG", "Vera Video", new[] { "gauteng" }, rating: 4.2, ratings: 3, years: 5, rate: 2500);
        s.Add(5, 500, "PH", "Ongeverifieer", new[] { "limpopo" }, rating: 5, ratings: 50, visible: false);
        s.Add(6, 600, "PH", "Reeds In Span", new[] { "limpopo" }, rating: 5, ratings: 50);
        s.Add(7, 100, "PH", "Piet Self (ook PH)", new[] { "limpopo" }, rating: 5, ratings: 50);
        s.Add(8, 800, "FARMER", "Boer Gert", new[] { "limpopo" }, rating: 4.0, ratings: 2, rate: 1200);
        s.Graph.TeamRows.Add((1, 6));
        return s;
    }

    [Fact]
    public async Task Advertiser_IsExcluded_WithTheNotice_AndNothingIsLoaded()
    {
        var store = World();
        var res = await Service(store).FindAsync(new AiTeamRequest { RoleCode = "AP" }, 900, CancellationToken.None);
        Assert.True(res.Excluded);
        Assert.Contains("Adverteerders (AP)", res.ExclusionMessage);
        Assert.Empty(res.Matches);
        Assert.Equal(0, store.Loads);
    }

    [Fact]
    public async Task Pj_GetsMembers_RankedByFit_WithoutSelfUnverifiedOrExistingTeam()
    {
        var res = await Service(World()).FindAsync(new AiTeamRequest { RoleCode = "PJ" }, 100, CancellationToken.None);

        var ids = res.Matches.Select(m => m.UserRoleId).ToList();
        Assert.DoesNotContain(5, ids);   // unverified / suspended
        Assert.DoesNotContain(6, ids);   // already in the team
        Assert.DoesNotContain(7, ids);   // the caller's own other role
        Assert.DoesNotContain(1, ids);   // PJs are not suggested to a PJ
        Assert.Equal(2, ids[0]);         // local, well rated, experienced
        Assert.True(res.Matches.First(m => m.UserRoleId == 2).Score > res.Matches.First(m => m.UserRoleId == 3).Score);
        Assert.All(res.Matches, m => Assert.True(m.CanInvite));
        Assert.Equal(new[] { "limpopo" }, res.ViewerProvinces);
        Assert.Equal("prior", res.Model.Source);
    }

    [Fact]
    public async Task Pj_CanNarrowTheRoles()
    {
        var res = await Service(World()).FindAsync(new AiTeamRequest { RoleCode = "PJ", WantRoles = new() { "VG", "Boer" } }, 100, CancellationToken.None);
        Assert.All(res.Matches, m => Assert.Contains(m.RoleCode, new[] { "VG", "FARMER" }));
        Assert.Equal(2, res.Matches.Count);
    }

    [Fact]
    public async Task Budget_PenalisesRatesAboveIt()
    {
        var withBudget = await Service(World()).FindAsync(new AiTeamRequest { RoleCode = "PJ", MaxDailyRate = 3500 }, 100, CancellationToken.None);
        double RateOf(int ur) => withBudget.Matches.First(m => m.UserRoleId == ur).Components.First(c => c.Key == "rate").Value;
        Assert.Equal(1.0, RateOf(2));                 // R3 000 <= R3 500
        Assert.True(RateOf(3) < 0.5);                 // R6 000 is well over
        Assert.Contains(withBudget.Matches.First(m => m.UserRoleId == 3).Facts, f => f.Contains("bo u begroting"));
    }

    [Fact]
    public async Task PendingInvite_CannotBeResent()
    {
        var store = World();
        store.Graph.Invites[(1, 2)] = "Pending";
        var res = await Service(store).FindAsync(new AiTeamRequest { RoleCode = "PJ" }, 100, CancellationToken.None);
        var m = res.Matches.First(x => x.UserRoleId == 2);
        Assert.Equal("invite_pending", m.Relationship);
        Assert.False(m.CanInvite);
    }

    [Fact]
    public async Task Member_GetsOutfitters_AndCannotInvite()
    {
        var res = await Service(World()).FindAsync(new AiTeamRequest { RoleCode = "PH" }, 300, CancellationToken.None);
        Assert.All(res.Matches, m => Assert.Equal("PJ", m.RoleCode));
        Assert.All(res.Matches, m => Assert.False(m.CanInvite));
        Assert.Equal("Werkvolume (voltooide jagte)", res.Matches[0].Components.First(c => c.Key == "rate").Label);
    }

    [Fact]
    public async Task ProvinceOverride_IsUsed_AndUnknownProvinceIsRejected()
    {
        var res = await Service(World()).FindAsync(new AiTeamRequest { RoleCode = "PJ", Province = "Wes-Kaap" }, 100, CancellationToken.None);
        Assert.Equal(new[] { "western_cape" }, res.ViewerProvinces);
        Assert.Equal(3, res.Matches[0].UserRoleId);   // now the local one

        await Assert.ThrowsAsync<AiValidationException>(() =>
            Service(World()).FindAsync(new AiTeamRequest { RoleCode = "PJ", Province = "Atlantis" }, 100, CancellationToken.None));
    }

    [Fact]
    public async Task LlmReasons_AreAttachedById_AndTheLlmNeverSeesNamesOrContactDetails()
    {
        var llm = FakeLlm.Returning(Reasons);
        var res = await Service(World(), llm).FindAsync(new AiTeamRequest { RoleCode = "PJ" }, 100, CancellationToken.None);

        Assert.Equal("ok", res.NarrativeStatus);
        Assert.Equal("Kontak eers K1.", res.Summary);
        Assert.Equal("Werk in u gebied.", res.Matches[0].Reason);
        Assert.Equal("Verder weg.", res.Matches[1].Reason);
        Assert.DoesNotContain("Sannie", llm.LastUser);
        Assert.DoesNotContain("Koos", llm.LastUser);
        Assert.DoesNotContain("Vera", llm.LastUser);
    }

    [Fact]
    public async Task LlmDown_StillReturnsTheRankedMatches()
    {
        var res = await Service(World(), FakeLlm.Failing("Gemini is af")).FindAsync(new AiTeamRequest { RoleCode = "PJ" }, 100, CancellationToken.None);
        Assert.Equal("unavailable", res.NarrativeStatus);
        Assert.Contains("Gemini is af", res.NarrativeError);
        Assert.NotEmpty(res.Matches);
        Assert.All(res.Matches, m => Assert.Null(m.Reason));
    }

    [Fact]
    public async Task EnoughRealOutcomes_TrainTheModel_AndItTakesAShareOfTheScore()
    {
        var store = World();
        // 12 extra PJ-member pairs: close + well-rated members accepted, far + unrated members declined.
        for (int i = 0; i < 12; i++)
        {
            bool good = i % 2 == 0;
            int pj = 1000 + i, member = 2000 + i;
            store.Add(pj, 10_000 + i, "PJ", $"PJ {i}", new[] { "limpopo" });
            store.Add(member, 20_000 + i, "PH", $"PH {i}", new[] { good ? "limpopo" : "western_cape" }, rating: good ? 4.9 : null, ratings: good ? 20 : 0);
            store.Graph.Invites[(pj, member)] = good ? "Accepted" : "Declined";
        }

        var res = await Service(store).FindAsync(new AiTeamRequest { RoleCode = "PJ" }, 100, CancellationToken.None);

        Assert.Equal("prior+learned", res.Model.Source);
        Assert.Equal(13, res.Model.LabelledPairs);                     // 12 invites + the existing team row
        Assert.Equal(Math.Round(13 / 63.0, 2), res.Model.LearnedShare);
        Assert.True(res.Model.LearnedCoefficients!["proximity"] > 0);  // it learned that being close matters
        Assert.Equal(2, res.Matches[0].UserRoleId);
    }

    [Fact]
    public void TooFewOutcomes_DoNotTrainAModel()
    {
        var data = Enumerable.Range(0, 9).Select(i => (new[] { 1.0, 1, 0.5, 0 }, i % 2)).ToList();
        Assert.Null(LogisticModel.Train(data));
    }

    [Theory]
    [InlineData("limpopo", "limpopo", 1.0)]
    [InlineData("limpopo", "gauteng", 0.6)]
    [InlineData("limpopo", "western_cape", 0.15)]
    public void Proximity_SameNeighbouringFar(string a, string b, double expected)
    {
        Assert.Equal(expected, TeamMatchService.Proximity(new[] { a }, new[] { b }));
    }

    [Fact]
    public void Proximity_UnknownAreaIsNeutral_AndOneFiveStarReviewIsNotPerfect()
    {
        Assert.Equal(0.5, TeamMatchService.Proximity(Array.Empty<string>(), new[] { "limpopo" }));
        var one = new TeamProfile { RatingAverage = 5, RatingCount = 1 };
        var many = new TeamProfile { RatingAverage = 5, RatingCount = 40 };
        Assert.True(TeamMatchService.Credentials(one) < TeamMatchService.Credentials(many));
    }

    // ------------------------------------------------------------------ controller

    private static AiHelperController Controller(ITeamMatcher team, int userId, params string[] roles)
    {
        var c = new AiHelperController(new Moq.Mock<IPackageAdvisor>().Object, new FakeStore(), AiTestSupport.RealModel(), new NullLlmClient(),
            Microsoft.Extensions.Options.Options.Create(AiTestSupport.Options()), team);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } };
        return c;
    }

    [Fact]
    public async Task Endpoint_RequiresTheRole()
    {
        var result = await Controller(Service(World()), 300, "PH").TeamMatches(new AiTeamRequest { RoleCode = "PJ" }, CancellationToken.None);
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Endpoint_ReturnsMatchesForTheCallersOwnRole()
    {
        var result = await Controller(Service(World()), 100, "PJ").TeamMatches(new AiTeamRequest { RoleCode = "PJ" }, CancellationToken.None);
        var body = Assert.IsType<AiTeamResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.NotEmpty(body.Matches);
    }
}
