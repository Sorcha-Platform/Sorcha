#!/usr/bin/env pwsh
# Cross-Node File Transfer Setup
# Sender on LOCAL (Phaethon), Receiver on N1 (n1.sorcha.dev)
# Register created on LOCAL, subscribed by N1

$ErrorActionPreference = "Stop"

$LOCAL = "http://127.0.0.1"
$N1 = "https://n1.sorcha.dev"
$PASSWORD = "Dev_Pass_2025!"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Get-Token($BaseUrl, $Email, $Pass) {
    $resp = Invoke-RestMethod -Uri "$BaseUrl/api/auth/login" -Method POST -ContentType "application/json" `
        -Body (@{ email = $Email; password = $Pass } | ConvertTo-Json) -SkipCertificateCheck
    return $resp.access_token
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Cross-Node File Transfer Setup" -ForegroundColor Cyan
Write-Host "  Sender: LOCAL ($LOCAL)" -ForegroundColor Cyan
Write-Host "  Receiver: N1 ($N1)" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# === Step 1: Admin login on both ===
Write-Host "`n=== Step 1: Admin Login ===" -ForegroundColor Yellow
$localAdmin = Get-Token $LOCAL "admin@sorcha.local" $PASSWORD
Write-Host "[OK] Local admin logged in" -ForegroundColor Green
$n1Admin = Get-Token $N1 "admin@sorcha.local" $PASSWORD
Write-Host "[OK] N1 admin logged in" -ForegroundColor Green

# === Step 2: Register sender on LOCAL ===
Write-Host "`n=== Step 2: Register Sender (LOCAL) ===" -ForegroundColor Yellow
try {
    $null = Invoke-RestMethod -Uri "$LOCAL/api/auth/register" -Method POST -ContentType "application/json" -Body (@{
        email = "sender@cross-node.local"
        password = $PASSWORD
        displayName = "sender"
        orgSubdomain = "sender-node"
    } | ConvertTo-Json)
    Write-Host "[OK] Sender registered on LOCAL" -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode -eq 409) {
        Write-Host "[!] Sender already exists on LOCAL" -ForegroundColor DarkYellow
    } else { throw }
}

# === Step 3: Register receiver on N1 ===
Write-Host "`n=== Step 3: Register Receiver (N1) ===" -ForegroundColor Yellow
try {
    $null = Invoke-RestMethod -Uri "$N1/api/auth/register" -Method POST -ContentType "application/json" -Body (@{
        email = "receiver@cross-node.local"
        password = $PASSWORD
        displayName = "receiver"
        orgSubdomain = "receiver-node"
    } | ConvertTo-Json) -SkipCertificateCheck
    Write-Host "[OK] Receiver registered on N1" -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode -eq 409) {
        Write-Host "[!] Receiver already exists on N1" -ForegroundColor DarkYellow
    } else { throw }
}

# === Step 4: Create orgs ===
Write-Host "`n=== Step 4: Create Organizations ===" -ForegroundColor Yellow
$senderOrgId = (Invoke-RestMethod -Uri "$LOCAL/api/organizations" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $localAdmin" } -Body (@{
    name = "Sender Node"
    description = "Sender organisation on local node"
    adminEmail = "sender@cross-node.local"
} | ConvertTo-Json)).organizationId
Write-Host "[OK] Sender org: $senderOrgId" -ForegroundColor Green

$receiverOrgId = (Invoke-RestMethod -Uri "$N1/api/organizations" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $n1Admin" } -Body (@{
    name = "Receiver Node"
    description = "Receiver organisation on n1 node"
    adminEmail = "receiver@cross-node.local"
} | ConvertTo-Json) -SkipCertificateCheck).organizationId
Write-Host "[OK] Receiver org: $receiverOrgId" -ForegroundColor Green

# === Step 5: Login as sender/receiver in their orgs ===
Write-Host "`n=== Step 5: Login as Sender/Receiver ===" -ForegroundColor Yellow
$senderToken = Get-Token $LOCAL "sender@cross-node.local" $PASSWORD
Write-Host "[OK] Sender logged in on LOCAL" -ForegroundColor Green
$receiverToken = Get-Token $N1 "receiver@cross-node.local" $PASSWORD
Write-Host "[OK] Receiver logged in on N1" -ForegroundColor Green

# === Step 6: Create wallets ===
Write-Host "`n=== Step 6: Create Wallets ===" -ForegroundColor Yellow
$senderWallet = (Invoke-RestMethod -Uri "$LOCAL/api/v1/wallets" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $senderToken" } -Body (@{
    name = "Sender Cross-Node Wallet"
    algorithm = "ED25519"
} | ConvertTo-Json)).wallet
Write-Host "[OK] Sender wallet: $($senderWallet.address)" -ForegroundColor Green

$receiverWallet = (Invoke-RestMethod -Uri "$N1/api/v1/wallets" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $receiverToken" } -Body (@{
    name = "Receiver Cross-Node Wallet"
    algorithm = "ED25519"
} | ConvertTo-Json) -SkipCertificateCheck).wallet
Write-Host "[OK] Receiver wallet: $($receiverWallet.address)" -ForegroundColor Green

# === Step 7: Create participants ===
Write-Host "`n=== Step 7: Register Participants ===" -ForegroundColor Yellow
$senderParticipant = Invoke-RestMethod -Uri "$LOCAL/api/me/register-participant" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $senderToken" } -Body (@{
    displayName = "Sender"
    walletAddress = $senderWallet.address
} | ConvertTo-Json)
Write-Host "[OK] Sender participant: $($senderParticipant.id)" -ForegroundColor Green

