using MobileApi.Database;
using MobileApi.Models.Auth;

namespace MobileAPI.Tests;

/// <summary>
/// The app no longer lets anyone pick a role at login: the MobileAPI reads the user's
/// roles from userRoles and chooses the most capable mobile role itself.
/// </summary>
public class MobileRoleSelectionTests
{
    private static AuthenticatedUser UserWith(params (int id, string code)[] roles) => new()
    {
        AvailableRoles = roles
            .Select(r => new AuthenticatedRole { UserRoleId = r.id, RoleCode = r.code, RoleName = r.code })
            .ToList()
    };

    [Fact]
    public void A_single_role_is_used_as_is()
    {
        var user = UserWith((7, "VG"));
        Auth_db.SelectRole(user);
        Assert.Equal(7, user.UserRoleId);
        Assert.Equal("VG", user.RoleCode);
    }

    [Fact]
    public void Hunter_wins_over_PH_and_VG()
    {
        var user = UserWith((3, "PH"), (9, "VG"), (12, "HUNTER"));
        Auth_db.SelectRole(user);
        Assert.Equal(12, user.UserRoleId);
    }

    [Fact]
    public void A_supported_mobile_role_wins_over_web_only_roles()
    {
        var user = UserWith((1, "FARMER"), (2, "AP"), (5, "VG"));
        Auth_db.SelectRole(user);
        Assert.Equal("VG", user.RoleCode);
    }

    [Fact]
    public void A_duplicated_role_uses_the_oldest_row()
    {
        var user = UserWith((40, "PH"), (8, "PH"));
        Auth_db.SelectRole(user);
        Assert.Equal(8, user.UserRoleId);
    }

    [Fact]
    public void An_account_without_roles_gets_none()
    {
        var user = UserWith();
        Auth_db.SelectRole(user);
        Assert.Equal(0, user.UserRoleId);
    }
}
