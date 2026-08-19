#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AssuredIdentity — Setup
# Feature 107 + Feature 124. Creates the Acme Verification Co. issuing
# organisation, provisions its trust anchor + HAIP issuer enrolment,
# creates the citizen public-org account, and publishes the AssuredIdentity
# blueprint (now configured to deliver into the Sorcha Wallet (PWA) via
# SorchaLocalWallet target audience).
#
# After setup, sign the citizen into the wallet host once on the demo
# device: open http://localhost/wallet/, tap Settings, sign in with the
# citizen credentials printed at the end of this script.
#
# Idempotent — safe to run multiple times.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    # Deploy-anywhere override — target any node by gateway URL (e.g. http://tiny:8090) without
    # adding a profile. Passed through to Initialize-SorchaEnvironment; ignored when empty.
    [string]$GatewayUrl,
    [switch]$SkipHealthCheck,
    [switch]$Force,
    # DevMode register (plaintext payloads, disclosure-filtered) — the default for the
    # citizen-late-bound credential demo. Pass -DevMode:$false for a Normal (encrypted)
    # register to exercise the production field-encryption + cross-node decrypt path.
    [bool]$DevMode = $true
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "AssuredIdentity — Setup"

$secrets = Get-SorchaSecrets -WalkthroughName "assured-identity" -Profile $Profile
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -GatewayUrl $GatewayUrl -SkipHealthCheck:$SkipHealthCheck

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"

# Well-known public org id (platform-wide, used for citizen accounts).
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
# Step 2: Enable Public Org + Register Users
# ============================================================================
Write-WtStep "Step 2: Enable Public Org and Register Users"

Invoke-SorchaApi -Method PUT `
    -Uri "$($sorchaEnv.TenantUrl)/platform/settings/public-org" `
    -Body @{ enabled = $true } `
    -Headers $sysAdmin.Headers
Write-WtInfo "Public org enabled"

$verificationAdminEmail    = "verification-admin@assured-identity.local"
$verificationAdminPassword = $secrets.DefaultPassword

Register-SorchaPublicUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $verificationAdminEmail `
    -Password $verificationAdminPassword `
    -DisplayName "Verification Admin" | Out-Null
Write-WtInfo "verification-admin registered: $verificationAdminEmail"

$citizenEmail    = "alex.macleod@assured-identity.local"
$citizenPassword = $secrets.DefaultPassword

Register-SorchaPublicUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $citizenEmail `
    -Password $citizenPassword `
    -DisplayName "Alex MacLeod" | Out-Null
Write-WtInfo "citizen registered: $citizenEmail"

# ============================================================================
# Step 3: Verify Emails (admin override — no SMTP)
# ============================================================================
Write-WtStep "Step 3: Verify User Emails"

$publicUsers = Invoke-SorchaApi -Method GET `
    -Uri "$($sorchaEnv.TenantUrl)/organizations/$publicOrgId/users?includeInactive=true" `
    -Headers $sysAdmin.Headers

foreach ($email in @($verificationAdminEmail, $citizenEmail)) {
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

# ============================================================================
# Step 4: Create Acme Verification Co. Org
# ============================================================================
Write-WtStep "Step 4: Create Acme Verification Co."

$verificationOrg = New-SorchaOrganization `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Acme Verification Co." `
    -Subdomain "acme-verification" `
    -AdminEmail $verificationAdminEmail `
    -AdminPassword $verificationAdminPassword `
    -Headers $sysAdmin.Headers `
    -Description "Issues Assured Identity credentials to citizens"

$verificationOrgId = $verificationOrg.OrganizationId
Write-WtSuccess "Verification org: $verificationOrgId"

# ============================================================================
# Step 5: Verification Admin (Tier 2) — Login + issuer wallet
# ============================================================================
# Three-tier identity model:
#   Tier 1: admin@sorcha.local           — system admin (creates orgs)
#   Tier 2: verification-admin@...local  — org admin (issuer wallet, register, blueprint)
#   Tier 3: verification-analyst@...local — consumer (executes Action 2 ONLY)
#
# Step 5 sets up Tier 2. Tier 3 is provisioned in Step 5c.
Write-WtStep "Step 5: Verification Admin (Tier 2) — Login + Issuer Wallet"

$verificationAdminSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $verificationAdminEmail `
    -Password $verificationAdminPassword `
    -OrganizationId $verificationOrgId

# Issuer wallet — owned by the org-admin (the org's signing identity for register
# creation + blueprint publishing). NOT used for action submission.
$verificationWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Acme Verification Co. Issuer" `
    -Headers $verificationAdminSession.Headers `
    -FetchPublicKey
Write-WtSuccess "Issuer wallet (Tier 2, org admin): $($verificationWallet.Address)"

# Register verification-admin as a participant in her own org and link the issuer
# wallet. This makes TokenService.AddWalletAddressClaimAsync find a (participant,
# wallet) pair and emit `wallet_address` in her JWT — which F142 PublishGate
# substring-matches against the register's roster Owner subject DID
# (`did:sorcha:w:{walletAddress}`) to authorise blueprint publish (FR-027).
# Without this link the token has no wallet_address claim and the publish 403s.
$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $verificationOrgId `
    -WalletAddress $verificationWallet.Address `
    -DisplayName "Verification Admin" `
    -Headers $verificationAdminSession.Headers
Write-WtInfo "Verification admin participant registered (issuer wallet linked)"

# Re-login so the refreshed access token carries the new wallet_address claim.
$verificationAdminSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $verificationAdminEmail `
    -Password $verificationAdminPassword `
    -OrganizationId $verificationOrgId
Write-WtInfo "Verification admin session refreshed (wallet_address claim now present)"

# ============================================================================
# Step 5b: Citizen — Login, Wallet, Participant (best-effort, late-bound)
# ============================================================================
# Spec 136: a public-org citizen now receives a CONSUMER-tier token (no org_id),
# so the org-style wallet API (CanManageWallets requires org_id) correctly refuses
# it (403). The citizen is an OPEN, late-bound participant (NOT in the wallet map at
# publish, Step 8), so a pre-provisioned scripted-citizen wallet is not needed to
# stand the service up — a real citizen provisions their wallet via the PWA enrol
# flow. This block is therefore best-effort: warn and continue on failure.
Write-WtStep "Step 5b: Citizen — Login, Wallet, Participant (best-effort)"

$citizenWallet = $null
try {
    $citizenSession = Connect-SorchaUser `
        -TenantUrl $sorchaEnv.TenantUrl `
        -Email $citizenEmail `
        -Password $citizenPassword `
        -OrganizationId $publicOrgId

    $citizenWallet = New-SorchaWallet `
        -WalletUrl $sorchaEnv.WalletUrl `
        -Name "Citizen Application Wallet" `
        -Headers $citizenSession.Headers `
        -FetchPublicKey
    Write-WtSuccess "Citizen wallet: $($citizenWallet.Address)"

    $null = Register-SorchaParticipant `
        -TenantUrl $sorchaEnv.TenantUrl `
        -WalletUrl $sorchaEnv.WalletUrl `
        -OrganizationId $publicOrgId `
        -WalletAddress $citizenWallet.Address `
        -DisplayName "Alex MacLeod" `
        -Headers $citizenSession.Headers
    Write-WtInfo "Citizen participant registered in public org"
} catch {
    Write-WtWarn "Scripted-citizen wallet provisioning skipped (spec 136: consumer token has no org_id; citizen is late-bound and provisions via the PWA enrol flow): $($_.Exception.Message)"
}

# ============================================================================
# Step 5c: Verification Analyst (Tier 3) — provision, login, wallet, participant
# ============================================================================
# Three-tier identity model — Tier 3 = consumer user, EXECUTES actions only.
# The analyst is org-scoped (Consumer role inside Acme Verification Co.) and is
# the sender of Action 2 (verifies the citizen's application). Their wallet —
# not the org-admin's — is what gets baked into the blueprint's walletMap for
# the 'verification-analyst' participant.
Write-WtStep "Step 5c: Verification Analyst (Tier 3) — Provision + Wallet + Participant"

