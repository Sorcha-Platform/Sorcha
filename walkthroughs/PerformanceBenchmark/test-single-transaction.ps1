#!/usr/bin/env pwsh
# Quick test to debug single transaction submission

$ErrorActionPreference = "Stop"

$token = ""
$registerId = "8c3a6ceea86d4663974467a0c2033fcd"  # From last test
$walletAddress = "ws11qqrg94pq2f8mtdgtfapmelme50uuec8ltvffxr9dzn6xgmle3nejkp5pauq"

# Authenticate
Write-Host "Authenticating..." -ForegroundColor Yellow
$body = "grant_type=password&username=admin@perf.local&password=PerfTest2026!&client_id=sorcha-cli"
$authResponse = Invoke-RestMethod -Method Post `
    -Uri 'http://localhost/api/service-auth/token' `
    -Body $body `
    -ContentType 'application/x-www-form-urlencoded' `
    -UseBasicParsing

$token = $authResponse.access_token
Write-Host "V Authenticated" -ForegroundColor Green

# Create test transaction
$payload = @{
    testData = "HELLO WORLD TEST"
    timestamp = (Get-Date).ToString("o")
    sequence = 1
}

# Serialize to canonical JSON and hash
# CRITICAL: Must use same serialization that validator will use
$payloadJson = $payload | ConvertTo-Json -Compress
$payloadElement = $payloadJson | ConvertFrom-Json  # Convert to object that will become JsonElement

# Re-serialize to get the actual JSON that will be sent
$canonicalJson = $payloadElement | ConvertTo-Json -Compress
$jsonBytes = [System.Text.Encoding]::UTF8.GetBytes($canonicalJson)
$sha256 = [System.Security.Cryptography.SHA256]::Create()
$payloadHashBytes = $sha256.ComputeHash($jsonBytes)
$payloadHash = [BitConverter]::ToString($payloadHashBytes).Replace('-', '').ToLowerInvariant()

Write-Host "Payload JSON: $canonicalJson" -ForegroundColor Gray
Write-Host "Payload Hash: $payloadHash" -ForegroundColor Gray

# Generate transaction ID
$txIdSource = "$registerId-$(Get-Date -Format 'o')-$(Get-Random)"
$txIdBytes = [System.Text.Encoding]::UTF8.GetBytes($txIdSource)
$txIdHashBytes = $sha256.ComputeHash($txIdBytes)
$transactionId = [BitConverter]::ToString($txIdHashBytes).Replace('-', '').ToLowerInvariant()

# Sign transaction ID
$dataToSignBase64 = [Convert]::ToBase64String($txIdHashBytes)
$signBody = @{
    transactionData = $dataToSignBase64
    isPreHashed = $true
} | ConvertTo-Json

$signResponse = Invoke-RestMethod -Method Post `
    -Uri "http://localhost/api/v1/wallets/$walletAddress/sign" `
    -Headers @{ Authorization = "Bearer $token" } `
    -Body $signBody `
    -ContentType "application/json" `
    -UseBasicParsing

Write-Host "V Transaction signed" -ForegroundColor Green

# Build validator request
$transaction = @{
    transactionId = $transactionId
    registerId = $registerId
    blueprintId = "performance-test-v1"
    actionId = "1"
    payload = $payloadElement
    payloadHash = $payloadHash
    signatures = @(
        @{
            publicKey = $signResponse.publicKey
            signatureValue = $signResponse.signature
            algorithm = if ($signResponse.algorithm) { $signResponse.algorithm } else { "ED25519" }
        }
    )
    createdAt = (Get-Date).ToUniversalTime().ToString("o")
    expiresAt = (Get-Date).AddMinutes(5).ToUniversalTime().ToString("o")
    priority = 1
    metadata = @{
        source = "debug-test"
    }
}

$txBody = $transaction | ConvertTo-Json -Depth 10

Write-Host "`nTransaction JSON:" -ForegroundColor Cyan
Write-Host $txBody -ForegroundColor Gray

# Submit to validator
Write-Host "`nSubmitting to validator..." -ForegroundColor Yellow

try {
    $response = Invoke-RestMethod -Method Post `
        -Uri "http://localhost/api/validator/transactions/validate" `
        -Headers @{ Authorization = "Bearer $token" } `
        -Body $txBody `
        -ContentType "application/json" `
        -UseBasicParsing

    Write-Host "V SUCCESS!" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 10)
}
catch {
    Write-Host "X FAILED" -ForegroundColor Red
    Write-Host "  Status: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red

    try {
        $errorStream = $_.Exception.Response.GetResponseStream()
        $errorReader = New-Object System.IO.StreamReader($errorStream)
        $errorBody = $errorReader.ReadToEnd()
        Write-Host "  Response:" -ForegroundColor Red
        Write-Host $errorBody -ForegroundColor Yellow
    } catch {}
}
