# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
<#
.SYNOPSIS
    Provisions the org, wallets, participants, register and blueprint for the encryption-at-rest
    conformance check (#1580) and the DevMode -> FLE promotion check (#1579).

.DESCRIPTION
    Deliberately minimal, but two details are load-bearing and neither is optional:

    * THE REGISTER IS CREATED IN DEVMODE AND IS CONSUMED BY ONE RUN. Promotion is one-way, so a
      re-run cannot reuse it: the DevMode half would find an already-promoted register and fail to
      see plaintext, which is indistinguishable from a broken probe. The register name therefore
      carries a per-run stamp, defeating New-SorchaRegister's reuse-by-name.

    * BOTH PARTICIPANTS ARE PUBLISHED ON THE REGISTER, AND THAT IS ASSERTED. If no recipient key
      resolves, ActionExecutionService takes the "legacy plaintext path" and writes an UNENCRYPTED
      payload to a Normal register — with only a "recipient skipped" warning. The Normal half of
      this check would then be measuring a fail-open rather than encryption, and would report the
      absence of a sentinel that was in fact written in the clear a moment later.

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
    Write-Host "NOTE: this walkthrough CONSUMES its register (promotion is one-way), so a re-run" -ForegroundColor Yellow
    Write-Host "      always provisions a fresh one." -ForegroundColor Yellow
    exit 0
}
if (Test-Path $statePath) { Remove-Item $statePath -Force }

$sorchaEnv = if ($GatewayUrl) {
    Initialize-SorchaEnvironment -GatewayUrl $GatewayUrl
} else {
    Initialize-SorchaEnvironment -Profile $Profile
}

Write-Host ""
Write-Host "=== Encryption at rest (#1580 / #1579): provisioning ===" -ForegroundColor Cyan
Write-Host "Gateway: $($sorchaEnv.GatewayUrl)"

$stamp = Get-Date -Format 'yyyyMMddHHmmss'
$sub = "encrest-$stamp"
$password = 'EncRest_Test_2026!'
$officerEmail = "officer@$sub.test"
$applicantEmail = "applicant@$sub.test"

$platform = Get-SorchaSecrets -WalkthroughName 'platform'
$admin = Connect-SorchaAdmin -TenantUrl $sorchaEnv.TenantUrl `
    -AdminEmail $platform.adminEmail -AdminPassword $platform.adminPassword

Write-Host "`n[1/7] Organisation + officer" -ForegroundColor Cyan
# The org's own admin creates the org wallet (#1525) — -WalletUrl does it while a session exists.
$org = New-SorchaOrganization `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Headers $admin.Headers `
    -Name "Encryption At Rest $stamp" `
    -Subdomain $sub `
    -AdminEmail $officerEmail `
    -AdminPassword $password `
    -AdminDisplayName 'Encryption Officer' `
    -AdminEmailVerified

$orgId = $org.OrganizationId
Write-Host "  org $orgId"

$officer = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl -Email $officerEmail -Password $password -OrganizationId $orgId

Write-Host "`n[2/7] Officer wallet" -ForegroundColor Cyan
# -FetchPublicKey is required: without it PublicKey comes back empty and the participant publish
# below fails on parameter binding rather than on anything meaningful.
$officerWallet = New-SorchaWallet -WalletUrl $sorchaEnv.WalletUrl -Headers $officer.Headers `
    -Algorithm 'ED25519' -Name "encrest-officer-$stamp" -FetchPublicKey
Register-SorchaParticipant -TenantUrl $sorchaEnv.TenantUrl -WalletUrl $sorchaEnv.WalletUrl `
    -Headers $officer.Headers -OrganizationId $orgId `
    -WalletAddress $officerWallet.Address -DisplayName 'Encryption Officer' | Out-Null

# RE-LOGIN. wallet_address is added to the JWT only at login, from the first active linked wallet.
# Without it the F142 publish HARD gate refuses with "you do not hold a publish-governance role",
# which reads as a permissions problem and is a stale token.
$officer = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl -Email $officerEmail -Password $password -OrganizationId $orgId
$ownerWallet = (Decode-SorchaJwt $officer.Token).wallet_address
if (-not $ownerWallet) { throw "Officer token carries no wallet_address claim after re-login." }
Write-Host "  officer wallet $ownerWallet"

Write-Host "`n[3/7] Applicant (org-scoped, so the OAuth password grant works)" -ForegroundColor Cyan
# Deliberately org-scoped rather than a public user: a public user added to an org becomes
# multi-org, and the password grant has no org-selection step, so it 401s. Nothing here needs a
# citizen identity — the applicant only has to hold a wallet and be published on the register.
$applicantUser = New-SorchaOrgUser -TenantUrl $sorchaEnv.TenantUrl -Headers $admin.Headers `
    -OrganizationId $orgId -Email $applicantEmail -Password $password `
    -DisplayName 'Encryption Applicant' -EmailVerified
