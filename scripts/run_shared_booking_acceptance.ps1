param(
    [string]$ApiBaseUrl = 'http://localhost:9082',
    [string]$EnvironmentFile = (Join-Path $PSScriptRoot '..\.env.dev'),
    [int]$RecapTimeoutMinutes = 8,
    [int]$YoutubeTimeoutMinutes = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Step([string]$Message) {
    Write-Host ("[{0:HH:mm:ss}] {1}" -f (Get-Date), $Message)
}

function Invoke-Api {
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST', 'PUT', 'PATCH', 'DELETE')][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [string]$Token,
        [object]$Body,
        [hashtable]$Form
    )

    $headers = @{ Accept = 'application/json' }
    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $headers.Authorization = "Bearer $Token"
    }
    $parameters = @{
        Uri               = "$ApiBaseUrl$Path"
        Method            = $Method
        Headers           = $headers
        SkipHttpErrorCheck = $true
    }
    if ($null -ne $Form) {
        $parameters.Form = $Form
    }
    elseif ($null -ne $Body) {
        $parameters.ContentType = 'application/json'
        $parameters.Body = $Body | ConvertTo-Json -Depth 20 -Compress
    }

    $response = Invoke-WebRequest @parameters
    $json = $null
    if (-not [string]::IsNullOrWhiteSpace($response.Content)) {
        try { $json = $response.Content | ConvertFrom-Json -Depth 30 }
        catch { $json = $null }
    }
    return [pscustomobject]@{
        StatusCode = [int]$response.StatusCode
        Json = $json
        Content = $response.Content
    }
}

function Assert-Status($Response, [int[]]$Expected, [string]$Operation) {
    if ($Expected -notcontains $Response.StatusCode) {
        $detail = if ($Response.Json -and $Response.Json.message) { $Response.Json.message } else { $Response.Content }
        throw "$Operation returned HTTP $($Response.StatusCode). $detail"
    }
}

function Login-TestUser([string]$Email, [string]$Password, [int]$UserRoleId) {
    $response = Invoke-Api -Method POST -Path '/api/auth/login' -Body @{
        email = $Email
        password = $Password
        userRoleId = $UserRoleId
    }
    Assert-Status $response @(200) "Login for $Email"
    if ([string]::IsNullOrWhiteSpace($response.Json.accessToken)) {
        throw "Login for $Email did not return an access token."
    }
    return [string]$response.Json.accessToken
}

function Upload-BookingPhoto {
    param(
        [int]$BookingId,
        [string]$Token,
        [System.IO.FileInfo]$Photo,
        [string]$Caption,
        [string]$CapturedAt,
        [switch]$IncludeGps
    )

    $form = @{
        photo = $Photo
        caption = $Caption
        originalCaptureTimestamp = $CapturedAt
        orientation = '1'
    }
    if ($IncludeGps) {
        $form.latitude = '-25.7500'
        $form.longitude = '28.1900'
    }
    $response = Invoke-Api -Method POST -Path "/api/bookings/$BookingId/media" -Token $Token -Form $form
    Assert-Status $response @(201) "Photo upload ($Caption)"
    return $response.Json
}

function Open-Database([string]$Path) {
    $setting = Get-Content -LiteralPath $Path |
        Where-Object { $_ -match '^ConnectionStrings__DefaultConnection=' } |
        Select-Object -Last 1
    if (-not $setting) { throw "ConnectionStrings__DefaultConnection is missing from $Path." }
    $connectionString = $setting -replace '^ConnectionStrings__DefaultConnection=', ''
    $connection = [System.Data.SqlClient.SqlConnection]::new($connectionString)
    $connection.Open()
    return $connection
}

function Add-Parameter($Command, [string]$Name, [System.Data.SqlDbType]$Type, $Value, [int]$Size = 0) {
    $parameter = if ($Size -ne 0) { $Command.Parameters.Add($Name, $Type, $Size) } else { $Command.Parameters.Add($Name, $Type) }
    $parameter.Value = $Value
    return $parameter
}

