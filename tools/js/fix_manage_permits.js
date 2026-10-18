const fs = require('fs');
let pagePath = "src/WebApp/WebApp/Components/Pages/ManagePermits.razor";
let code = fs.readFileSync(pagePath, 'utf8');

let splitCode = code.split('@code {');

let cleanCodeBlock = `
    public class PermitDisplay
    {
        public int Id { get; set; }
        public string FullNames { get; set; } = "";
        public string Surname { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public SapsForm? FormObject { get; set; }
    }

    private List<PermitDisplay> _permits = new();
    private PermitDisplay? _selectedPermit;

    protected override async Task OnInitializedAsync()
    {
        if (!AuthService.IsAuthenticated || !AuthService.UserId.HasValue)
        {
            NavManager.NavigateTo("/login?ReturnUrl=/jagter/permits");
            return;
        }

        try
        {
            var profile = await ProfileApi.GetProfile(AuthService.UserId.Value);
            var jagterRole = profile?.Roles.FirstOrDefault(r => r.RoleCode == "Jagter" || r.RoleCode == "HUNTER");
            
            if (jagterRole != null)
            {
                var apps = await PermitApi.GetApplicationsByUserRole(jagterRole.Id);
                _permits = apps.Select(a => 
                {
                    var disp = new PermitDisplay
                    {
                        Id = a.Id,
                        Status = a.Status
                    };

                    if (!string.IsNullOrWhiteSpace(a.FilePath))
                    {
                        try
                        {
                            var form = System.Text.Json.JsonSerializer.Deserialize<SapsForm>(a.FilePath);
                            if (form != null)
                            {
                                disp.FullNames = form.PermitFullNames ?? "";
                                disp.Surname = form.PermitSurname ?? "";
                                disp.FromDate = form.FromDate;
                                disp.ToDate = form.ToDate;
                                disp.FormObject = form;
                            }
                        }
                        catch { /* Ignore parsing errors */ }
                    }

                    return disp;
                }).ToList();
            }
        }
        catch { /* Handle profile fetch error */ }
    }

    private void OnRowClick(MudBlazor.TableRowClickEventArgs<PermitDisplay> args)
    {
        _selectedPermit = args.Item;
    }

    private async Task DownloadPdf()
    {
        if (_selectedPermit?.FormObject != null)
        {
            var pdfBytes = SapsPdfService.FillSapsPdf(_selectedPermit.FormObject);
            await JS.InvokeVoidAsync(
                "downloadFile",
                $"SAPS520_{_selectedPermit.Id}.pdf",
                Convert.ToBase64String(pdfBytes)
            );
        }
    }
}
`;

fs.writeFileSync(pagePath, splitCode[0] + '@code {' + cleanCodeBlock);
console.log("Fixed @code block in ManagePermits.razor");
