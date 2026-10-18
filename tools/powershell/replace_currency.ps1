$dir = "src\WebApp\WebApp\Components"

# ConfirmAd.razor
$file = "$dir\Dialogs\ConfirmAd.razor"
(Get-Content $file) -replace '"R {Ad.Price}"', '"$ {Ad.Price.ToString(""0.00"")}"' | Set-Content $file

# AdDetail.razor
$file = "$dir\Pages\AdDetail.razor"
(Get-Content $file) -replace 'R @_ad.Price', '$ @_ad.Price.ToString("0.00")' | Set-Content $file

# EditAd.razor
$file = "$dir\Pages\EditAd.razor"
(Get-Content $file) -replace 'Prys \(R\)', 'Prys ($)' | Set-Content $file

# HuntingPackets.razor
$file = "$dir\Pages\HuntingPackets.razor"
(Get-Content $file) -replace 'Prys \(R\)', 'Prys ($)' -replace 'R @packet.StockPrice.ToString\("N2"\)', '$ @packet.StockPrice.ToString("N2")' -replace 'R @_selectedPacket.StockPrice.ToString\("N2"\)', '$ @_selectedPacket.StockPrice.ToString("N2")' | Set-Content $file

# SearchAds.razor
$file = "$dir\Pages\SearchAds.razor"
(Get-Content $file) -replace 'R @ad.Price', '$ @ad.Price.ToString("0.00")' | Set-Content $file

# ManageAds.razor
$file = "$dir\Pages\ManageAds.razor"
(Get-Content $file) -replace '"R {context.Price}"', '"$ {context.Price.ToString(""0.00"")}"' | Set-Content $file

# ManagePackets.razor
$file = "$dir\Pages\ManagePackets.razor"
(Get-Content $file) -replace 'ToString\("C"\)', 'ToString("C", new System.Globalization.CultureInfo("en-US"))' -replace 'Prys \(ZAR\)', 'Prys (USD)' | Set-Content $file

# ManagePhPackages.razor
$file = "$dir\Pages\ManagePhPackages.razor"
(Get-Content $file) -replace 'ToString\("C"\)', 'ToString("C", new System.Globalization.CultureInfo("en-US"))' -replace 'Prys per dag \(ZAR\)', 'Prys per dag (USD)' | Set-Content $file

# ManageVgPackages.razor
$file = "$dir\Pages\ManageVgPackages.razor"
(Get-Content $file) -replace 'ToString\("C"\)', 'ToString("C", new System.Globalization.CultureInfo("en-US"))' -replace 'Prys \(ZAR\)', 'Prys (USD)' | Set-Content $file

# RoleAdmin.razor
$file = "$dir\Pages\RoleAdmin.razor"
(Get-Content $file) -replace 'AdornmentText="R"', 'AdornmentText="$"' | Set-Content $file

Write-Host "Replaced all currency occurrences!"
