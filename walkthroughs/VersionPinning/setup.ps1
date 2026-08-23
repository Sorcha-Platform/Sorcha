# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
<#
.SYNOPSIS
    Provisions the org, wallets, participants and register for the Feature 194 version-pinning
    acceptance test.
.DESCRIPTION
    Deliberately minimal. The acceptance test is about ONE property — an in-flight instance keeps
    the definition it started on — so this creates the smallest world that can demonstrate it:
    one org, one officer, one register, one open applicant.
.PARAMETER GatewayUrl
    Target node. Omit to use the -Profile default.
#>
[CmdletBinding()]
param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'n1',
    [string]$GatewayUrl,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$modulePath = Join-Path $PSScriptRoot '..' 'modules' 'SorchaWalkthrough' 'SorchaWalkthrough.psm1'
Import-Module $modulePath -Force

$statePath = Join-Path $PSScriptRoot 'state.json'
if ((Test-Path $statePath) -and -not $Force) {
    Write-Host "state.json already exists. Re-run with -Force to reprovision." -ForegroundColor Yellow
    exit 0
}
if (Test-Path $statePath) { Remove-Item $statePath -Force }

$sorchaEnv = if ($GatewayUrl) {
    Initialize-SorchaEnvironment -GatewayUrl $GatewayUrl
} else {
    Initialize-SorchaEnvironment -Profile $Profile
}

Write-Host ""
Write-Host "=== Feature 194 acceptance: provisioning ===" -ForegroundColor Cyan
Write-Host "Gateway: $($sorchaEnv.GatewayUrl)"

# A unique suffix per run. The register/org namespace on n1 accumulates, and reusing a name would
# silently reuse someone else's register — see the walkthrough-builder skill on org reuse.
$stamp = Get-Date -Format 'yyyyMMddHHmmss'
$sub = "vpin-$stamp"
$officerEmail = "officer@$sub.test"
$password = 'Vpin_Test_2026!'

$admin = Connect-SorchaAdmin -TenantUrl $sorchaEnv.TenantUrl `
    -AdminEmail 'admin@sorcha.local' -AdminPassword 'Dev_Pass_2025!'

# The org's own admin creates the org wallet (#1525) — pass -WalletUrl so New-SorchaOrganization
# does it while it still holds an admin session.
Write-Host "`n[1/6] Organisation + officer" -ForegroundColor Cyan
$org = New-SorchaOrganization `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Headers $admin.Headers `
    -Name "Version Pinning $stamp" `
    -Subdomain $sub `
    -AdminEmail $officerEmail `
    -AdminPassword $password `
    -AdminDisplayName 'Pinning Officer' `
    -AdminEmailVerified

$orgId = $org.OrganizationId
Write-Host "  org $orgId"

# Log in AS the officer. Single-org, so the password grant returns a token directly.
$officer = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl -Email $officerEmail -Password $password -OrganizationId $orgId

Write-Host "`n[2/6] Officer wallet" -ForegroundColor Cyan
$officerWallet = New-SorchaWallet -WalletUrl $sorchaEnv.WalletUrl -Headers $officer.Headers `
    -Algorithm 'ED25519' -Name "pinning-officer-$stamp" -FetchPublicKey
Write-Host "  wallet $($officerWallet.Address)"

Register-SorchaParticipant -TenantUrl $sorchaEnv.TenantUrl -WalletUrl $sorchaEnv.WalletUrl `
    -Headers $officer.Headers -OrganizationId $orgId `
    -WalletAddress $officerWallet.Address -DisplayName 'Pinning Officer' | Out-Null

# RE-LOGIN. wallet_address is added to the JWT only at login, from the first active linked wallet.
# Without this the F142 publish HARD gate refuses with "you do not hold a publish-governance role",
# which reads as a permissions problem and is actually a stale token.
$officer = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl -Email $officerEmail -Password $password -OrganizationId $orgId
$ownerWallet = (Decode-SorchaJwt $officer.Token).wallet_address
if (-not $ownerWallet) { throw "Officer token carries no wallet_address claim after re-login." }
Write-Host "  token wallet_address $ownerWallet"

Write-Host "`n[3/6] Register" -ForegroundColor Cyan
$register = New-SorchaRegister `
    -RegisterUrl $sorchaEnv.RegisterUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "VersionPinning $stamp" `
    -Description 'Feature 194 acceptance register' `
    -TenantId $orgId `
    -OwnerUserId $officer.UserId `
    -OwnerWalletAddress $ownerWallet `
    -Headers $officer.Headers
