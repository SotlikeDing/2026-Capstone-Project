using System.Text.Json.Serialization;

namespace WebAPI.Models;

/// <summary>Unused - no controller or database class references this class.</summary>
public class PersonResponse
{
    public int UserRoleId { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
}

/// <summary>Unused - no controller or database class references this class.</summary>
public class PersonDetailResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
}

/// <summary>Unused - no controller or database class references this class.</summary>
public class Person
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

/// <summary>Public "recruit"/discovery profile shape - what People_db.GetPeople/GetPerson actually returns.</summary>
public class PublicProfileResponse
{
    public int UserRoleId { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";
    public string RoleCode { get; set; } = "";
    public string RoleName { get; set; } = "";
    public string? Bio { get; set; }
    public string? Location { get; set; }
    public double? AverageRating { get; set; }
    public int RatingCount { get; set; }
    public bool IsPublic { get; set; }
    public DateTime? PublicUntil { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
}
