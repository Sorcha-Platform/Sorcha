#!/usr/bin/env pwsh
# Quick test to verify register creation flow

$ErrorActionPreference = "Stop"

$token = ""

# Authenticate
Write-Host "Authenticating..." -ForegroundColor Yellow
$body = "grant_type=password&username=admin@perf.local&password=PerfTest2026!&client_id=sorcha-cli"
$authResponse = Invoke-RestMethod -Method Post `
    -Uri 'http://localhost/api/service-auth/token' `
    -Body $body `
    -ContentType 'application/x-www-form-urlencoded' `
    -UseBasicParsing

$token = $authResponse.access_token
Write-Host "✓ Authenticated" -ForegroundColor Green

# Create wallet
Write-Host "Creating wallet..." -ForegroundColor Yellow
$walletBody = @{
    name = "Test Wallet"
    algorithm = "ED25519"
    wordCount = 12
} | ConvertTo-Json

$walletResponse = Invoke-RestMethod -Method Post `
    -Uri 'http://localhost/api/v1/wallets' `
    -Headers @{ Authorization = "Bearer $token" } `
    -Body $walletBody `
    -ContentType 'application/json'

$walletAddress = $walletResponse.wallet.address
Write-Host "✓ Wallet created: $walletAddress" -ForegroundColor Green

# Initiate register
Write-Host "Initiating register..." -ForegroundColor Yellow
$initiateBody = @{
    name = "Test Register"
    description = "Test register for performance testing"
    tenantId = "00000000-0000-0000-0000-000000000000"
    advertise = $false
    owners = @(
        @{
            userId = "test-admin"
            walletId = $walletAddress
        }
    )
} | ConvertTo-Json -Depth 10

Write-Host "Request body:" -ForegroundColor Gray
Write-Host $initiateBody -ForegroundColor Gray

try {
    $initiateResponse = Invoke-RestMethod -Method Post `
        -Uri 'http://localhost/api/registers/initiate' `
        -Headers @{ Authorization = "Bearer $token" } `
        -Body $initiateBody `
        -ContentType 'application/json'

    Write-Host "✓ Register initiated" -ForegroundColor Green
    Write-Host "  Register ID: $($initiateResponse.registerId)" -ForegroundColor White
    Write-Host "  Nonce: $($initiateResponse.nonce)" -ForegroundColor White
    Write-Host "  Attestations: $($initiateResponse.attestationsToSign.Count)" -ForegroundColor White
}
catch {
    Write-Host "✗ Register initiation failed" -ForegroundColor Red
    Write-Host "  Status: $($_.Exception.Response.StatusCode.value__)" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red

    try {
        $errorStream = $_.Exception.Response.GetResponseStream()
        $errorReader = New-Object System.IO.StreamReader($errorStream)
        $errorBody = $errorReader.ReadToEnd()
        Write-Host "  Response body: $errorBody" -ForegroundColor Red
    } catch {}

    exit 1
}