if (-not $applicantUser.UserId) { throw "Applicant provisioning returned no user id." }

$applicant = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl -Email $applicantEmail -Password $password -OrganizationId $orgId
$applicantWallet = New-SorchaWallet -WalletUrl $sorchaEnv.WalletUrl -Headers $applicant.Headers `
    -Algorithm 'ED25519' -Name "encrest-applicant-$stamp" -FetchPublicKey
Register-SorchaParticipant -TenantUrl $sorchaEnv.TenantUrl -WalletUrl $sorchaEnv.WalletUrl `
    -Headers $applicant.Headers -OrganizationId $orgId `
    -WalletAddress $applicantWallet.Address -DisplayName 'Encryption Applicant' | Out-Null
$applicant = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl -Email $applicantEmail -Password $password -OrganizationId $orgId

# Deliberately NOT read from the JWT. The applicant holds the Consumer role, so its token is
# consumer-tier — and a consumer token omits the wallet binding by design (CLAUDE.md #13). Only
# the officer needs wallet_address on its token, because only the officer publishes a blueprint
# (the F142 publish gate matches that claim). The applicant merely submits and receives
# disclosures, both of which resolve its wallet server-side.
$applicantAddress = $applicantWallet.Address
if (-not $applicantAddress) { throw "Applicant wallet creation returned no address." }
Write-Host "  applicant wallet $applicantAddress (consumer tier — no wallet_address claim, as designed)"

Write-Host "`n[4/7] Register (DevMode — this run PROMOTES it, so it is single-use)" -ForegroundColor Cyan
# Name must be <= 38 chars. "EncAtRest " + 14-char stamp = 24.
$registerName = "EncAtRest $stamp"
$register = New-SorchaRegister `
    -RegisterUrl $sorchaEnv.RegisterUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name $registerName `
    -Description 'Encryption-at-rest conformance register (#1580) — promoted mid-run' `
    -TenantId $orgId `
    -OwnerUserId $officer.UserId `
    -OwnerWalletAddress $ownerWallet `
    -Headers $officer.Headers `
    -DevMode
$registerId = $register.RegisterId
Write-Host "  register $registerId"

if ($register.PSObject.Properties['Reused'] -and $register.Reused) {
    throw ("Register '$registerName' already existed and was REUSED. This check promotes its " +
           "register one-way, so a reused register may already be in Normal mode — the DevMode " +
           "half would then silently measure nothing. Provision a fresh one.")
}

Write-Host "`n[5/7] Waiting for the genesis roster to seal" -ForegroundColor Cyan
# The genesis control tx recording the owner roster seals ASYNC. Publishing before it lands reads
# an EMPTY roster and fail-closes with the same 403 as a missing role.
$null = Wait-SorchaRegisterRoster -GatewayUrl $sorchaEnv.GatewayUrl -RegisterId $registerId -Headers $officer.Headers
Write-Host "  roster sealed"

