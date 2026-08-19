#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Strathcarron Council — Cold-start demo setup (Feature 126).
#
# Provisions the Strathcarron Council org, publishes a minimal Driving
# Licence blueprint (SorchaLocalWallet target audience so credentials
# land in the citizen's PWA), and seeds three reset-able test-citizen
# email addresses for the three Feature 126 citizen tiers:
#
#   cold-start-<random>@example.test  — Tier 3 (no account)
#   mini-gate-<random>@example.test   — Tier 2 (account, no device)
#   returning-<random>@example.test   — Tier 1 (account + paired device)
#
# Re-run with -Force to reset all three test accounts to their target tier.
#
# After setup:
#   1. Browse  http://localhost:5400/services/driving-licence
#      (the Strathcarron Council sample portal — F127 PR-A moved this page
#      out of Sorcha.UI.Web.Client into samples/strathcarron-portal/ per
#      the platform-vs-consumer boundary contract).
#   2. For Tier 3: just click "Sign in or create your account" — the
#      preflight surface fires; create the cold-start-* account and walk
#      the post-signup pairing flow.
#   3. For Tier 1 / Tier 2: this script pre-creates the accounts; the
#      gate skips the preflight and you can exercise SC-002 / SC-003.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "Strathcarron — Cold-start demo setup (Feature 126)"

$secrets = Get-SorchaSecrets -WalkthroughName "strathcarron-cold-start" -Profile $Profile
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck:$SkipHealthCheck

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"
$publicOrgId = "00000000-0000-0000-0000-000000000002"

if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtInfo "state.json exists. Use -Force to re-run."
    return
}

# ============================================================================
# Step 1: Login as System Admin
# ============================================================================
Write-WtStep "Step 1: Login as System Admin"
$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl $sorchaEnv.TenantUrl `
    -AdminEmail $secrets.adminEmail `
    -AdminPassword $secrets.adminPassword
Write-WtSuccess "Connected (org: $($sysAdmin.OrganizationId))"

# ============================================================================
# Step 2: Enable Public Org
# ============================================================================
Write-WtStep "Step 2: Enable Public Org"
Invoke-SorchaApi -Method PUT `
    -Uri "$($sorchaEnv.TenantUrl)/platform/settings/public-org" `
    -Body @{ enabled = $true } `
    -Headers $sysAdmin.Headers | Out-Null
Write-WtInfo "Public org enabled"

# ============================================================================
# Step 3: Create Strathcarron Council org
# ============================================================================
Write-WtStep "Step 3: Create Strathcarron Council org"

# The council admin is an ORG OPERATOR, so it is provisioned org-scoped — never registered as a
# public user first. Registering publicly and then naming the same address as -AdminEmail is the
# documented anti-pattern (walkthrough-builder skill): it makes the operator multi-org at best, and
# on a node where the org ALREADY EXISTS it does nothing at all — New-SorchaOrganization's duplicate
# recovery returns the existing org without adopting the admin, so the user stays Public-only. On n1,
# where the `council` walkthrough had already created Strathcarron Council, that produced a session
# scoped to the Public org and a 403 four steps later at master-key provisioning (#1427).
#
# The address is deliberately distinct from council-admin@strathcarron.local, which earlier revisions
# of this script left behind as a public account on any node they ran against; /users/provision
# requires an email that is new platform-wide.
$councilAdminEmail    = "council-issuer@strathcarron.local"
$councilAdminPassword = $secrets.DefaultPassword

$councilOrg = New-SorchaOrganization `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Strathcarron Council" `
    -Subdomain "strathcarron" `
    -AdminEmail $councilAdminEmail `
    -AdminPassword $councilAdminPassword `
    -AdminDisplayName "Strathcarron Council Issuer" `
    -AdminEmailVerified `
    -Headers $sysAdmin.Headers `
    -Description "Issues licences and other credentials to Strathcarron citizens"
$councilOrgId = $councilOrg.OrganizationId
Write-WtSuccess "Council org: $councilOrgId"

