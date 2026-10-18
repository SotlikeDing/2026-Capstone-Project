using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace MobileApi.Models;

public class SapsForm : IValidatableObject
{
    public string? PermitIdNum { get; set; }
    public string? PermitPassportNum { get; set; }

    [Required(ErrorMessage = "Verpligtend")]
    public string PermitSurname { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public string PermitInitials { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public string PermitFullNames { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public DateTime? PermitDob { get; set; }
    
    [Required(ErrorMessage = "Verpligtend")]
    public string PermitGender { get; set; } = string.Empty;

    [Required(ErrorMessage = "Verpligtend")]
    public string ResidentialAddress { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public string ResidentialPostalCode { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public string PostalAddress { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public string PostalPostalCode { get; set; } = string.Empty;

    [Required(ErrorMessage = "Verpligtend")]
    public string Profession { get; set; } = string.Empty;
    
    public string SelfEmployedSpecify { get; set; } = string.Empty;
    public string EmployerName { get; set; } = string.Empty;
    public string BusinessAddress { get; set; } = string.Empty;
    public string BusinessPostalCode { get; set; } = string.Empty;
    
    public string? HomeTelephone { get; set; } = string.Empty;
    public string? WorkTelephone { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public string CellPhone { get; set; } = string.Empty;
    public string? Fax { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Verpligtend")]
    public string MaritalStatus { get; set; } = string.Empty;
    
    public string? PartnerIdNum { get; set; }
    public string? PartnerPassportNum { get; set; }
    public string PartnerFullNameAndSurname { get; set; } = string.Empty;

    [Required(ErrorMessage = "Verpligtend")]
    public string CountryOfOrigin { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public string CountryOfDestination { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public string PortOfEntry { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public string PortOfExit { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public string ReasonForPermit { get; set; } = string.Empty;

    [Required(ErrorMessage = "Verpligtend")]
    public DateTime? FromDate { get; set; }
    
    [Required(ErrorMessage = "Verpligtend")]
    public DateTime? ToDate { get; set; }

    public List<SapsFormFirearms>? Firearms { get; set; } = new();
    public List<SapsFormAmmunition>? Ammunition { get; set; } = new();

    // Verklaring (Declaration) Fields
    [Required(ErrorMessage = "Verpligtend")]
    public string PersonPossessionName { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public DateTime? PersonPossessionDate { get; set; }
    
    [Required(ErrorMessage = "Verpligtend")]
    public string PersonPossessionPlace { get; set; } = string.Empty;
    
    public byte[]? PersonPossessionSignature { get; set; }

    [Required(ErrorMessage = "Verpligtend")]
    public string PersonApplicantName { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Verpligtend")]
    public DateTime? PersonApplicantDate { get; set; }
    
    [Required(ErrorMessage = "Verpligtend")]
    public string PersonApplicantPlace { get; set; } = string.Empty;
    
    public byte[]? PersonApplicantSignature { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(PermitIdNum) && string.IsNullOrWhiteSpace(PermitPassportNum))
        {
            yield return new ValidationResult("SA ID nommer of Paspoort nommer is verpligtend (vul slegs een in).", new[] { nameof(PermitIdNum), nameof(PermitPassportNum) });
        }
        else if (!string.IsNullOrWhiteSpace(PermitIdNum) && !string.IsNullOrWhiteSpace(PermitPassportNum))
        {
            yield return new ValidationResult("Vul asseblief SLEGS SA ID nommer OF Paspoort nommer in, nie albei nie.", new[] { nameof(PermitIdNum), nameof(PermitPassportNum) });
        }

        if (!string.IsNullOrWhiteSpace(PartnerFullNameAndSurname))
        {
            if (string.IsNullOrWhiteSpace(PartnerIdNum) && string.IsNullOrWhiteSpace(PartnerPassportNum))
            {
                yield return new ValidationResult("Gade se SA ID nommer of Paspoort nommer is verpligtend (vul slegs een in).", new[] { nameof(PartnerIdNum), nameof(PartnerPassportNum) });
            }
            else if (!string.IsNullOrWhiteSpace(PartnerIdNum) && !string.IsNullOrWhiteSpace(PartnerPassportNum))
            {
                yield return new ValidationResult("Vul asseblief SLEGS gade se SA ID nommer OF Paspoort nommer in, nie albei nie.", new[] { nameof(PartnerIdNum), nameof(PartnerPassportNum) });
            }
        }
    }
}
