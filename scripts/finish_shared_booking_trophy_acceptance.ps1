param(
    [Parameter(Mandatory)][int]$BookingId,
    [string]$ApiBaseUrl = 'http://localhost:9082',
    [string]$EnvironmentFile = (Join-Path $PSScriptRoot '..\.env.dev')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Open-Database([string]$Path) {
    $setting = Get-Content -LiteralPath $Path |
        Where-Object { $_ -match '^ConnectionStrings__DefaultConnection=' } |
        Select-Object -Last 1
    if (-not $setting) { throw "ConnectionStrings__DefaultConnection is missing from $Path." }
    $connection = [System.Data.SqlClient.SqlConnection]::new(
        ($setting -replace '^ConnectionStrings__DefaultConnection=', ''))
    $connection.Open()
    return $connection
}

function Invoke-Api([string]$Method, [string]$Path, [string]$Token, $Body) {
    $parameters = @{
        Uri = "$ApiBaseUrl$Path"
        Method = $Method
        Headers = @{ Authorization = "Bearer $Token"; Accept = 'application/json' }
        SkipHttpErrorCheck = $true
    }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = $Body | ConvertTo-Json -Depth 12 -Compress
    }
    $response = Invoke-WebRequest @parameters
    $json = if ([string]::IsNullOrWhiteSpace($response.Content)) {
        $null
    }
    else {
        $response.Content | ConvertFrom-Json -Depth 20
    }
    return [pscustomobject]@{ Code = [int]$response.StatusCode; Json = $json; Content = $response.Content }
}

function Require-Status($Response, [int]$Expected, [string]$Operation) {
    if ($Response.Code -ne $Expected) {
        throw "$Operation returned HTTP $($Response.Code). $($Response.Content)"
    }
}

function Login([string]$Email, [int]$RoleId, [string]$Password) {
    $body = @{ email = $Email; password = $Password; userRoleId = $RoleId } |
        ConvertTo-Json -Compress
    return [string](Invoke-RestMethod -Uri "$ApiBaseUrl/api/auth/login" -Method Post -ContentType 'application/json' -Body $body).accessToken
}

$environmentPath = (Resolve-Path -LiteralPath $EnvironmentFile).Path
$password = "Finish-$([Guid]::NewGuid().ToString('N'))!"
$passwordHash = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($password)))
$passwordHash = $passwordHash.ToLowerInvariant()

$db = Open-Database $environmentPath
try {
    $lookup = $db.CreateCommand()
    $lookup.CommandText = @'
SELECT hu.userId,hur.userRoleId,hu.uEmail,
       pu.userId,pur.userRoleId,pu.uEmail,
       vu.userId,vur.userRoleId,vu.uEmail
FROM dbo.bookings b
JOIN dbo.userRoles hur ON hur.userRoleId=b.bookHunterId
JOIN dbo.users hu ON hu.userId=hur.urUserId
JOIN dbo.ph p ON p.phId=b.bookPhId
JOIN dbo.userRoles pur ON pur.userRoleId=p.phUserRoleId
JOIN dbo.users pu ON pu.userId=pur.urUserId
JOIN dbo.vg v ON v.vgId=b.bookVgId
JOIN dbo.userRoles vur ON vur.userRoleId=v.vgUserRoleId
JOIN dbo.users vu ON vu.userId=vur.urUserId
WHERE b.bookingId=@BookingId
  AND hu.uEmail LIKE 'acceptance.h1.%@example.invalid'
  AND pu.uEmail LIKE 'acceptance.ph.%@example.invalid'
  AND vu.uEmail LIKE 'acceptance.vg.%@example.invalid';
'@
    [void]$lookup.Parameters.Add('@BookingId', [Data.SqlDbType]::Int)
    $lookup.Parameters['@BookingId'].Value = $BookingId
    $reader = $lookup.ExecuteReader()
    if (-not $reader.Read()) { throw "Booking $BookingId is not an isolated acceptance booking." }
    $participants = [pscustomobject]@{
        HunterUser = $reader.GetInt32(0); HunterRole = $reader.GetInt32(1); HunterEmail = $reader.GetString(2)
        PhUser = $reader.GetInt32(3); PhRole = $reader.GetInt32(4); PhEmail = $reader.GetString(5)
        VgUser = $reader.GetInt32(6); VgRole = $reader.GetInt32(7); VgEmail = $reader.GetString(8)
    }
    $reader.Close()

    $update = $db.CreateCommand()
    $update.CommandText = 'UPDATE dbo.users SET uPassword=@Hash WHERE userId IN(@Hunter,@Ph,@Vg);'
    [void]$update.Parameters.Add('@Hash', [Data.SqlDbType]::NVarChar, 200)
    $update.Parameters['@Hash'].Value = $passwordHash
    foreach ($item in @(
        @('@Hunter', $participants.HunterUser),
        @('@Ph', $participants.PhUser),
        @('@Vg', $participants.VgUser))) {
        [void]$update.Parameters.Add($item[0], [Data.SqlDbType]::Int)
        $update.Parameters[$item[0]].Value = $item[1]
    }
    [void]$update.ExecuteNonQuery()
}
finally {
    $db.Close()
}

