[Net.ServicePointManager]::ServerCertificateValidationCallback = {$true}

$headers = @{
    "Content-Type" = "application/json"
}

$chat1 = @{
    createdAt = (Get-Date).AddMinutes(-5).ToString("o")
    title = "Jag Naweek - 15 Augustus"
    chatParticipants = @(
        @{ UserId = 12 },
        @{ UserId = 7 }
    )
    senderId = 7
    message = "More Helgard! Is jy nog reg vir die jagnaweek by my plaas in die Vrystaat?"
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Uri "http://localhost:5004/api/Chats/createChat" -Method Post -Headers $headers -Body $chat1

$chat2 = @{
    createdAt = (Get-Date).AddMinutes(-45).ToString("o")
    title = "Navrae oor Pakkette"
    chatParticipants = @(
        @{ UserId = 12 },
        @{ UserId = 2 }
    )
    senderId = 2
    message = "Goeiedag, kan u asb die nuutste pryse vir Blesbokke en Springbokke vir my aanstuur?"
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Uri "http://localhost:5004/api/Chats/createChat" -Method Post -Headers $headers -Body $chat2

$chat3 = @{
    createdAt = (Get-Date).AddHours(-3).ToString("o")
    title = "Vervoer Reëlings"
    chatParticipants = @(
        @{ UserId = 12 },
        @{ UserId = 1 }
    )
    senderId = 12
    message = "John, hoe laat ry ons Vrydag oggend? Ek wil net my bakkie gereed kry."
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Uri "http://localhost:5004/api/Chats/createChat" -Method Post -Headers $headers -Body $chat3

$chat4 = @{
    createdAt = (Get-Date).AddDays(-1).ToString("o")
    title = "Algemene Gesels"
    chatParticipants = @(
        @{ UserId = 12 },
        @{ UserId = 6 }
    )
    senderId = 6
    message = "Hallo Helgard, jou profiel verifikasie is suksesvol voltooi. Welkom by MikPunt!"
} | ConvertTo-Json -Depth 5

Invoke-RestMethod -Uri "http://localhost:5004/api/Chats/createChat" -Method Post -Headers $headers -Body $chat4