$verificationAnalystEmail    = "verification-analyst@assured-identity.local"
$verificationAnalystPassword = $secrets.DefaultPassword

# Tier 3 user provisioned single-org-direct so they avoid the multi-org
# OAuth-grant flow. Consumer role = no authoring rights; only action execution.
$verificationAnalystUser = New-SorchaOrgUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -OrganizationId $verificationOrgId `
    -Email $verificationAnalystEmail `
    -Password $verificationAnalystPassword `
    -DisplayName "Verification Analyst" `
    -Headers $sysAdmin.Headers `
    -Roles @("Consumer") `
    -EmailVerified

$verificationAnalystSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $verificationAnalystEmail `
    -Password $verificationAnalystPassword `
    -OrganizationId $verificationOrgId

# Analyst's own wallet — signs Action 2. The org's issuer wallet stays
# separate (used by the Tier-2 admin path only).
$verificationAnalystWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Verification Analyst Wallet" `
    -Headers $verificationAnalystSession.Headers `
    -FetchPublicKey
Write-WtSuccess "Analyst wallet (Tier 3, executes Action 2): $($verificationAnalystWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $verificationOrgId `
    -WalletAddress $verificationAnalystWallet.Address `
    -DisplayName "Verification Analyst" `
    -Headers $verificationAnalystSession.Headers
Write-WtInfo "Verification analyst participant registered (Tier 3 wallet)"

# ============================================================================
# Step 6: Provision Trust Anchor + Enrol as HAIP Issuer
# ============================================================================
Write-WtStep "Step 6: Trust Anchor + HAIP Issuer Enrolment"

$tenantId = $sysAdmin.OrganizationId

try {
    Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$tenantId/provision" `
        -Headers $sysAdmin.Headers `
        -Body @{}
    Write-WtSuccess "Trust anchor provisioned"
} catch { Write-WtWarn "Trust anchor may already exist" }

try {
    Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$tenantId/orgs/$($verificationWallet.Address)/enrol" `
        -Headers $sysAdmin.Headers `
        -Body @{
            orgPublicKeyBase64 = $verificationWallet.PublicKey
            orgDisplayName     = "Acme Verification Co."
        }
    Write-WtSuccess "Org cert enrolled"
} catch { Write-WtWarn "Enrolment may already exist" }

# ============================================================================
# Step 7: Create Register
# ============================================================================
Write-WtStep "Step 7: Create Register"

# DevMode (default) — citizen is late-bound and never published to the register, so
# Action 2's credential delivery uses the plaintext path. Same security posture as
# HaipVerifiedCitizen (the source this walkthrough replaces). Pass -DevMode:$false for
# a Normal (encrypted) register that exercises the production field-encryption + the
# cross-node disclosure-group decrypt path.
Write-WtInfo "Register CryptoPolicy: $(if ($DevMode) { 'DevMode (plaintext)' } else { 'Normal (encrypted)' })"
# Mode-suffix the name so DevMode and Normal registers are distinct (New-SorchaRegister
# reuses by name) — lets the Normal (encrypted) path be tested without disturbing the DevMode one.
$registerName = if ($DevMode) { "Assured Identity Register" } else { "Assured Identity Register (Normal)" }
$register = New-SorchaRegister `
    -RegisterUrl $sorchaEnv.RegisterUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name $registerName `
    -Description "Register for Feature 107 Assured Identity workflows" `
    -TenantId $verificationOrgId `
    -OwnerUserId $verificationAdminSession.UserId `
    -OwnerWalletAddress $verificationWallet.Address `
    -Headers $verificationAdminSession.Headers `
    -TenantUrl $sorchaEnv.TenantUrl `
    -DevMode:$DevMode
Write-WtSuccess "Register: $($register.RegisterId)"

try {
    # Publish the ANALYST (Tier 3) as the on-register participant — the analyst
    # is the action sender, NOT the org-admin. Org-admin still authorises the
    # publish via their session headers (publishing is a Tier-2 authoring act).
    $null = Publish-SorchaParticipant `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $verificationOrgId `
        -RegisterId $register.RegisterId `
        -ParticipantName "Verification Analyst" `
        -OrganizationName "Acme Verification Co." `
        -WalletAddress $verificationAnalystWallet.Address `
        -PublicKey $verificationAnalystWallet.PublicKey `
        -Headers $verificationAdminSession.Headers
} catch {
    Write-WtWarn "Verification analyst participant publish failed: $($_.Exception.Message)"
}

