namespace WebAPI.Models;

/// <summary>Registration/update payload for the PJ (outfitter) role.</summary>
public class RegisterPjRequest
{
    public string BusinessName { get; set; } = string.Empty;
    public string BusinessRegistrationNumber { get; set; } = string.Empty;
    public string TaxPinVat { get; set; } = string.Empty;
    public string PhysicalAddress { get; set; } = string.Empty;
    public string ProvincialLicenses { get; set; } = string.Empty;
    public int YearsOfExperience { get; set; } = 5;
    public string Bio { get; set; } = string.Empty;
}

/// <summary>Registration/update payload for the PH (professional hunter) role.</summary>
public class RegisterPhRequest
{
    public string ProvincialLicenses { get; set; } = string.Empty;
    public bool HasDangerousGameEndorsement { get; set; }
    public string DangerousGameDetails { get; set; } = string.Empty;
    public string AssociationName { get; set; } = string.Empty;
    public string AssociationMembershipNumber { get; set; } = string.Empty;
    public string FirstAidCertification { get; set; } = string.Empty;
    public DateTime? FirstAidExpiryDate { get; set; }
    public string FirearmCompetencyLicenses { get; set; } = string.Empty;
    public int YearsOfExperience { get; set; } = 5;
    public string Bio { get; set; } = string.Empty;
}

/// <summary>Registration/update payload for the Farmer (Boer) role.</summary>
public class RegisterBoerRequest
{
    public string BusinessName { get; set; } = string.Empty;
    public string BusinessRegistrationNumber { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
}

/// <summary>Registration/update payload for the VG (videographer) role.</summary>
public class RegisterVgRequest
{
    public bool HasDrone { get; set; } = true;
    public string SacaaRegistrationNumber { get; set; } = string.Empty;
    public string RplLicenseNumber { get; set; } = string.Empty;
    public string RocCertificateNumber { get; set; } = string.Empty;
    public string DroneLicence { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string CompanyRegistrationNumber { get; set; } = string.Empty;
    public int YearsOfExperience { get; set; } = 3;
}

/// <summary>Registration/update payload for the Hunter role.</summary>
public class RegisterHunterRequest
{
    public string Country { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string IdPassportNumber { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
}
