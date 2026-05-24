#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Feature 137 Tier-2 — local citizen submits AssuredIdentity action 1 against a
# register OWNED BY n1 (replicated to this SyncOnly node). The submission fans
# out to n1 for sealing. Run AFTER local is up as a SyncOnly replica, subscribed
# to the n1 register, with the F137 blueprint recovered.

$ErrorActionPreference = "Stop"
Import-Module (Join-Path $PSScriptRoot "../modules/SorchaWalkthrough/SorchaWalkthrough.psm1") -Force

$gw          = "http://localhost"
$tenantUrl   = "$gw/api"
$walletUrl   = "$gw/api"
$blueprintUrl= "$gw/api"
$publicOrgId = "00000000-0000-0000-0000-000000000002"
$sysOrgId    = "00000000-0000-0000-0000-000000000001"
$registerId  = "deccbf4dc9ad4edebe5d6a3651da80b9"
$blueprintId = "assured-identity-20260524151245"
$citizenEmail = "citizen.crossnode@local.node"
$pw          = "Dev_Pass_2025!"

Write-Host "== Step 1: sysadmin login (local) =="
$admin = Connect-SorchaUser -TenantUrl $tenantUrl -Email "admin@local.node" -Password $pw -OrganizationId $sysOrgId

Write-Host "== Step 2: register + verify local citizen =="
try { Register-SorchaPublicUser -TenantUrl $tenantUrl -Email $citizenEmail -Password $pw -DisplayName "Cross Node Citizen" | Out-Null } catch { Write-Host "  register: $($_.Exception.Message)" }
$users = Invoke-SorchaApi -Method GET -Uri "$tenantUrl/organizations/$publicOrgId/users?includeInactive=true" -Headers $admin.Headers
$cu = $users.users | Where-Object { $_.email -eq $citizenEmail } | Select-Object -First 1
if ($cu) { Confirm-SorchaUserEmail -TenantUrl $tenantUrl -OrganizationId $publicOrgId -UserId $cu.id -Headers $admin.Headers; Write-Host "  verified $citizenEmail" }

Write-Host "== Step 3: citizen login (consumer) =="
$cit = Connect-SorchaUser -TenantUrl $tenantUrl -Email $citizenEmail -Password $pw -OrganizationId $publicOrgId
Write-Host "  citizen token acquired"

Write-Host "== Step 4: create citizen wallet =="
$wallet = New-SorchaWallet -WalletUrl $walletUrl -Name "Cross Node Citizen Wallet" -Headers $cit.Headers -FetchPublicKey
Write-Host "  wallet: $($wallet.Address)"

Write-Host "== Step 4b: register citizen participant (wallet-ownership check; late-bound to the open slot) =="
try {
    $null = Register-SorchaParticipant -TenantUrl $tenantUrl -WalletUrl $walletUrl -OrganizationId $publicOrgId -WalletAddress $wallet.Address -DisplayName "Cross Node Citizen" -Headers $cit.Headers
    Write-Host "  participant registered in public org"
} catch { Write-Host "  participant register: $($_.Exception.Message)" }

Write-Host "== Step 5: re-login (pick up wallet_address claim) + fetch holder-keys (F137) =="
$cit = Connect-SorchaUser -TenantUrl $tenantUrl -Email $citizenEmail -Password $pw -OrganizationId $publicOrgId
$holderKeys = Invoke-SorchaApi -Method GET -Uri "$walletUrl/v1/wallet/holder-keys" -Headers $cit.Headers
Write-Host "  holder-keys: alg=$($holderKeys.algorithm) wallet=$($holderKeys.walletAddress)"

Write-Host "== Step 6: create instance from REPLICATED blueprint (owned by n1) =="
$instance = Invoke-SorchaApi -Method POST -Uri "$blueprintUrl/instances/" -Headers $cit.Headers -Body @{
    blueprintId = $blueprintId
    registerId  = $registerId
    tenantId    = $publicOrgId
    metadata    = @{ source = "crossnode-tier2" }
}
$instanceId = $instance.id
Write-Host "  instance: $instanceId"

Write-Host "== Step 7: submit Action 1 (with holderKeys) — fans out to n1 =="
$payload = @{
    name    = @{ givenName = "Alex"; middleName = "Morgan"; familyName = "MacLeod"; fullName = "Alex Morgan MacLeod" }
    dob     = @{ dateOfBirth = "1990-06-21" }
    email   = @{ email = $citizenEmail }
    address = @{ line1 = "12 Castle Street"; town = "Edinburgh"; region = "Lothian"; postcode = "EH1 2DU"; country = "Scotland" }
    holderKeys = @{ holderJwk = $holderKeys.holderJwk; encryptionPublicKey = $holderKeys.encryptionPublicKey; algorithm = $holderKeys.algorithm }
}
$resp = Invoke-SorchaAction -BlueprintUrl $blueprintUrl -InstanceId $instanceId -ActionId "1" -BlueprintId $blueprintId -SenderWallet $wallet.Address -RegisterId $registerId -Token $cit.Token -PayloadData $payload
Write-Host "  submit response: $($resp | ConvertTo-Json -Compress -Depth 4)"

# Persist for the analyst-approve + delivery-poll steps
@{
    instanceId = $instanceId; registerId = $registerId; blueprintId = $blueprintId
    citizenWallet = $wallet.Address; citizenEmail = $citizenEmail
    txId = ($resp.transactionId ?? $resp.txId)
} | ConvertTo-Json | Set-Content -Path (Join-Path $PSScriptRoot "crossnode-state.json")
Write-Host "== DONE. instance=$instanceId tx=$($resp.transactionId ?? $resp.txId) =="