try {
    $null = New-SorchaRegisterSubscription `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $publicOrgId `
        -RegisterId $register.RegisterId `
        -RegisterName "Assured Identity Register" `
        -SubscriptionType "Public" `
        -Headers $sysAdmin.Headers
} catch {
    Write-WtWarn "Public org subscribe to identity register failed: $($_.Exception.Message)"
}

# ============================================================================
# Step 8: Publish Blueprint
# ============================================================================
Write-WtStep "Step 8: Publish Blueprint"

# Open-participant contract: the 'citizen' participant is the sender of an
# isStartingAction: true action and therefore MUST NOT appear in the wallet
# map. Runtime late-binds whichever authenticated public-org user submits
# Action 1 as the bound citizen for that instance.
# See: .claude/skills/blueprint-builder/SKILL.md — "Open Participants & Late Binding"
$walletMap = @{
    # Tier 3 analyst's wallet — the action SENDER. NOT the org-admin's issuer
    # wallet (that one stays purely for org-level authoring acts).
    "verification-analyst" = $verificationAnalystWallet.Address
    # "citizen" is intentionally absent — late-bound at runtime.
}

$blueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "blueprints/assured-identity.json") `
    -WalletMap $walletMap `
    -Headers $verificationAdminSession.Headers `
    -IdPrefix "assured-identity" `
    -RegisterId $register.RegisterId

Write-WtSuccess "Blueprint: $($blueprint.BlueprintId)"

# ============================================================================
# Step 9: Acme Licensing Co. — Feature 107 PR 2 (US2)
#
# Phase 2 (driving licence) is currently a stub — Feature 124 deferred the
# credential-gated second-service flow to Spec 4 of the citizen arc. Steps 9
# and 10 also predate F142's PublishGate, which requires the publisher's
# wallet to hold an Owner/Admin/Designer role on the target register —
# licensing-admin's wallet is only a subscribed participant on the assured-
# identity register, so the publish (Step 10) cannot satisfy the governance
# hard gate today. Wrap both steps in a best-effort guard so Step 11+ and
# Phase 1 still run; a real Phase 2 will need either a separate licensing
# register (with licensing-admin as Owner) or an Admin/Designer attestation
# on the assured-identity register at register-creation time.
# ============================================================================
$licensingWallet = $null
$licenceBlueprint = $null
$licensingOrgId = $null
try {
Write-WtStep "Step 9: Create Acme Licensing Co."

$licensingAdminEmail    = "licensing-admin@assured-identity.local"
$licensingAdminPassword = $secrets.DefaultPassword

Register-SorchaPublicUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $licensingAdminEmail `
    -Password $licensingAdminPassword `
    -DisplayName "Licensing Admin" | Out-Null

$publicUsersAfterLicensing = Invoke-SorchaApi -Method GET `
    -Uri "$($sorchaEnv.TenantUrl)/organizations/$publicOrgId/users?includeInactive=true" `
    -Headers $sysAdmin.Headers
$licensingPublicUser = $publicUsersAfterLicensing.users | Where-Object { $_.email -eq $licensingAdminEmail } | Select-Object -First 1
if ($licensingPublicUser) {
    Confirm-SorchaUserEmail `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $publicOrgId `
        -UserId $licensingPublicUser.id `
        -Headers $sysAdmin.Headers
}