Write-Host "`n[6/7] Publishing BOTH participants onto the register" -ForegroundColor Cyan
# Both publishes use the OFFICER's session. Publishing a participant onto a register is an
# org-admin operation (platform tier); the applicant's Consumer-role token is consumer-tier and is
# refused with a 403 that reads as a permissions bug and is really a tier boundary (CLAUDE.md #13).
# An org admin may publish any participant record belonging to their own org, which is what this is.
foreach ($p in @(
    @{ Name = 'Encryption Officer';   Address = $ownerWallet;      PublicKey = $officerWallet.PublicKey },
    @{ Name = 'Encryption Applicant'; Address = $applicantAddress; PublicKey = $applicantWallet.PublicKey }
)) {
    $pub = Publish-SorchaParticipant `
        -TenantUrl $sorchaEnv.TenantUrl -OrganizationId $orgId -RegisterId $registerId `
        -ParticipantName $p.Name -OrganizationName "Encryption At Rest $stamp" `
        -WalletAddress $p.Address -PublicKey $p.PublicKey -Headers $officer.Headers

    if ($pub -and $pub.PSObject.Properties['transactionId'] -and $pub.transactionId) {
        Wait-SorchaActorReady -Mode ParticipantSealed -TxId $pub.transactionId `
            -RegisterId $registerId -Headers $officer.Headers -GatewayUrl $sorchaEnv.GatewayUrl | Out-Null
    }
    Write-Host "  published $($p.Name)"
}

# PROVE the keys resolve. This is the precondition the whole Normal half rests on: with no
# resolvable recipient, ActionExecutionService falls through to the plaintext builder and writes
# an unencrypted payload to an encrypted register. Asserting it HERE turns that into a named
# failure at the point of cause instead of a mystery absence four steps later.
$resolved = Invoke-SorchaApi -Method POST `
    -Uri "$($sorchaEnv.RegisterUrl)/registers/$registerId/participants/resolve-public-keys" `
    -Body @{ walletAddresses = @($ownerWallet, $applicantAddress) } -Headers $officer.Headers
if (@($resolved.notFound).Count -gt 0) {
    throw ("Participant keys are NOT on the register: $(@($resolved.notFound) -join ', '). " +
           "Without them every recipient is skipped, no disclosure-group envelope is built, and " +
           "the encrypted register silently stores PLAINTEXT — so the conformance check would be " +
           "measuring a fail-open rather than encryption.")
}
Write-Host "  resolve-public-keys: both keys present on the register"

Write-Host "`n[7/7] Publishing the blueprint" -ForegroundColor Cyan
# The applicant is the starting-action sender, so it is late-bound and Publish-SorchaBlueprint
# skips it from the wallet map regardless. Its key still resolves as a DISCLOSURE RECIPIENT
# because it is published on the register above — those are different mechanisms.
$walletMap = @{ 'officer' = $ownerWallet }
$publish = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $PSScriptRoot 'blueprints' 'encryption-at-rest.json') `
    -WalletMap $walletMap `
    -Headers $officer.Headers `
    -IdPrefix "encrest-$stamp" `
    -RegisterId $registerId

$blueprintId = $publish.BlueprintId
if (-not $blueprintId) { throw "Blueprint publish returned no blueprint id." }
Write-Host "  blueprint $blueprintId"

$state = [ordered]@{
    gatewayUrl     = $sorchaEnv.GatewayUrl
    tenantUrl      = $sorchaEnv.TenantUrl
    blueprintUrl   = $sorchaEnv.BlueprintUrl
    registerUrl    = $sorchaEnv.RegisterUrl
    walletUrl      = $sorchaEnv.WalletUrl
    organizationId = $orgId
    registerId     = $registerId
    registerName   = $registerName
    blueprintId    = $blueprintId
    stamp          = $stamp
    officer        = @{ email = $officerEmail; password = $password; wallet = $ownerWallet; organizationId = $orgId }
    applicant      = @{ email = $applicantEmail; password = $password; wallet = $applicantAddress; organizationId = $orgId }
}
$state | ConvertTo-Json -Depth 10 | Set-Content $statePath -Encoding utf8

Write-Host ""
Write-Host "PROVISIONED. state.json written." -ForegroundColor Green
Write-Host "  register $registerId (DevMode — run-conformance.ps1 promotes it, one way)"
