using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.JSInterop;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WebApp.Models;

namespace WebApp.Services
{
    /// <summary>
    /// Generates the SAPS 520 (temporary firearm import/export permit) application form
    /// as a PDF client-side, entirely in-browser via QuestPDF (Community license - free
    /// for this non-commercial capstone use) and Blazor Server, then triggers a browser
    /// download through the "mikpuntDownloadPdf" JS helper. No server round-trip beyond
    /// the Blazor circuit itself - the form data is assumed already validated/collected
    /// by the calling page (Saps520Generator.razor).
    /// </summary>
    public class Saps520PdfService
    {
        private readonly IJSRuntime _js;

        public Saps520PdfService(IJSRuntime js)
        {
            _js = js;
            QuestPDF.Settings.License = LicenseType.Community;
        }

        /// <summary>Builds the A4 PDF document (header/content/footer) from form, base64-encodes it, and hands it to the browser to save as "SAPS520_<id/passport>.pdf".</summary>
        public async Task ExportSaps520PdfAsync(Saps520FormModel form)
        {
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Element(x => ComposeHeader(x));
                    page.Content().Element(x => ComposeContent(x, form));
                    page.Footer().Element(x => ComposeFooter(x));
                });
            });

            byte[] pdfBytes = document.GeneratePdf();
            string base64 = Convert.ToBase64String(pdfBytes);
            string docId = !string.IsNullOrWhiteSpace(form.PassportNum) 
                ? form.PassportNum 
                : (!string.IsNullOrWhiteSpace(form.IdNum) ? form.IdNum : (form.Id > 0 ? form.Id.ToString() : "Aansoek"));
            await _js.InvokeVoidAsync("mikpuntDownloadPdf", "SAPS520_" + docId + ".pdf", base64);
        }

        /// <summary>Title block: SAPS letterhead text and the form's official name/section reference.</summary>
        private void ComposeHeader(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().AlignCenter().Text("SOUTH AFRICAN POLICE SERVICE").FontSize(16).SemiBold();
                col.Item().AlignCenter().Text("APPLICATION FOR A TEMPORARY IMPORT / EXPORT PERMIT (SAPS 520)").FontSize(13).SemiBold();
                col.Item().AlignCenter().Text("Section 73 of the Firearms Control Act, 2000 (Act No 60 of 2000)").FontSize(9).FontColor(Colors.Grey.Darken2);
                col.Item().PaddingTop(6).LineHorizontal(1);
            });
        }

        /// <summary>
        /// Sections A-G of the form covering all official fillable fields from the SAPS 520
        /// declaration: applicant details, employment, addresses, contact numbers, marital status,
        /// partner details, travel itinerary, firearms (with action, frame, barrel serials), ammunition,
        /// and signed legal declaration.
        /// </summary>
        private void ComposeContent(IContainer container, Saps520FormModel form)
        {
            container.PaddingVertical(1).Column(col =>
            {
                col.Spacing(10);
                
                // Section A: Personal & Employment
                col.Item().Background(Colors.Grey.Darken4).Padding(3).Text("SECTION A: APPLICANT PERSONAL & EMPLOYMENT DETAILS").FontColor(Colors.White).SemiBold();
                col.Item().Column(sub =>
                {
                    sub.Spacing(3);
                    sub.Item().Text($"Full Names & Surname: {form.FullNames} {form.Surname} (Initials: {form.Initials})");
                    var idDisplay = !string.IsNullOrWhiteSpace(form.IdNum) 
                        ? $"SA ID Nommer: {form.IdNum}" 
                        : $"Paspoortnommer: {form.PassportNum}";
                    var dobStr = form.Dob.ToString("yyyy-MM-dd");
                    var ageStr = form.Age.HasValue ? $" | Age: {form.Age.Value}" : "";
                    sub.Item().Text($"Identification: {idDisplay} | DOB: {dobStr}{ageStr} | Gender: {form.Gender}");
                    
                    var empStr = form.IsSelfEmployed 
                        ? $"Self-Employed ({(string.IsNullOrWhiteSpace(form.SelfEmployedSpecify) ? "Yes" : form.SelfEmployedSpecify)})" 
                        : (string.IsNullOrWhiteSpace(form.EmployerName) ? "None specified" : form.EmployerName);
                    sub.Item().Text($"Profession: {form.Profession} | Employer: {empStr}");
                    if (!string.IsNullOrWhiteSpace(form.BusinessAddress))
                        sub.Item().Text($"Business Address: {form.BusinessAddress} (Postal Code: {form.BusinessPostalCode})");
                });

                // Section B: Residential & Contact Details
                col.Item().Background(Colors.Grey.Darken4).Padding(3).Text("SECTION B: ADDRESSES & CONTACT DETAILS").FontColor(Colors.White).SemiBold();
                col.Item().Column(sub =>
                {
                    sub.Spacing(3);
                    sub.Item().Text($"Residential Address: {form.ResidentialAddress} (Code: {form.ResidentialPostalCode}), Country: {form.Country}");
                    if (!string.IsNullOrWhiteSpace(form.PostalAddress))
                        sub.Item().Text($"Postal Address: {form.PostalAddress} (Code: {form.PostalCode})");
                    sub.Item().Text($"SA Contact Address / Lodge: {form.SaContactAddress}");
                    
                    var phones = new List<string>();
                    if (!string.IsNullOrWhiteSpace(form.CellPhone)) phones.Add($"Cell: {form.CellPhone}");
                    if (!string.IsNullOrWhiteSpace(form.ContactPhone)) phones.Add($"Tel: {form.ContactPhone}");
                    if (!string.IsNullOrWhiteSpace(form.HomeTelephone)) phones.Add($"Home: {form.HomeTelephone}");
                    if (!string.IsNullOrWhiteSpace(form.WorkTelephone)) phones.Add($"Work: {form.WorkTelephone}");
                    if (!string.IsNullOrWhiteSpace(form.Fax)) phones.Add($"Fax: {form.Fax}");
                    if (!string.IsNullOrWhiteSpace(form.Email)) phones.Add($"Email: {form.Email}");
                    sub.Item().Text($"Contact: {(phones.Count > 0 ? string.Join(" | ", phones) : "None provided")}");
                });

                // Section C: Marital Status & Partner
                col.Item().Background(Colors.Grey.Darken4).Padding(3).Text("SECTION C: MARITAL STATUS & SPOUSE / PARTNER").FontColor(Colors.White).SemiBold();
                col.Item().Column(sub =>
                {
                    sub.Spacing(3);
                    var marital = form.MaritalStatus == "Other" && !string.IsNullOrWhiteSpace(form.MaritalStatusOtherSpecify)
                        ? $"Other ({form.MaritalStatusOtherSpecify})"
                        : form.MaritalStatus;
                    sub.Item().Text($"Marital Status: {marital}");
                    if (!string.IsNullOrWhiteSpace(form.PartnerFullNameAndSurname))
                    {
                        var partnerId = !string.IsNullOrWhiteSpace(form.PartnerIdNum) 
                            ? $"SA ID: {form.PartnerIdNum}" 
                            : (!string.IsNullOrWhiteSpace(form.PartnerPassportNum) ? $"Passport: {form.PartnerPassportNum}" : "N/A");
                        sub.Item().Text($"Partner / Spouse: {form.PartnerFullNameAndSurname} ({partnerId})");
                    }
                });

                // Section D: Travel, Ports & Purpose
                col.Item().Background(Colors.Grey.Darken4).Padding(3).Text("SECTION D: TRAVEL, PORTS OF ENTRY/EXIT & PURPOSE").FontColor(Colors.White).SemiBold();
                col.Item().Column(sub =>
                {
                    sub.Spacing(3);
                    sub.Item().Text($"Origin: {(string.IsNullOrWhiteSpace(form.CountryOfOrigin) ? form.Country : form.CountryOfOrigin)} → Destination: {form.CountryOfDestination}");
                    sub.Item().Text($"Port of Entry: {form.PortOfEntry} | Port of Exit: {form.PortOfExit} | Flight: {form.FlightNumber}");
                    var fromStr = form.FromDate.HasValue ? form.FromDate.Value.ToString("yyyy-MM-dd") : form.ArrivalDate.ToString("yyyy-MM-dd");
                    var toStr = form.ToDate.HasValue ? form.ToDate.Value.ToString("yyyy-MM-dd") : form.DepartureDate.ToString("yyyy-MM-dd");
                    sub.Item().Text($"Permit Validity / Travel Dates: {fromStr} to {toStr}");
                    sub.Item().Text($"Reason for Permit: {form.ReasonForPermit}");
                });

                // Section E: Firearms Specification
                col.Item().Background(Colors.Grey.Darken4).Padding(3).Text("SECTION E: FIREARMS / VUURWAPENS").FontColor(Colors.White).SemiBold();
                col.Item().Column(sub =>
                {
                    sub.Spacing(6);
                    var firearms = form.Firearms != null && form.Firearms.Count > 0
                        ? form.Firearms
                        : new List<FirearmItemModel> { new FirearmItemModel { Type = form.FirearmType, Make = form.Make, Calibre = form.Calibre, SerialNumber = form.SerialNumber } };

                    sub.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2); // Type
                            columns.RelativeColumn(2); // Action
                            columns.RelativeColumn(2); // Make / Model
                            columns.RelativeColumn(2); // Calibre
                            columns.RelativeColumn(3); // Frame & Barrel Serial
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(2).Text("Type").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(2).Text("Action").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(2).Text("Make/Model").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(2).Text("Calibre").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(2).Text("Serial / Frame / Barrel").SemiBold();
                        });

                        foreach (var f in firearms)
                        {
                            var serialParts = new List<string>();
                            var mainSerial = !string.IsNullOrWhiteSpace(f.FrameReceiverSerialNumber) ? f.FrameReceiverSerialNumber : f.SerialNumber;
                            if (!string.IsNullOrWhiteSpace(mainSerial)) serialParts.Add($"Frame: {mainSerial}");
                            if (!string.IsNullOrWhiteSpace(f.BarrelSerialNumber)) serialParts.Add($"Barrel: {f.BarrelSerialNumber}");
                            var serialText = serialParts.Count > 0 ? string.Join(", ", serialParts) : (f.SerialNumber ?? "-");

                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(f.Type);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(string.IsNullOrWhiteSpace(f.Action) ? "Bolt Action" : f.Action);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(2).Text($"{f.Make} {f.Model}".Trim());
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(f.Calibre);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(serialText);
                        }
                    });
                });

                // Section F: Ammunition Details
                col.Item().Background(Colors.Grey.Darken4).Padding(3).Text("SECTION F: AMMUNITION / AMMUNISIE").FontColor(Colors.White).SemiBold();
                col.Item().Column(sub =>
                {
                    sub.Spacing(6);
                    var ammoList = form.Ammunition != null && form.Ammunition.Count > 0
                        ? form.Ammunition
                        : new List<AmmunitionItemModel> { new AmmunitionItemModel { Type = form.Calibre, Quantity = form.AmmoQuantity } };

                    sub.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4); // Calibre/Type
                            columns.RelativeColumn(2); // Quantity
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(2).Text("Calibre / Type").SemiBold();
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(2).Text("Quantity (Rounds)").SemiBold();
                        });

                        foreach (var a in ammoList)
                        {
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(a.Type);
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(2).Text($"{a.Quantity} rounds");
                        }
                    });
                });

                // Section G: Declaration & Confirmation
                col.Item().Background(Colors.Grey.Darken4).Padding(3).Text("SECTION G: LEGAL DECLARATION & APPLICANT CONFIRMATION").FontColor(Colors.White).SemiBold();
                col.Item().Column(sub =>
                {
                    sub.Spacing(3);
                    var declPlace = !string.IsNullOrWhiteSpace(form.ApplicantDeclarationPlace) ? form.ApplicantDeclarationPlace : (form.PortOfEntry ?? "Pretoria");
                    var declDate = form.ApplicantDeclarationDate.HasValue ? form.ApplicantDeclarationDate.Value.ToString("yyyy-MM-dd") : DateTime.Today.ToString("yyyy-MM-dd");
                    var declName = !string.IsNullOrWhiteSpace(form.ApplicantDeclarationName) ? form.ApplicantDeclarationName : $"{form.FullNames} {form.Surname}".Trim();
                    sub.Item().Text($"Applicant Declaration: I hereby declare that the above information is complete, true and correct.");
                    sub.Item().Text($"Declared by: {declName} | Place: {declPlace} | Date: {declDate}");

                    if (!string.IsNullOrWhiteSpace(form.PossessionName))
                    {
                        var possDate = form.PossessionDate.HasValue ? form.PossessionDate.Value.ToString("yyyy-MM-dd") : declDate;
                        sub.Item().Text($"Responsible Person in Possession: {form.PossessionName} | Place: {form.PossessionPlace} | Date: {possDate}");
                    }
                    sub.Item().Text($"Official Status: {form.Status} (Submitted to SAPS Central Firearms Register - CFR)");
                });
            });
        }

        /// <summary>Generation timestamp footer, stamped with the server's local clock (DateTime.Now, not UTC) at PDF-build time.</summary>
        private void ComposeFooter(IContainer container)
        {
            container.AlignCenter().Text(x =>
            {
                x.Span("Official SAPS 520 Permit Application - Generated via MikPunt Platform on ");
                x.Span($"{DateTime.Now:yyyy-MM-dd HH:mm}");
            });
        }
    }
}
