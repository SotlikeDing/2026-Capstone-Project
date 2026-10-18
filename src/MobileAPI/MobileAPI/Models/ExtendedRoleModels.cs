namespace MobileApi.Models;

public class RegisterPjRequest
{
    public string BusinessName { get; set; } = string.Empty;
    public string BusinessRegistrationNumber { get; set; } = string.Empty;
    public int YearsOfExperience { get; set; }
    public string Bio { get; set; } = string.Empty;
}

public class RegisterPhRequest
{
    public int YearsOfExperience { get; set; }
    public string Bio { get; set; } = string.Empty;
}

public class RegisterBoerRequest
{
    public string BusinessName { get; set; } = string.Empty;
    public string BusinessRegistrationNumber { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
}

public class RegisterVgRequest
{
    public bool HasDrone { get; set; }
    public string DroneLicence { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyRegistrationNumber { get; set; } = string.Empty;
    public int YearsOfExperience { get; set; }
}

public class RegisterHunterRequest
{
    public string Country { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
}