# Whether the org was created just now or recovered as an existing one, make sure the operator is
# actually IN it. The recovery path cannot create members, and a re-run finds the user already there
# — both are fine, both are handled here rather than surfacing later as an unexplained 403.
if (-not $councilOrg.AdminDirectlyAdded) {
    try {
        New-SorchaOrgUser `
            -TenantUrl $sorchaEnv.TenantUrl `
            -OrganizationId $councilOrgId `
            -Email $councilAdminEmail `
            -Password $councilAdminPassword `
            -DisplayName "Strathcarron Council Issuer" `
            -Roles @("Administrator") `
            -EmailVerified `
            -Headers $sysAdmin.Headers | Out-Null
    } catch {
        # Already provisioned by an earlier run — the password is deterministic, so the login below
        # picks the existing account up. Anything else is a real failure and must not be swallowed.
        $body = ''
        try { $body = Get-SorchaErrorBody $_ } catch { }
        if ("$body$($_.Exception.Message)" -notmatch 'already exists|duplicate|409') { throw }
        Write-WtInfo "Council operator already provisioned — reusing"
    }
}

# ============================================================================
# Step 4: Council Admin — Login, Wallet
# ============================================================================
Write-WtStep "Step 4: Council Admin — Login + Wallet"
$councilSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $councilAdminEmail `
    -Password $councilAdminPassword `
    -OrganizationId $councilOrgId

$councilWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Strathcarron Council Issuer" `
    -Headers $councilSession.Headers `
    -FetchPublicKey
Write-WtSuccess "Council wallet: $($councilWallet.Address)"

# ============================================================================
# Step 4b: Provision the Feature-083 org master key for the credential ISSUER org.
# ============================================================================
# The council org's "licensing-officer" participant issues both the
# AssuredIdentityCredential (strathcarron-driving-licence.json, action 2) and
# the BlueBadgeCredential (strathcarron-blue-badge.json, action 3 — published
# later by setup-blue-badge-demo.ps1 against this same council org/wallet).
# Both are SorchaLocalWallet issuances. Without a master key,
# IssuanceKeyService.GetActiveSigningMaterialAsync returns null and the mint
# FAILS (400 "Failed to issue credential"). Re-login first so the session JWT
# carries wallet_address (the master-key endpoint is wallet-authorized); the
# council wallet was just created above. Idempotent (409 on re-run is fine).
# One org issues both blueprints, so a single provisioning call here covers
# strathcarron-blue-badge.json as well. See the walkthrough-builder skill.

Write-WtStep "Step 4b: Provision credential-issuer org master key"

# Register the council admin as a participant FIRST — creating a wallet is not enough.
#
# wallet_address is put on a JWT by TokenService.AddWalletAddressClaimAsync, which looks up the
# PARTICIPANT record for (user, org) and takes its first active wallet LINK. A wallet with no
# participant record produces no claim, no matter how many times you log in again. Without the claim
# the F142 publish gate refuses the blueprint with
# "You do not hold a publish-governance role (Owner, Admin, or Designer) on the target register." —
# on a register this very user owns, which is what makes it so misleading. Verified on n1: a fresh
# register created by this user, with an empty wallet_address claim, was refused (#1427).
$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $councilOrgId `
    -WalletAddress $councilWallet.Address `
    -DisplayName "Strathcarron Council Issuer" `
    -Headers $councilSession.Headers

$councilIssuerSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $councilAdminEmail `
    -Password $councilAdminPassword `
    -OrganizationId $councilOrgId

Set-SorchaOrgMasterKey `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $councilOrgId `
    -Headers $councilIssuerSession.Headers

# From here on, use the RE-LOGGED session for everything wallet-authorized.
#
# $councilSession was obtained at Step 4 BEFORE the council wallet existed, and wallet_address is
# added to a JWT only at login (TokenService.AddWalletAddressClaimAsync). So that token carries no
# wallet_address for the rest of its life, and the F142 publish gate — which matches the caller's
# wallet_address against the register's governance roster — refuses it with
# "You do not hold a publish-governance role (Owner, Admin, or Designer) on the target register."
# The message reads like a ROLE problem; the admin's roles were never in question. Noted as a
# pre-existing gap in #1427 and confirmed live on n1.
$councilSession = $councilIssuerSession