$hunterToken = Login $participants.HunterEmail $participants.HunterRole $password
$phToken = Login $participants.PhEmail $participants.PhRole $password
$vgToken = Login $participants.VgEmail $participants.VgRole $password

$tempPath = Join-Path ([IO.Path]::GetTempPath()) "mikpunt-trophy-$BookingId.png"
Add-Type -AssemblyName System.Drawing
$bitmap = [Drawing.Bitmap]::new(640, 360)
try {
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try { $graphics.Clear([Drawing.Color]::DarkOliveGreen) }
    finally { $graphics.Dispose() }
    $bitmap.Save($tempPath, [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $bitmap.Dispose()
}

try {
    $upload = Invoke-RestMethod -Uri "$ApiBaseUrl/api/trophies/photos" -Method Post -Headers @{ Authorization = "Bearer $hunterToken" } -Form @{ photo = Get-Item -LiteralPath $tempPath }
    $photoPath = [string]$upload.photoPath
    if ([string]::IsNullOrWhiteSpace($photoPath)) { throw 'Trophy upload returned no photo path.' }

    $submission = Invoke-Api POST '/api/trophy-submissions' $hunterToken @{
        bookingId = $BookingId
        species = 'Impala'
        trophyPhotoPath = $photoPath
        antlerSizeCm = 58.4
        carcassWeightKg = 62.0
        harvestDate = '2026-10-09'
        locationText = 'Acceptance test location'
        shotDistanceMeters = 145
        firearmType = '.308 Winchester'
        notes = 'Private local acceptance submission'
        supportingMediaIds = @()
        rowlandWard = $null
    }
    Require-Status $submission 201 'Trophy submission'
    $submissionId = [long]$submission.Json.submissionId
    if ($submission.Json.status -ne 'Pending') { throw 'New trophy submission was not Pending.' }

    $hunterReview = Invoke-Api POST "/api/trophy-submissions/$submissionId/review" $hunterToken @{
        action = 'Approve'; reason = $null; actionKey = [Guid]::NewGuid()
    }
    Require-Status $hunterReview 403 'Hunter approval denial'

    $correction = Invoke-Api POST "/api/trophy-submissions/$submissionId/review" $phToken @{
        action = 'RequestCorrection'; reason = 'Acceptance correction cycle'; actionKey = [Guid]::NewGuid()
    }
    Require-Status $correction 200 'PH correction request'
    $resubmit = Invoke-Api POST "/api/trophy-submissions/$submissionId/resubmit" $hunterToken @{
        notes = 'Acceptance correction supplied'; trophyPhotoPath = $null
    }
    Require-Status $resubmit 200 'Hunter resubmission'
    $approval = Invoke-Api POST "/api/trophy-submissions/$submissionId/review" $phToken @{
        action = 'Approve'; reason = 'Acceptance approval'; actionKey = [Guid]::NewGuid()
    }
    Require-Status $approval 200 'PH approval'
    if ($approval.Json.status -ne 'Approved') { throw 'PH approval did not return Approved.' }

    Require-Status (Invoke-Api GET '/api/trophies' $hunterToken $null) 200 'Hunter Trophy Room access'
    Require-Status (Invoke-Api GET '/api/trophies' $phToken $null) 403 'PH Trophy Room denial'
    Require-Status (Invoke-Api GET '/api/trophies' $vgToken $null) 403 'VG Trophy Room denial'

    $db = Open-Database $environmentPath
    try {
        $verify = $db.CreateCommand()
        $verify.CommandText = @'
SELECT s.status,s.approvedTrophyId,t.tApprovalStatus
FROM dbo.trophySubmissions s
LEFT JOIN dbo.trophies t ON t.trophyId=s.approvedTrophyId
WHERE s.submissionId=@Id;
'@
        [void]$verify.Parameters.Add('@Id', [Data.SqlDbType]::BigInt)
        $verify.Parameters['@Id'].Value = $submissionId
        $reader = $verify.ExecuteReader()
        if (-not $reader.Read()) { throw 'Approved trophy record was not found.' }
        $result = [pscustomobject]@{
            BookingId = $BookingId
            SubmissionId = $submissionId
            SubmissionStatus = $reader.GetString(0)
            ApprovedTrophyId = $reader.GetInt32(1)
            ApprovedTrophyStatus = $reader.GetString(2)
            HunterTrophyRoom = 'Allowed'
            PhTrophyRoom = 'Denied'
            VgTrophyRoom = 'Denied'
        }
        $reader.Close()
        $result | ConvertTo-Json
    }
    finally {
        $db.Close()
    }
}
finally {
    Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    $password = $null
    $passwordHash = $null
    $hunterToken = $null
    $phToken = $null
    $vgToken = $null
}