$environmentPath = (Resolve-Path -LiteralPath $EnvironmentFile).Path
$runKey = [Guid]::NewGuid().ToString('N').Substring(0, 10)
$testPassword = "Acceptance-$([Guid]::NewGuid().ToString('N'))!"
$passwordBytes = [Text.Encoding]::UTF8.GetBytes($testPassword)
$passwordHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($passwordBytes)).ToLowerInvariant()
$bookingStart = [datetime]'2026-10-08T08:00:00Z'
$bookingEnd = [datetime]'2026-10-10T16:00:00Z'
$captureTime = '2026-10-09T08:00:00Z'
$emails = [ordered]@{
    Hunter1 = "acceptance.h1.$runKey@example.invalid"
    Hunter2 = "acceptance.h2.$runKey@example.invalid"
    PH = "acceptance.ph.$runKey@example.invalid"
    VG = "acceptance.vg.$runKey@example.invalid"
    Outsider = "acceptance.outsider.$runKey@example.invalid"
}

$db = Open-Database $environmentPath
try {
    Write-Step 'Creating isolated acceptance users and a completed booking.'
    $seedSql = @'
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @HunterRoleId int = (SELECT roleId FROM dbo.roles WHERE UPPER(roleCode)='HUNTER');
DECLARE @PhRoleId int = (SELECT roleId FROM dbo.roles WHERE UPPER(roleCode)='PH');
DECLARE @VgRoleId int = (SELECT roleId FROM dbo.roles WHERE UPPER(roleCode)='VG');
IF @HunterRoleId IS NULL OR @PhRoleId IS NULL OR @VgRoleId IS NULL
    THROW 52001, 'Required acceptance roles are missing.', 1;

INSERT dbo.users(uName,uSurname,uEmail,uPassword,uRegistrationDate,uIsEmailVerified,uIsPhoneVerified,uIsSuspended)
VALUES('Acceptance','Hunter One',@H1Email,@Password,SYSUTCDATETIME(),1,1,0);
DECLARE @H1User int=SCOPE_IDENTITY();
INSERT dbo.userRoles(urUserId,urRoleId) VALUES(@H1User,@HunterRoleId);
DECLARE @H1Role int=SCOPE_IDENTITY();

INSERT dbo.users(uName,uSurname,uEmail,uPassword,uRegistrationDate,uIsEmailVerified,uIsPhoneVerified,uIsSuspended)
VALUES('Acceptance','Hunter Two',@H2Email,@Password,SYSUTCDATETIME(),1,1,0);
DECLARE @H2User int=SCOPE_IDENTITY();
INSERT dbo.userRoles(urUserId,urRoleId) VALUES(@H2User,@HunterRoleId);
DECLARE @H2Role int=SCOPE_IDENTITY();

INSERT dbo.users(uName,uSurname,uEmail,uPassword,uRegistrationDate,uIsEmailVerified,uIsPhoneVerified,uIsSuspended)
VALUES('Acceptance','Professional Hunter',@PhEmail,@Password,SYSUTCDATETIME(),1,1,0);
DECLARE @PhUser int=SCOPE_IDENTITY();
INSERT dbo.userRoles(urUserId,urRoleId) VALUES(@PhUser,@PhRoleId);
DECLARE @PhUserRole int=SCOPE_IDENTITY();
INSERT dbo.ph(phUserRoleId,phYearsOfExperience,phBio) VALUES(@PhUserRole,10,'Automated local acceptance account');
DECLARE @PhId int=SCOPE_IDENTITY();

INSERT dbo.users(uName,uSurname,uEmail,uPassword,uRegistrationDate,uIsEmailVerified,uIsPhoneVerified,uIsSuspended)
VALUES('Acceptance','Videographer',@VgEmail,@Password,SYSUTCDATETIME(),1,1,0);
DECLARE @VgUser int=SCOPE_IDENTITY();
INSERT dbo.userRoles(urUserId,urRoleId) VALUES(@VgUser,@VgRoleId);
DECLARE @VgUserRole int=SCOPE_IDENTITY();
INSERT dbo.vg(vgUserRoleId,vgHasDrone,vgCompanyName,vgYearsOfExperience) VALUES(@VgUserRole,0,'Acceptance Media',5);
DECLARE @VgId int=SCOPE_IDENTITY();

INSERT dbo.users(uName,uSurname,uEmail,uPassword,uRegistrationDate,uIsEmailVerified,uIsPhoneVerified,uIsSuspended)
VALUES('Acceptance','Unrelated Hunter',@OutsiderEmail,@Password,SYSUTCDATETIME(),1,1,0);
DECLARE @OutsiderUser int=SCOPE_IDENTITY();
INSERT dbo.userRoles(urUserId,urRoleId) VALUES(@OutsiderUser,@HunterRoleId);
DECLARE @OutsiderRole int=SCOPE_IDENTITY();

INSERT dbo.bookings(bookHunterId,bookStartDate,bookEndDate,bookStatus,bookHuntGuests,bookNonHuntGuests,
                    bookPhId,bookVgId,bookPaymentStatus,bookSpecialNotes,bookWantsVideographer)
VALUES(@H1Role,@StartDate,@EndDate,'Completed',2,0,@PhId,@VgId,'Paid',@BookingNote,1);
DECLARE @BookingId int=SCOPE_IDENTITY();
INSERT dbo.bookingCompanions(bookingId,userRoleId,inviteName,inviteEmail,status,addedByUserId,linkedAt)
VALUES(@BookingId,@H2Role,'Acceptance Hunter Two',@H2Email,'Linked',@H1User,SYSUTCDATETIME());
COMMIT;

SELECT @BookingId,@H1User,@H1Role,@H2User,@H2Role,@PhUser,@PhUserRole,@VgUser,@VgUserRole,@OutsiderUser,@OutsiderRole;
'@
    $seed = $db.CreateCommand()
    $seed.CommandText = $seedSql
    [void](Add-Parameter $seed '@H1Email' ([Data.SqlDbType]::NVarChar) $emails.Hunter1 320)
    [void](Add-Parameter $seed '@H2Email' ([Data.SqlDbType]::NVarChar) $emails.Hunter2 320)
    [void](Add-Parameter $seed '@PhEmail' ([Data.SqlDbType]::NVarChar) $emails.PH 320)
    [void](Add-Parameter $seed '@VgEmail' ([Data.SqlDbType]::NVarChar) $emails.VG 320)
    [void](Add-Parameter $seed '@OutsiderEmail' ([Data.SqlDbType]::NVarChar) $emails.Outsider 320)
    [void](Add-Parameter $seed '@Password' ([Data.SqlDbType]::NVarChar) $passwordHash 200)
    [void](Add-Parameter $seed '@StartDate' ([Data.SqlDbType]::Date) $bookingStart)
    [void](Add-Parameter $seed '@EndDate' ([Data.SqlDbType]::Date) $bookingEnd)
    [void](Add-Parameter $seed '@BookingNote' ([Data.SqlDbType]::NVarChar) "Private acceptance run $runKey" 2000)
    $reader = $seed.ExecuteReader()
    if (-not $reader.Read()) { throw 'The acceptance seed did not return identifiers.' }
    $ids = [pscustomobject]@{
        Booking = $reader.GetInt32(0)
        H1User = $reader.GetInt32(1); H1Role = $reader.GetInt32(2)
        H2User = $reader.GetInt32(3); H2Role = $reader.GetInt32(4)
        PhUser = $reader.GetInt32(5); PhRole = $reader.GetInt32(6)
        VgUser = $reader.GetInt32(7); VgRole = $reader.GetInt32(8)
        OutsiderUser = $reader.GetInt32(9); OutsiderRole = $reader.GetInt32(10)
    }
    $reader.Close()
}
finally {
    $db.Close()
}

