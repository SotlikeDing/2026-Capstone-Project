using System.Text.Json.Serialization;

namespace MobileApi.Models;

public class Team
{
    [JsonPropertyName("teamId")]
    public int TeamId { get; set; }

    [JsonPropertyName("teamPjUserRoleId")]
    public int TeamPjUserRoleId { get; set; }

    [JsonPropertyName("teamMemberUserRoleId")]
    public int TeamMemberUserRoleId { get; set; }
}

public class TeamMemberResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("memberUserRoleId")]
    public int MemberUserRoleId { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("surname")]
    public string? Surname { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }
}

public class AddTeamMemberRequest
{
    public int TeamPjId { get; set; }
    public int MemberId { get; set; }
}

public class AddTeamMemberResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class BrowseProfileResponse
{
    [JsonPropertyName("userRoleId")]
    public int UserRoleId { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("surname")]
    public string? Surname { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("roleCode")]
    public string? RoleCode { get; set; }

    [JsonPropertyName("roleName")]
    public string? RoleName { get; set; }
}

public class TeamInviteResponse
{
    [JsonPropertyName("inviteId")]
    public int InviteId { get; set; }

    [JsonPropertyName("pjUserRoleId")]
    public int PjUserRoleId { get; set; }

    [JsonPropertyName("pjName")]
    public string? PjName { get; set; }

    [JsonPropertyName("pjSurname")]
    public string? PjSurname { get; set; }

    [JsonPropertyName("pjEmail")]
    public string? PjEmail { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("inviteDate")]
    public DateTime InviteDate { get; set; }
}

public class SendInviteRequest
{
    public int PjUserId { get; set; }
    public int MemberUserRoleId { get; set; }
}

public class SendInviteResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}