$receiverParticipant = Invoke-RestMethod -Uri "$N1/api/me/register-participant" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $receiverToken" } -Body (@{
    displayName = "Receiver"
    walletAddress = $receiverWallet.address
} | ConvertTo-Json) -SkipCertificateCheck
Write-Host "[OK] Receiver participant: $($receiverParticipant.id)" -ForegroundColor Green

# === Step 8: Create register on LOCAL ===
Write-Host "`n=== Step 8: Create Register (LOCAL) ===" -ForegroundColor Yellow
$initResp = Invoke-RestMethod -Uri "$LOCAL/api/registers/initiate" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $senderToken" } -Body (@{
    name = "Cross-Node Register"
    description = "Register for cross-node file transfer testing"
    attestations = @(@{
        role = "Owner"
        subject = "did:sorcha:w:$($senderWallet.address)"
    })
} | ConvertTo-Json -Depth 5)
$registerId = $initResp.registerId
Write-Host "[OK] Register initiated: $registerId" -ForegroundColor Green

# Sign attestation
$attHash = $initResp.attestationHashes[0].hash
$signResp = Invoke-RestMethod -Uri "$LOCAL/api/v1/wallets/$($senderWallet.address)/sign" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $senderToken" } -Body (@{
    data = $attHash
    encoding = "hex"
} | ConvertTo-Json)

# Finalize
$finalizeResp = Invoke-RestMethod -Uri "$LOCAL/api/registers/finalize" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $senderToken" } -Body (@{
    registerId = $registerId
    signedAttestations = @(@{
        attestationData = @{
            role = "Owner"
            subject = "did:sorcha:w:$($senderWallet.address)"
            grantedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        }
        publicKey = $signResp.publicKey
        signature = $signResp.signature
        algorithm = $signResp.algorithm
    })
} | ConvertTo-Json -Depth 5)
Write-Host "[OK] Register created: $registerId" -ForegroundColor Green

# === Step 9: Subscribe receiver's org to the register on N1 ===
Write-Host "`n=== Step 9: Subscribe N1 to Register ===" -ForegroundColor Yellow
# Wait for advertisements to propagate
Write-Host "[i] Waiting 30s for peer advertisements..." -ForegroundColor DarkGray
Start-Sleep -Seconds 30

$null = Invoke-RestMethod -Uri "$N1/api/organizations/$receiverOrgId/register-subscriptions" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $receiverToken" } -Body (@{
    register_id = $registerId
    register_name = "Cross-Node Register"
} | ConvertTo-Json) -SkipCertificateCheck
Write-Host "[OK] N1 subscribed to register $registerId" -ForegroundColor Green

# === Step 10: Publish participants to register ===
Write-Host "`n=== Step 10: Publish Participants ===" -ForegroundColor Yellow
$null = Invoke-RestMethod -Uri "$LOCAL/api/organizations/$senderOrgId/participants/publish" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $senderToken" } -Body (@{
    registerId = $registerId
    participantId = $senderParticipant.id
} | ConvertTo-Json)
Write-Host "[OK] Sender published to register" -ForegroundColor Green

$null = Invoke-RestMethod -Uri "$N1/api/organizations/$receiverOrgId/participants/publish" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $receiverToken" } -Body (@{
    registerId = $registerId
    participantId = $receiverParticipant.id
} | ConvertTo-Json) -SkipCertificateCheck
Write-Host "[OK] Receiver published to register" -ForegroundColor Green

# === Step 11: Publish blueprint ===
Write-Host "`n=== Step 11: Publish Blueprint ===" -ForegroundColor Yellow
$templatePath = Join-Path $scriptDir "file-transfer-template.json"
$template = Get-Content $templatePath -Raw | ConvertFrom-Json

# Patch participant wallet addresses
$template.template.participants[0].walletAddress = $senderWallet.address
$template.template.participants[1].walletAddress = $receiverWallet.address

$blueprint = Invoke-RestMethod -Uri "$LOCAL/api/blueprints" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $senderToken" } -Body ($template | ConvertTo-Json -Depth 20)
$blueprintId = $blueprint.id
Write-Host "[OK] Blueprint created: $blueprintId" -ForegroundColor Green

$null = Invoke-RestMethod -Uri "$LOCAL/api/register/registers/$registerId/blueprints/publish" -Method POST -ContentType "application/json" -Headers @{ Authorization = "Bearer $senderToken" } -Body (@{
    blueprintId = $blueprintId
    registerId = $registerId
    publishedBy = $senderWallet.address
} | ConvertTo-Json)
Write-Host "[OK] Blueprint published to register" -ForegroundColor Green

# === Save state ===
$state = @{
    profile = "cross-node"
    localUrl = $LOCAL
    n1Url = $N1
    registerId = $registerId
    blueprintId = $blueprintId
    sender = @{
        email = "sender@cross-node.local"
        password = $PASSWORD
        organizationId = $senderOrgId
        walletAddress = $senderWallet.address
        publicKey = $senderWallet.publicKey
        node = "local"
    }
    receiver = @{
        email = "receiver@cross-node.local"
        password = $PASSWORD
        organizationId = $receiverOrgId
        walletAddress = $receiverWallet.address
        publicKey = $receiverWallet.publicKey
        node = "n1"
    }
}

$state | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $scriptDir "cross-node-state.json")

Write-Host "`n================================================================" -ForegroundColor Green
Write-Host "  CROSS-NODE SETUP COMPLETE" -ForegroundColor Green
Write-Host "  Register: $registerId" -ForegroundColor Green
Write-Host "  Blueprint: $blueprintId" -ForegroundColor Green
Write-Host "  Sender (LOCAL): $($senderWallet.address)" -ForegroundColor Green
Write-Host "  Receiver (N1): $($receiverWallet.address)" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
