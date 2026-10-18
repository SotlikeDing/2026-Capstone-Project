const fs = require('fs');

let pagePath = "src/WebApp/WebApp/Components/Pages/TemporaryImportPage.razor";
let code = fs.readFileSync(pagePath, 'utf8');

// Replace <MudTextField @bind-Value="_form.PropertyName" ... />
// with <MudTextField @bind-Value="_form.PropertyName" For="@(() => _form.PropertyName)" ... />
code = code.replace(/(@bind-Value="_form\.(\w+)")/g, '$1 For="@(() => _form.$2)"');
code = code.replace(/(@bind-Date="_form\.(\w+)")/g, '$1 For="@(() => _form.$2)"');

// Wrap everything from <!-- ========================= --> down to the button in an EditForm
let startMarker = "    <!-- ========================= -->\n    <!-- PERSONAL -->";
let endMarker = "    </MudStack>\n\n</MudPaper>";

let newStart = `<EditForm Model="_form" OnValidSubmit="SaveForm">\n<DataAnnotationsValidator />\n` + startMarker;
let newEnd = `    </MudStack>\n</EditForm>\n\n</MudPaper>`;

code = code.replace(startMarker, newStart);
code = code.replace(endMarker, newEnd);

// Change button to submit
code = code.replace('OnClick="SaveForm" StartIcon="@Icons.Material.Filled.Send">Stuur</MudButton>', 'ButtonType="ButtonType.Submit" StartIcon="@Icons.Material.Filled.Send">Stuur</MudButton>');

fs.writeFileSync(pagePath, code);
console.log("Validation For tags and EditForm added to TemporaryImportPage.razor");