# ============================================================================
# Step 5: Register + Blueprint
# ============================================================================
Write-WtStep "Step 5: Driving Licence register + blueprint"

$register = New-SorchaRegister `
    -RegisterUrl $sorchaEnv.RegisterUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Strathcarron Driving Licence Register" `
    -Description "Council-issued driving licences delivered into the citizen's Sorcha Wallet" `
    -TenantId $councilOrgId `
    -OwnerUserId $councilSession.UserId `
    -OwnerWalletAddress $councilWallet.Address `
    -Headers $councilSession.Headers `
    -TenantUrl $sorchaEnv.TenantUrl `
    -DevMode
Write-WtSuccess "Register: $($register.RegisterId)"

# Subscribe the public org so cold-start citizens can submit into this register.
try {
    $null = New-SorchaRegisterSubscription `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $publicOrgId `
        -RegisterId $register.RegisterId `
        -RegisterName "Strathcarron Driving Licence Register" `
        -SubscriptionType "Public" `
        -Headers $sysAdmin.Headers
} catch {
    Write-WtWarn "Public org subscribe to driving-licence register failed: $($_.Exception.Message)"
}

# The genesis governance roster seals asynchronously after New-SorchaRegister returns, and the
# F142 publish gate reads it. Publishing before it seals fails with a 403 whose wording blames
# the caller's publish-governance ROLE — nothing about the role is wrong. Confirmed live on n1
# 2026-08-17: this setup 403'd on a fresh register and succeeded unchanged on the re-run.
$null = Wait-SorchaRegisterRoster `
    -GatewayUrl $sorchaEnv.GatewayUrl `
    -RegisterId $register.RegisterId `
    -Headers $councilSession.Headers

# Wallet map: council "licensing-officer" participant; citizen is open
# (late-bound at submission time).
$walletMap = @{
    "licensing-officer" = $councilWallet.Address
}

$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "blueprints/strathcarron-driving-licence.json") `
    -WalletMap $walletMap `
    -Headers $councilSession.Headers `
    -IdPrefix "strathcarron-driving-licence" `
    -RegisterId $register.RegisterId
Write-WtSuccess "Blueprint: $($blueprint.BlueprintId)"

# ============================================================================
# Step 6: Provision three reset-able test citizens
# ============================================================================
Write-WtStep "Step 6: Provision Tier 1 / Tier 2 / Tier 3 test citizens"

$random = (Get-Random -Maximum 9999).ToString("0000")

# Tier 3 — cold-start. NOT registered. The operator drives the F116 signup
# flow through the gate's PreflightSignupSurface during the demo walk.
$tier3Email = "cold-start-$random@example.test"

# Tier 2 — mini-gate. Registered + verified, but no wallet device. The
# WalletPairingSurface fires with TierMode.MiniGate when this citizen
# arrives signed in.
$tier2Email = "mini-gate-$random@example.test"
$tier2Password = $secrets.DefaultPassword
Register-SorchaPublicUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $tier2Email `
    -Password $tier2Password `
    -DisplayName "Mini-Gate Test Citizen" | Out-Null

# Tier 1 — fast-path. Registered, verified, AND has a paired device. The
# gate skips the wallet-pairing surface entirely and drops the citizen
# straight into the form.
$tier1Email = "returning-$random@example.test"
$tier1Password = $secrets.DefaultPassword
Register-SorchaPublicUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $tier1Email `
    -Password $tier1Password `
    -DisplayName "Returning Test Citizen" | Out-Null

# Verify both emails (admin override — no SMTP in the dev stack).
$publicUsers = Invoke-SorchaApi -Method GET `
    -Uri "$($sorchaEnv.TenantUrl)/organizations/$publicOrgId/users?includeInactive=true" `
    -Headers $sysAdmin.Headers
