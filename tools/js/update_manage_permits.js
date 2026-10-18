const fs = require('fs');

let pagePath = "src/WebApp/WebApp/Components/Pages/ManagePermits.razor";
let code = fs.readFileSync(pagePath, 'utf8');

// Inject JS and SapsPdfService
code = code.replace('@inject AuthService AuthService', '@inject AuthService AuthService\n@inject IJSRuntime JS\n@inject WebApp.Services.SapsPdfService SapsPdfService');

// Add FormObject to PermitDisplay
code = code.replace('public DateTime? ToDate { get; set; }', 'public DateTime? ToDate { get; set; }\n        public SapsForm? FormObject { get; set; }');

// Set FormObject in the Try block
code = code.replace('disp.ToDate = form.ToDate;', 'disp.ToDate = form.ToDate;\n                                disp.FormObject = form;');

// Add OnRowClick to MudTable
code = code.replace('<MudTable Items="_permits" Hover="true" Dense="true" Style="background-color:transparent;">', '<MudTable Items="_permits" Hover="true" Dense="true" Style="background-color:transparent;" T="PermitDisplay" OnRowClick="OnRowClick">');

// Add SelectedPermit UI
let endOfTable = `        </MudTable>\n    </div>`;
let selectedPermitUI = `        </MudTable>\n    </div>\n
    @if (_selectedPermit != null)
    {
        <MudDivider Class="my-4" />
        <MudText Typo="Typo.h6">Permit Besonderhede (#@_selectedPermit.Id)</MudText>
        <MudGrid>
            <MudItem xs="12" md="6">
                <MudText Typo="Typo.body1"><b>Naam:</b> @_selectedPermit.FullNames @_selectedPermit.Surname</MudText>
                <MudText Typo="Typo.body1"><b>Status:</b> @_selectedPermit.Status</MudText>
            </MudItem>
            <MudItem xs="12" md="6">
                <MudText Typo="Typo.body1"><b>Geldig vanaf:</b> @(_selectedPermit.FromDate?.ToShortDateString() ?? "-")</MudText>
                <MudText Typo="Typo.body1"><b>Geldig tot:</b> @(_selectedPermit.ToDate?.ToShortDateString() ?? "-")</MudText>
            </MudItem>
        </MudGrid>
        
        @if (_selectedPermit.FormObject != null)
        {
            <MudStack Row="true" Class="mt-4">
                <MudButton Variant="Variant.Filled" Style="background-color: #6C5037; color: #F7E7CC;" StartIcon="@Icons.Material.Filled.PictureAsPdf" OnClick="DownloadPdf">Laai PDF Af</MudButton>
                <MudButton Variant="Variant.Text" OnClick="() => _selectedPermit = null">Maak Toe</MudButton>
            </MudStack>
        }
    }`;
code = code.replace(endOfTable, selectedPermitUI);

// Add code behind methods
let endOfCode = `    }\n}\n`;
let methods = `
    private PermitDisplay? _selectedPermit;

    private void OnRowClick(TableRowClickEventArgs<PermitDisplay> args)
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
    }\n}\n`;
code = code.replace(endOfCode, methods);

fs.writeFileSync(pagePath, code);
console.log("ManagePermits updated with details panel and PDF download");