$registerId = $register.RegisterId
Write-Host "  register $registerId"

Write-Host "`n[4/6] Waiting for the genesis roster to seal" -ForegroundColor Cyan
# The genesis control tx that records the owner roster seals ASYNC. Publishing before it lands
# reads an EMPTY roster and fail-closes with the same 403 as a missing role.
$null = Wait-SorchaRegisterRoster -GatewayUrl $sorchaEnv.GatewayUrl -RegisterId $registerId -Headers $officer.Headers
Write-Host "  roster sealed"

Write-Host "`n[5/6] Publishing the officer participant onto the register" -ForegroundColor Cyan
$pub = Publish-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl -OrganizationId $orgId -RegisterId $registerId `
    -ParticipantName 'Pinning Officer' -OrganizationName "Version Pinning $stamp" `
    -WalletAddress $ownerWallet -PublicKey $officerWallet.PublicKey -Headers $officer.Headers

if ($pub -and $pub.PSObject.Properties['transactionId'] -and $pub.transactionId) {
    Wait-SorchaActorReady -Mode ParticipantSealed -TxId $pub.transactionId `
        -RegisterId $registerId -Headers $officer.Headers -GatewayUrl $sorchaEnv.GatewayUrl | Out-Null
}
Write-Host "  participant sealed"

Write-Host "`n[6/6] Applicant (public citizen — late-bound at runtime)" -ForegroundColor Cyan
$publicOrgId = '00000000-0000-0000-0000-000000000002'
$applicantEmail = "applicant-$stamp@vpin.test"
$applicantUser = Register-SorchaPublicUser -TenantUrl $sorchaEnv.TenantUrl -Email $applicantEmail `
    -Password $password -DisplayName 'Pinning Applicant'
$applicantUserId = if ($applicantUser -is [string]) { $applicantUser } else { $applicantUser.UserId }
Confirm-SorchaUserEmail -TenantUrl $sorchaEnv.TenantUrl -Headers $admin.Headers `
    -OrganizationId $publicOrgId -UserId $applicantUserId | Out-Null
$applicant = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl -Email $applicantEmail -Password $password -OrganizationId $publicOrgId

$applicantWallet = New-SorchaWallet -WalletUrl $sorchaEnv.WalletUrl -Headers $applicant.Headers `
    -Algorithm 'ED25519' -Name "pinning-applicant-$stamp"
Register-SorchaParticipant -TenantUrl $sorchaEnv.TenantUrl -WalletUrl $sorchaEnv.WalletUrl `
    -Headers $applicant.Headers -OrganizationId $publicOrgId `
    -WalletAddress $applicantWallet.Address -DisplayName 'Pinning Applicant' | Out-Null
$applicant = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl -Email $applicantEmail -Password $password -OrganizationId $publicOrgId
Write-Host "  applicant wallet $($applicantWallet.Address)"

$state = [ordered]@{
    gatewayUrl     = $sorchaEnv.GatewayUrl
    tenantUrl      = $sorchaEnv.TenantUrl
    blueprintUrl   = $sorchaEnv.BlueprintUrl
    registerUrl    = $sorchaEnv.RegisterUrl
    walletUrl      = $sorchaEnv.WalletUrl
    organizationId = $orgId
    registerId     = $registerId
    stamp          = $stamp
    officer        = @{ email = $officerEmail; password = $password; wallet = $ownerWallet }
    applicant      = @{ email = $applicantEmail; password = $password; wallet = $applicantWallet.Address }
}
$state | ConvertTo-Json -Depth 10 | Set-Content $statePath -Encoding utf8

Write-Host ""
Write-Host "PROVISIONED. state.json written." -ForegroundColor Green
Write-Host "  register $registerId"