foreach ($email in @($tier1Email, $tier2Email)) {
    $user = $publicUsers.users | Where-Object { $_.email -eq $email } | Select-Object -First 1
    if ($user) {
        Confirm-SorchaUserEmail `
            -TenantUrl $sorchaEnv.TenantUrl `
            -OrganizationId $publicOrgId `
            -UserId $user.id `
            -Headers $sysAdmin.Headers
        Write-WtInfo "  verified: $email"
    }
}

# Tier 1: pre-run the F114 device-pairing ceremony so the account starts
# with one active device. The PWA-side ceremony needs IJSRuntime which
# we don't have here — so we drive the device-pairing endpoint directly
# with a generated key thumbprint. This is the same shape the PWA's
# IEnrolmentService.EnrolAsync produces.
Write-WtInfo "Tier 1 device-pairing: see TODO below"
# TODO (operator step until automation lands): sign $tier1Email into
# http://localhost/wallet/ once, then walk Settings → Enrol this device.
# The next run of this script (with -Force) preserves the device.

# ============================================================================
# Step 7: Save state
# ============================================================================
$state = @{
    profile                = $Profile
    gatewayUrl             = $sorchaEnv.GatewayUrl
    tenantUrl              = $sorchaEnv.TenantUrl
    walletUrl              = $sorchaEnv.WalletUrl
    registerUrl            = $sorchaEnv.RegisterUrl
    blueprintUrl           = $sorchaEnv.BlueprintUrl
    publicOrgId            = $publicOrgId
    councilOrgId           = $councilOrgId
    # Recorded so setup-blue-badge-demo.ps1 signs in as whoever THIS script provisioned, instead of
    # hardcoding an address the two files have to keep in step by hand.
    councilAdminEmail      = $councilAdminEmail
    councilWalletAddress   = $councilWallet.Address
    registerId             = $register.RegisterId
    blueprintId            = $blueprint.BlueprintId
    councilPage            = $env:STRATHCARRON_PORTAL_URL ?? "http://localhost:5400/services/driving-licence"
    citizens = @{
        coldStart = @{
            tier  = 3
            email = $tier3Email
            note  = "Not yet registered — operator drives the F116 signup flow through the gate's PreflightSignupSurface."
        }
        miniGate = @{
            tier     = 2
            email    = $tier2Email
            password = $tier2Password
            note     = "Registered + verified, no wallet device. WalletPairingSurface fires with TierMode.MiniGate."
        }
        fastPath = @{
            tier         = 1
            email        = $tier1Email
            password     = $tier1Password
            note         = "Registered + verified. Operator runs the PWA device-pairing once to complete Tier 1 seeding."
            devicePaired = $false
        }
    }
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State saved to $stateFile"

Write-Host ""
Write-WtInfo "Demo URLs:"
Write-WtInfo "  Council page: $($state.councilPage) (Strathcarron sample portal — separate container)"
Write-WtInfo "  Wallet PWA:   $($sorchaEnv.GatewayUrl)/wallet/"
Write-Host ""
Write-WtInfo "Citizens:"
Write-WtInfo "  Tier 3 (cold-start): $tier3Email (sign up through the gate)"
Write-WtInfo "  Tier 2 (mini-gate):  $tier2Email / $tier2Password"
Write-WtInfo "  Tier 1 (fast-path):  $tier1Email / $tier1Password (pair a device first)"
Write-Host ""
Write-WtInfo "Walks (see specs/126-enrol-inside-wizard/quickstart.md):"
Write-WtInfo "  Walk 1 — Tier 3 cold-start: SC-001, SC-007"
Write-WtInfo "  Walk 2 — Tier 1 fast-path:  SC-002"
Write-WtInfo "  Walk 3 — Tier 2 mini-gate:  SC-003"
Write-WtInfo "  Variation — stranger scans QR: SC-008"
