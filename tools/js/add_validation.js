const fs = require('fs');

let modelPath = "src/WebApp/WebApp/Models/SapsForm.cs";
let code = fs.readFileSync(modelPath, 'utf8');

// Add using System.ComponentModel.DataAnnotations;
if (!code.includes("using System.ComponentModel.DataAnnotations;")) {
    code = code.replace("using System.Text.Json.Serialization;", "using System.Text.Json.Serialization;\nusing System.ComponentModel.DataAnnotations;");
}

// Add [Required] to strings that are not nullable, except some that might be ok.
// We will replace public string PropName with [Required(ErrorMessage="Verpligtend")] public string PropName
code = code.replace(/public string PermitSurname/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string PermitSurname');
code = code.replace(/public string PermitInitials/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string PermitInitials');
code = code.replace(/public string PermitFullNames/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string PermitFullNames');
code = code.replace(/public string PermitGender/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string PermitGender');

code = code.replace(/public string ResidentialAddress/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string ResidentialAddress');
code = code.replace(/public string ResidentialPostalCode/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string ResidentialPostalCode');
code = code.replace(/public string PostalAddress/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string PostalAddress');
code = code.replace(/public string PostalPostalCode/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string PostalPostalCode');

code = code.replace(/public string CellPhone/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string CellPhone');
code = code.replace(/public string Email/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string Email');

code = code.replace(/public string CountryOfOrigin/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string CountryOfOrigin');
code = code.replace(/public string CountryOfDestination/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string CountryOfDestination');
code = code.replace(/public string ReasonForPermit/g, '[Required(ErrorMessage = "Verpligtend")]\n    public string ReasonForPermit');

fs.writeFileSync(modelPath, code);
console.log("Validation added to SapsForm.cs");
