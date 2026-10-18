import re
import os

html_template = """@page "{route}"

@using WebApp.Models
@using WebApp.Services

@inject NavigationManager NavManager

<PageTitle>{title}</PageTitle>

<MudPaper Elevation="0" Class="pa-6" Style="
    width:100%;
    background: rgba(247,231,204,0.90);
    border: 1px solid rgba(60,40,30,0.3);
    border-radius: 12px;
    box-shadow: 0 10px 30px rgba(0,0,0,0.25);
    max-height: calc(100vh - 160px);
    overflow:hidden;
    display:flex;
    flex-direction:column;">

    <MudStack Row="true" AlignItems="AlignItems.Center" Class="mb-4">
        <div>
            <MudText Typo="Typo.h4" Style="color:#2C1F18;">{title}</MudText>
            <MudText Typo="Typo.body2" Style="color:#1A2215;">{subtitle}</MudText>
        </div>
        <MudSpacer />
        {button_html}
    </MudStack>

    <MudGrid Class="mb-4">
        <MudItem xs="12" sm="4">
            <MudCard Elevation="1" Style="background:rgba(108,80,55,0.10); border-radius:10px;">
                <MudCardContent Class="pa-3">
                    <MudStack Row="true" AlignItems="AlignItems.Center">
                        <MudIcon Icon="@Icons.Material.Filled.List" Style="color:#6C5037;" />
                        <div>
                            <MudText Typo="Typo.body2" Style="color:#1A2215;">Totale Rekords</MudText>
                            <MudText Typo="Typo.h5" Style="color:#2C1F18;">@{list_name}.Count</MudText>
                        </div>
                    </MudStack>
                </MudCardContent>
            </MudCard>
        </MudItem>
    </MudGrid>

    <div style="flex:1; overflow-y:auto; min-height:0; padding-right:6px;">
        {table_html}
    </div>
</MudPaper>

@code {{
{code_behind}
}}
"""

pages = [
    {'file': 'ManageVgPackages.razor', 'route': '/vg/packages', 'title': 'VG Pakkette', 'subtitle': 'Bestuur VG Pakkette', 'tab': 'VG Pakkette', 'list_name': '_vgPackages'},
    {'file': 'ManageVgBookings.razor', 'route': '/vg/bookings', 'title': 'VG Besprekings', 'subtitle': 'Bestuur VG Besprekings', 'tab': 'Besprekings', 'role': 'VG', 'list_name': '_vgBookings'},
    {'file': 'ManagePhPackages.razor', 'route': '/ph/packages', 'title': 'PH Jag Pakkette', 'subtitle': 'Bestuur PH Jag Pakkette', 'tab': 'Jag Pakkette', 'list_name': '_phPackages'},
    {'file': 'ManagePhBookings.razor', 'route': '/ph/bookings', 'title': 'PH Besprekings', 'subtitle': 'Bestuur PH Besprekings', 'tab': 'Besprekings', 'role': 'PH', 'list_name': '_phBookings'},
    {'file': 'ManageFarmBookings.razor', 'route': '/boer/bookings', 'title': 'Plaas Besprekings', 'subtitle': 'Bestuur Plaas Besprekings', 'tab': 'Besprekings', 'role': 'Boer', 'list_name': '_farmBookings'},
    {'file': 'ManageHunterBookings.razor', 'route': '/jagter/bookings', 'title': 'Jagter Besprekings', 'subtitle': 'Bestuur Jagter Besprekings', 'tab': 'Besprekings', 'role': 'Jagter', 'list_name': '_bookings'},
    {'file': 'ManagePermits.razor', 'route': '/jagter/permits', 'title': 'My Permitte', 'subtitle': 'Bestuur Permitte', 'tab': 'Permitte', 'list_name': '_permits'}
]

with open('RoleAdmin.razor', 'r', encoding='utf-8') as f:
    content = f.read()

# Extract code blocks defining lists
code_section_match = re.search(r'@code\s*\{(.*)\}', content, re.DOTALL)
code_section = code_section_match.group(1) if code_section_match else ""

for page in pages:
    list_name = page['list_name']
    
    # Extract the table
    # We find the table by searching for `<MudTable Items="_vgPackages"` and extracting the matching element
    # Use non-greedy match to find the closing tag
    table_match = re.search(r'(<MudTable\s+Items="' + list_name + r'".*?</MudTable>)', content, re.DOTALL)
    table_html = table_match.group(1) if table_match else "<!-- Table not found -->"
    
    # Check if there is an ADD button in that tab
    btn_html = ""
    if page['tab'] in ['VG Pakkette', 'PH Jag Pakkette', 'Permitte']:
        btn_html = '''<MudButton Variant="Variant.Filled" Style="background-color:#6C5037; color:#F7E7CC; border-radius:8px;">Nuwe byvoeg</MudButton>'''

    # Extract list definition from code block
    list_def_match = re.search(r'(private List<[A-Za-z0-9_]+>\s+' + list_name + r'\s*=\s*new[^\;]*;)', code_section, re.DOTALL)
    list_def = list_def_match.group(1) if list_def_match else f"private List<object> {list_name} = new();"
    
    # Write to file
    out_content = html_template.format(
        route=page['route'],
        title=page['title'],
        subtitle=page['subtitle'],
        list_name=list_name,
        table_html=table_html,
        button_html=btn_html,
        code_behind=f"    {list_def}"
    )
    with open(page['file'], 'w', encoding='utf-8') as out_f:
        out_f.write(out_content)

print("Generated pages.")