Write-Step "Logging in the five acceptance accounts for booking $($ids.Booking)."
$tokens = [ordered]@{
    H1 = Login-TestUser $emails.Hunter1 $testPassword $ids.H1Role
    H2 = Login-TestUser $emails.Hunter2 $testPassword $ids.H2Role
    PH = Login-TestUser $emails.PH $testPassword $ids.PhRole
    VG = Login-TestUser $emails.VG $testPassword $ids.VgRole
    Outsider = Login-TestUser $emails.Outsider $testPassword $ids.OutsiderRole
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "mikpunt-acceptance-$runKey"
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
$photoPath = Join-Path $tempRoot 'acceptance.png'
$previewPath = Join-Path $tempRoot 'recap-preview.mp4'
Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new(640, 360)
try {
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::DarkOliveGreen)
    }
    finally {
        $graphics.Dispose()
    }
    $bitmap.Save($photoPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $bitmap.Dispose()
}
$photo = Get-Item -LiteralPath $photoPath

try {
    Write-Step 'Uploading Hunter 1 media before route selection (expected unmatched).'
    $h1Media = Upload-BookingPhoto $ids.Booking $tokens.H1 $photo 'Hunter 1 unmatched moment' $captureTime -IncludeGps
    if ($h1Media.matchMethod -ne 'Unmatched') { throw 'Pre-route Hunter 1 photo was expected to be unmatched.' }

    Write-Step 'Submitting the Hunter route and selecting it as the assigned PH.'
    $routeBody = @{
        sourceHuntSessionId = "acceptance-route-$runKey"
        points = @(
            @{ latitude = -25.7500; longitude = 28.1900; timestamp = '2026-10-09T08:00:00Z' },
            @{ latitude = -25.7510; longitude = 28.1910; timestamp = '2026-10-09T08:10:00Z' },
            @{ latitude = -25.7520; longitude = 28.1920; timestamp = '2026-10-09T08:20:00Z' }
        )
        pins = @(
            @{ id = 'acceptance-shot'; type = 'Shot'; latitude = -25.7500; longitude = 28.1900; timestamp = '2026-10-09T08:00:00Z'; note = 'Acceptance pin' }
        )
        distanceMeters = 300.0
        durationSeconds = 1200
        routeVersion = 1
    }
    $route = Invoke-Api -Method POST -Path "/api/bookings/$($ids.Booking)/routes" -Token $tokens.H1 -Body $routeBody
    Assert-Status $route @(201) 'Hunter route submission'
    $routeId = [long]$route.Json.routeId
    $selected = Invoke-Api -Method PUT -Path "/api/bookings/$($ids.Booking)/routes/$routeId/selection" -Token $tokens.PH
    Assert-Status $selected @(204) 'PH route selection'

    Write-Step 'Uploading matched media from Hunter 2, PH and VG.'
    $h2Media = Upload-BookingPhoto $ids.Booking $tokens.H2 $photo 'Hunter 2 timeline moment' '2026-10-09T08:10:00Z'
    $phMedia = Upload-BookingPhoto $ids.Booking $tokens.PH $photo 'PH GPS moment' '2026-10-09T08:05:00Z' -IncludeGps
    $vgMedia = Upload-BookingPhoto $ids.Booking $tokens.VG $photo 'VG GPS moment' '2026-10-09T08:06:00Z' -IncludeGps
    if ($h2Media.matchMethod -notin @('Timestamp', 'Pin')) { throw 'Hunter 2 timestamp matching did not run.' }
    if ($phMedia.matchMethod -ne 'GPS' -or $vgMedia.matchMethod -ne 'GPS') { throw 'GPS matching did not run for PH/VG media.' }

    Write-Step 'Correcting the uncertain Hunter 1 photo manually.'
    $manual = Invoke-Api -Method PATCH -Path "/api/bookings/$($ids.Booking)/media/$($h1Media.mediaId)" -Token $tokens.H1 -Body @{
        caption = 'Hunter 1 manually matched moment'
        adjustedCaptureTimestamp = $captureTime
        matchedRoutePointIndex = 0
        matchedPinId = 'acceptance-shot'
        matchMethod = 'Manual'
    }
    Assert-Status $manual @(204) 'Manual photo matching'

    Write-Step 'Checking shared access, VG coordinate redaction and unrelated-user denial.'
    foreach ($role in @('H1', 'H2', 'PH', 'VG')) {
        $list = Invoke-Api -Method GET -Path "/api/bookings/$($ids.Booking)/media" -Token $tokens[$role]
        Assert-Status $list @(200) "$role shared media list"
        if (@($list.Json).Count -ne 4) { throw "$role did not see all four shared photos." }
        if ($role -eq 'VG') {
            foreach ($item in @($list.Json)) {
                if ($null -ne $item.latitude -or $null -ne $item.longitude -or $null -ne $item.matchedRoutePointIndex -or $null -ne $item.matchedPinId) {
                    throw 'VG media response exposed precise route or GPS fields.'
                }
            }
        }
    }
    $outsider = Invoke-Api -Method GET -Path "/api/bookings/$($ids.Booking)/media" -Token $tokens.Outsider
    Assert-Status $outsider @(403) 'Unrelated Hunter media denial'

    Write-Step 'Requesting FFmpeg recap generation.'
    $recapTitle = "Mikpunt Private Acceptance $runKey"
    $recapRequest = Invoke-Api -Method POST -Path "/api/bookings/$($ids.Booking)/recaps" -Token $tokens.H1 -Body @{ title = $recapTitle }
    Assert-Status $recapRequest @(202) 'Recap request'
    $recapId = [long]$recapRequest.Json.recapId
    $recapDeadline = (Get-Date).AddMinutes($RecapTimeoutMinutes)
    do {
        Start-Sleep -Seconds 5
        $recapResponse = Invoke-Api -Method GET -Path "/api/bookings/$($ids.Booking)/recaps/$recapId" -Token $tokens.H1
        Assert-Status $recapResponse @(200) 'Recap status'
        Write-Step "Recap status: $($recapResponse.Json.status), progress $($recapResponse.Json.progress)%"
        if ($recapResponse.Json.status -eq 'Failed') { throw "Recap generation failed: $($recapResponse.Json.failureReason)" }
    } until ($recapResponse.Json.status -eq 'Completed' -or (Get-Date) -ge $recapDeadline)
    if ($recapResponse.Json.status -ne 'Completed') { throw 'Recap generation timed out.' }
    if ([long]$recapResponse.Json.outputFileSize -le 0 -or [int]$recapResponse.Json.durationSeconds -le 0) {
        throw 'The completed recap has invalid file size or duration metadata.'
    }

    $playback = Invoke-WebRequest -Uri "$ApiBaseUrl/api/recaps/$recapId/content" -Headers @{ Authorization = "Bearer $($tokens.H1)" } -OutFile $previewPath -PassThru -SkipHttpErrorCheck
    if ([int]$playback.StatusCode -ne 200 -or (Get-Item -LiteralPath $previewPath).Length -le 0) {
        throw 'Authorized recap playback failed.'
    }

    Write-Step 'Queueing a private resumable YouTube upload.'
    $publish = Invoke-Api -Method POST -Path "/api/bookings/$($ids.Booking)/recaps/$recapId/publish" -Token $tokens.H1 -Body @{
        idempotencyKey = [Guid]::NewGuid()
        privacyStatus = 'private'
    }
    Assert-Status $publish @(202) 'Private YouTube publish request'
    $youtubeDeadline = (Get-Date).AddMinutes($YoutubeTimeoutMinutes)
    $poll = 0
    do {
        Start-Sleep -Seconds 10
        $poll++
        if ($poll % 30 -eq 0) { $tokens.H1 = Login-TestUser $emails.Hunter1 $testPassword $ids.H1Role }
        $recapResponse = Invoke-Api -Method GET -Path "/api/bookings/$($ids.Booking)/recaps/$recapId" -Token $tokens.H1
        Assert-Status $recapResponse @(200) 'YouTube publish status'
        Write-Step "YouTube status: $($recapResponse.Json.uploadStatus), progress $($recapResponse.Json.uploadProgress)%"
        if ($recapResponse.Json.uploadStatus -in @('PermanentlyFailed', 'VerificationFailed')) {
            throw "YouTube publication failed: $($recapResponse.Json.failureReason)"
        }
    } until ($recapResponse.Json.uploadStatus -eq 'Published' -or (Get-Date) -ge $youtubeDeadline)
    if ($recapResponse.Json.uploadStatus -ne 'Published') { throw 'YouTube publication verification timed out.' }
    if ($recapResponse.Json.youtubePrivacyStatus -ne 'private' -or [string]::IsNullOrWhiteSpace($recapResponse.Json.youtubeUrl)) {
        throw 'Published YouTube metadata did not confirm a private video and valid URL.'
    }

    Write-Step 'Verifying the retained archive and delayed cleanup schedule.'
    $tokens.H1 = Login-TestUser $emails.Hunter1 $testPassword $ids.H1Role
    $cleanup = Invoke-Api -Method GET -Path "/api/bookings/$($ids.Booking)/recaps/$recapId/cleanup" -Token $tokens.H1
    Assert-Status $cleanup @(200) 'Cleanup status'
    if ($cleanup.Json.status -ne 'CleanupPending' -or ([datetime]$cleanup.Json.notBefore).ToUniversalTime() -le [datetime]::UtcNow) {
        throw 'Cleanup was not retained behind its configured safety period.'
    }

    Write-Step 'Submitting, correcting and approving a trophy through the assigned PH.'
    $trophyPhoto = Invoke-Api -Method POST -Path '/api/trophies/photos' -Token $tokens.H1 -Form @{ photo = $photo }
    Assert-Status $trophyPhoto @(201) 'Trophy photo upload'
    $submission = Invoke-Api -Method POST -Path '/api/trophy-submissions' -Token $tokens.H1 -Body @{
        bookingId = $ids.Booking
        species = 'Impala'
        trophyPhotoPath = $trophyPhoto.Json.photoPath
        antlerSizeCm = 58.4
        carcassWeightKg = 62.0
        harvestDate = '2026-10-09'
        locationText = 'Acceptance test location'
        shotDistanceMeters = 145
        firearmType = '.308 Winchester'
        notes = 'Private local acceptance submission'
        supportingMediaIds = @([long]$h1Media.mediaId, [long]$phMedia.mediaId)
        rowlandWard = $null
    }
    Assert-Status $submission @(201) 'Trophy submission'
    $submissionId = [long]$submission.Json.submissionId
    if ($submission.Json.status -ne 'Pending') { throw 'A new trophy submission was not Pending.' }

    $hunterReview = Invoke-Api -Method POST -Path "/api/trophy-submissions/$submissionId/review" -Token $tokens.H1 -Body @{
        action = 'Approve'; reason = $null; actionKey = [Guid]::NewGuid()
    }
    Assert-Status $hunterReview @(403) 'Hunter trophy-approval denial'

    $correction = Invoke-Api -Method POST -Path "/api/trophy-submissions/$submissionId/review" -Token $tokens.PH -Body @{
        action = 'RequestCorrection'; reason = 'Acceptance correction cycle'; actionKey = [Guid]::NewGuid()
    }
    Assert-Status $correction @(200) 'PH correction request'
    $resubmit = Invoke-Api -Method POST -Path "/api/trophy-submissions/$submissionId/resubmit" -Token $tokens.H1 -Body @{
        notes = 'Acceptance correction supplied'; trophyPhotoPath = $null
    }
    Assert-Status $resubmit @(200) 'Hunter trophy resubmission'
    $approval = Invoke-Api -Method POST -Path "/api/trophy-submissions/$submissionId/review" -Token $tokens.PH -Body @{
        action = 'Approve'; reason = 'Acceptance approval'; actionKey = [Guid]::NewGuid()
    }
    Assert-Status $approval @(200) 'PH trophy approval'
    if ($approval.Json.status -ne 'Approved') { throw 'PH approval did not return Approved.' }

    $phTrophyRoom = Invoke-Api -Method GET -Path '/api/trophies' -Token $tokens.PH
    Assert-Status $phTrophyRoom @(403) 'PH Trophy Room denial'
    $vgTrophyRoom = Invoke-Api -Method GET -Path '/api/trophies' -Token $tokens.VG
    Assert-Status $vgTrophyRoom @(403) 'VG Trophy Room denial'

    $db = Open-Database $environmentPath
    try {
        $verify = $db.CreateCommand()
        $verify.CommandText = @'
SELECT a.archiveId,a.privacyStatus,a.youtubeChannelId,a.youtubeVideoId,a.youtubeUrl,
       a.sourceMediaCount,a.hunterPhotoCount,a.phPhotoCount,a.vgPhotoCount,
       j.status,j.notBefore,
       s.status,s.approvedTrophyId,t.tApprovalStatus
FROM dbo.bookingVideoArchives a
JOIN dbo.mediaCleanupJobs j ON j.archiveId=a.archiveId
JOIN dbo.trophySubmissions s ON s.submissionId=@SubmissionId
LEFT JOIN dbo.trophies t ON t.trophyId=s.approvedTrophyId
WHERE a.recapId=@RecapId;
'@
        [void](Add-Parameter $verify '@SubmissionId' ([Data.SqlDbType]::BigInt) $submissionId)
        [void](Add-Parameter $verify '@RecapId' ([Data.SqlDbType]::BigInt) $recapId)
        $verification = $verify.ExecuteReader()
        if (-not $verification.Read()) { throw 'Archive or trophy verification records were not found.' }
        $summary = [pscustomobject]@{
            RunKey = $runKey
            BookingId = $ids.Booking
            RecapId = $recapId
            ArchiveId = $verification.GetInt64(0)
            PrivacyStatus = $verification.GetString(1)
            ChannelId = $verification.GetString(2)
            YoutubeVideoId = $verification.GetString(3)
            YoutubeUrl = $verification.GetString(4)
            SourceMediaCount = $verification.GetInt32(5)
            HunterPhotoCount = $verification.GetInt32(6)
            PhPhotoCount = $verification.GetInt32(7)
            VgPhotoCount = $verification.GetInt32(8)
            CleanupStatus = $verification.GetString(9)
            CleanupNotBeforeUtc = $verification.GetDateTime(10)
            TrophySubmissionId = $submissionId
            TrophyStatus = $verification.GetString(11)
            ApprovedTrophyId = $verification.GetInt32(12)
            ApprovedTrophyStatus = $verification.GetString(13)
        }
        $verification.Close()
    }
    finally {
        $db.Close()
    }

    if ($summary.PrivacyStatus -ne 'private' -or $summary.SourceMediaCount -ne 4 -or
        $summary.HunterPhotoCount -ne 2 -or $summary.PhPhotoCount -ne 1 -or $summary.VgPhotoCount -ne 1 -or
        $summary.CleanupStatus -ne 'CleanupPending' -or $summary.TrophyStatus -ne 'Approved' -or
        $summary.ApprovedTrophyStatus -ne 'Approved') {
        throw 'Final archive or trophy database assertions failed.'
    }

    Write-Step 'Acceptance flow completed successfully.'
    $summary | ConvertTo-Json -Depth 5
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
    $testPassword = $null
    $passwordHash = $null
    $tokens = $null
}