$licensingOrg = New-SorchaOrganization `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Acme Licensing Co." `
    -Subdomain "acme-licensing" `
    -AdminEmail $licensingAdminEmail `
    -AdminPassword $licensingAdminPassword `
    -Headers $sysAdmin.Headers `
    -Description "Issues Driving Licence credentials to citizens holding an AssuredIdentityCredential"
$licensingOrgId = $licensingOrg.OrganizationId
Write-WtSuccess "Licensing org: $licensingOrgId"

$licensingSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl `
    -Email $licensingAdminEmail `
    -Password $licensingAdminPassword `
    -OrganizationId $licensingOrgId

$licensingWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl `
    -Name "Acme Licensing Co. Issuer" `
    -Headers $licensingSession.Headers `
    -FetchPublicKey
Write-WtSuccess "Licensing wallet: $($licensingWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl `
    -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $licensingOrgId `
    -WalletAddress $licensingWallet.Address `
    -DisplayName "Licensing Officer" `
    -Headers $licensingSession.Headers

# Licensing org as HAIP issuer too — trust anchor already provisioned for the tenant,
# enrol the licensing org cert alongside Acme Verification Co.
try {
    Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$tenantId/orgs/$($licensingWallet.Address)/enrol" `
        -Headers $sysAdmin.Headers `
        -Body @{
            orgPublicKeyBase64 = $licensingWallet.PublicKey
            orgDisplayName     = "Acme Licensing Co."
        }
    Write-WtSuccess "Licensing org cert enrolled"
} catch { Write-WtWarn "Licensing enrolment may already exist" }

# Reuse the Assured Identity register — Feature 107 design keeps both
# credentials on a single canonical register for the walkthrough so the
# citizen's wallet stays coherent.

try {
    $null = Publish-SorchaParticipant `
        -TenantUrl $sorchaEnv.TenantUrl `
        -OrganizationId $licensingOrgId `
        -RegisterId $register.RegisterId `
        -ParticipantName "Licensing Officer" `
        -OrganizationName "Acme Licensing Co." `
        -WalletAddress $licensingWallet.Address `
        -PublicKey $licensingWallet.PublicKey `
        -Headers $licensingSession.Headers
} catch {
    Write-WtWarn "Licensing participant publish failed: $($_.Exception.Message)"
}

# ============================================================================
# Step 10: Publish Driving Licence Blueprint
# ============================================================================
Write-WtStep "Step 10: Publish Driving Licence Blueprint"

$licenceWalletMap = @{
    "licensing-officer" = $licensingWallet.Address
    # "citizen" intentionally absent — late-bound per Feature 103 open-participant contract.
}

$licenceBlueprint = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "blueprints/driving-licence.json") `
    -WalletMap $licenceWalletMap `
    -Headers $licensingSession.Headers `
    -IdPrefix "driving-licence" `
    -RegisterId $register.RegisterId

Write-WtSuccess "Driving Licence blueprint: $($licenceBlueprint.BlueprintId)"
} catch {
    Write-WtWarn "Phase 2 setup (Steps 9-10) skipped (F142 publish-gate blocks licensing-admin — no Owner role on assured-identity register): $($_.Exception.Message)"
}

# ============================================================================
# Save State
# ============================================================================
$state = @{
    tenantUrl                 = $sorchaEnv.TenantUrl
    blueprintUrl              = $sorchaEnv.BlueprintUrl
    walletUrl                 = $sorchaEnv.WalletUrl
    registerUrl               = $sorchaEnv.RegisterUrl
    gatewayUrl                = $sorchaEnv.GatewayUrl
    tenantId                  = $tenantId
    verificationOrgId         = $verificationOrgId
    # The Tier-2 org-admin's issuer wallet — used by org-level authoring acts
    # (register creation, blueprint publish). NOT a runtime action sender.
    verificationWalletAddress = $verificationWallet.Address
    verificationWalletPublicKey = $verificationWallet.PublicKey
    # The Tier-3 analyst's wallet — signs Action 2. This is the wallet bound
    # to the verification-analyst participant on the published blueprint.
    verificationAnalystWalletAddress = $verificationAnalystWallet.Address
    verificationAnalystWalletPublicKey = $verificationAnalystWallet.PublicKey
    publicOrgId               = $publicOrgId
    citizenWalletAddress      = $citizenWallet.Address
    registerId                = $register.RegisterId
    blueprintId               = $blueprint.BlueprintId
    # walletDir removed in Feature 124 — credential delivery now lands in the
    # Sorcha Wallet (PWA) (SorchaLocalWallet target audience), not in a
    # filesystem wallet on the operator host.
    # PR 2 additions — licensing org, licensing wallet, Driving Licence blueprint id.
    # Phase 2 is best-effort (see Step 9 comment); these may be null when the
    # F142 publish gate blocks licensing-admin on the assured-identity register.
    licensingOrgId            = $licensingOrgId
    licensingWalletAddress    = if ($licensingWallet) { $licensingWallet.Address } else { $null }
    licensingWalletPublicKey  = if ($licensingWallet) { $licensingWallet.PublicKey } else { $null }
    licenceBlueprintId        = if ($licenceBlueprint) { $licenceBlueprint.BlueprintId } else { $null }
    roles = @{
        # Tier 2: org admin — authoring only. The Phase-1 citizen flow MUST NOT
        # use this identity for action submission.
        verificationAdmin = @{
            email          = $verificationAdminEmail
            password       = $verificationAdminPassword
            organizationId = $verificationOrgId
            walletAddress  = $verificationWallet.Address
        }
        # Tier 3: consumer (Consumer role inside Acme Verification Co.) — the
        # sender of Action 2 (verifies the citizen's application). This is the
        # identity Phase-1 step 5 logs in as.
        verificationAnalyst = @{
            email          = $verificationAnalystEmail
            password       = $verificationAnalystPassword
            organizationId = $verificationOrgId
            walletAddress  = $verificationAnalystWallet.Address
        }
        citizen = @{
            email          = $citizenEmail
            password       = $citizenPassword
            organizationId = $publicOrgId
            walletAddress  = $citizenWallet.Address
        }
        licensingOfficer = @{
            email          = $licensingAdminEmail
            password       = $licensingAdminPassword
            organizationId = $licensingOrgId
            walletAddress  = $licensingWallet.Address
        }
    }
    persona = @{
        givenName  = "Alex"
        middleName = "Morgan"
        familyName = "MacLeod"
        fullName   = "Alex Morgan MacLeod"
        dateOfBirth = "1990-06-21"
        defaultEmail = $citizenEmail
        defaultAddress = @{
            street   = "12 Castle Street"
            locality = "Edinburgh"
            region   = "Lothian"
            postcode = "EH1 2DU"
            country  = "Scotland"
        }
    }
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State saved to $stateFile"

# Feature 124 — surface the citizen credentials so the operator can sign
# the demo citizen into the PWA host before running phase 1.
Write-Host ""
Write-WtInfo "Next step: sign the citizen into the wallet host."
Write-WtInfo "  URL:       http://localhost/wallet/ (or your gateway equivalent)"
Write-WtInfo "  Settings → Sign in"
Write-WtInfo "  Email:     $citizenEmail"
Write-WtInfo "  Password:  $citizenPassword"
Write-Host ""
Write-WtInfo "Then enrol the device on the wallet PWA. In two terminals:"
Write-WtInfo "  T1> pwsh walkthroughs/AssuredIdentity/run-agents.ps1"
Write-WtInfo "  T2> pwsh walkthroughs/AssuredIdentity/run-phase1-identity.ps1"
Write-Host ""

Write-WtBanner "AssuredIdentity — Setup Complete"
