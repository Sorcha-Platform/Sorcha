#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# CyberEssentialsUac — Setup
# Feature: Cyber Essentials UAC posture assessment + cyber insurance application.
# Creates three single-org operator accounts (assessor org, subject-org, insurer org),
# provisions wallets and participants, creates an assessor-owned register,
# subscribes all three orgs, waits for the genesis governance roster to seal,
# substitutes the assessor issuer DID into Blueprint B, then publishes both blueprints.
#
# Blueprint A: ce-uac-assessment-template.json
#   Participants: assessor (OPEN starter), subject-org (pre-bound recipient)
#   WalletMap:    { "subject-org": <subjectWallet.Address> }   # assessor omitted — open
#
# Blueprint B: cyber-insurance-application-template.json
#   Participants: subject-org (OPEN starter, credential-gated), insurer (pre-bound)
#   WalletMap:    { "insurer": <insurerWallet.Address> }        # subject-org omitted — open
#
# Re-run guard: exits early when state.json exists; use -Force to re-provision.
# (New-SorchaRegister reuses by name; subscriptions 409 gracefully; blueprint IDs
# are timestamp-stamped so each -Force run produces a fresh pair.)

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    # Deploy-anywhere override — target any node by gateway URL without adding a
    # profile. Passed through to Initialize-SorchaEnvironment; ignored when empty.
    [string]$GatewayUrl,
    # Re-provision even when state.json already exists. Without -Force, the script
    # exits early to prevent accumulating orphan timestamped blueprints on re-run.
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "CyberEssentialsUac — Setup"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ============================================================================
# Re-run guard — exit early if state.json exists and -Force not supplied
# ============================================================================
$stateFile = Join-Path $scriptDir "state.json"
if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtInfo "state.json exists — use -Force to re-provision"
    exit 0
}

# ============================================================================
# Environment + Secrets
# ============================================================================

$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -GatewayUrl $GatewayUrl
$secrets    = Get-SorchaSecrets -WalkthroughName "cyber-essentials-uac"

# ============================================================================
# Step 1: System Admin Login
# ============================================================================
Write-WtStep "Step 1: Login as System Admin"

$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl    $sorchaEnv.TenantUrl `
    -AdminEmail   $secrets.adminEmail `
    -AdminPassword $secrets.adminPassword

Write-WtSuccess "Connected (org: $($sysAdmin.OrganizationId))"

# ============================================================================
# Step 2: Create Three Single-Org Operator Accounts
# ============================================================================
# Single-org operators avoid the multi-org OAuth-password-grant 401.
# Each org gets its own operator (Administrator role) provisioned directly
# via New-SorchaOrganization -AdminPassword.
#
# Three orgs:
#   Assessor  — runs UAC assessments, issues posture credentials
#   Subject   — assessed organisation; receives the posture credential
#   Insurer   — quotes cyber cover after posture credential gate passes

Write-WtStep "Step 2: Create Three Organisations"

# --- Assessor org ---
$assessorAdminEmail       = "ops@ce-assessor.test"
$assessorAdminDisplayName = "Assessor Operator"

$assessorOrg   = New-SorchaOrganization `
    -TenantUrl         $sorchaEnv.TenantUrl `
    -Headers           $sysAdmin.Headers `
    -Name              "CE Assessor" `
    -Subdomain         "ce-assessor" `
    -AdminEmail        $assessorAdminEmail `
    -AdminPassword     $secrets.DefaultPassword `
    -AdminDisplayName  $assessorAdminDisplayName `
    -AdminEmailVerified
$assessorOrgId = $assessorOrg.OrganizationId
Write-WtSuccess "Assessor org: $assessorOrgId"

# --- Subject org ---
$subjectAdminEmail       = "ops@ce-subject.test"
$subjectAdminDisplayName = "Subject Operator"

$subjectOrg   = New-SorchaOrganization `
    -TenantUrl         $sorchaEnv.TenantUrl `
    -Headers           $sysAdmin.Headers `
    -Name              "CE Subject Org" `
    -Subdomain         "ce-subject" `
    -AdminEmail        $subjectAdminEmail `
    -AdminPassword     $secrets.DefaultPassword `
    -AdminDisplayName  $subjectAdminDisplayName `
    -AdminEmailVerified
$subjectOrgId = $subjectOrg.OrganizationId
Write-WtSuccess "Subject org: $subjectOrgId"

# --- Insurer org ---
$insurerAdminEmail       = "ops@ce-insurer.test"
$insurerAdminDisplayName = "Insurer Operator"

$insurerOrg   = New-SorchaOrganization `
    -TenantUrl         $sorchaEnv.TenantUrl `
    -Headers           $sysAdmin.Headers `
    -Name              "CE Insurer" `
    -Subdomain         "ce-insurer" `
    -AdminEmail        $insurerAdminEmail `
    -AdminPassword     $secrets.DefaultPassword `
    -AdminDisplayName  $insurerAdminDisplayName `
    -AdminEmailVerified
$insurerOrgId = $insurerOrg.OrganizationId
Write-WtSuccess "Insurer org: $insurerOrgId"

# ============================================================================
# Step 3: Wallets + Participants + Re-login (F136/F142 wallet_address gate)
# ============================================================================
# Pattern per org:
#   1. First login (token has no wallet_address yet)
#   2. Create wallet
#   3. Register participant (links wallet to identity)
#   4. Re-login — fresh token now carries wallet_address claim
# Without step 4 the blueprint publish will 403 (no wallet_address on the JWT).

Write-WtStep "Step 3: Assessor — Wallet + Participant + Re-login"

$assessorSession = Connect-SorchaUser `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -Email          $assessorAdminEmail `
    -Password       $secrets.DefaultPassword `
    -OrganizationId $assessorOrgId

$assessorWallet = New-SorchaWallet `
    -WalletUrl    $sorchaEnv.WalletUrl `
    -Name         "CE Assessor Wallet" `
    -Headers      $assessorSession.Headers `
    -Algorithm    ED25519 `
    -FetchPublicKey
Write-WtSuccess "Assessor wallet: $($assessorWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -WalletUrl      $sorchaEnv.WalletUrl `
    -OrganizationId $assessorOrgId `
    -WalletAddress  $assessorWallet.Address `
    -DisplayName    "Assessor Operator" `
    -Headers        $assessorSession.Headers
Write-WtInfo "Assessor participant registered (wallet linked)"

# Re-login — token now carries wallet_address
$assessorSession = Connect-SorchaUser `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -Email          $assessorAdminEmail `
    -Password       $secrets.DefaultPassword `
    -OrganizationId $assessorOrgId
Write-WtInfo "Assessor session refreshed (wallet_address claim now present)"

Write-WtStep "Step 3b: Subject Org — Wallet + Participant + Re-login"

$subjectSession = Connect-SorchaUser `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -Email          $subjectAdminEmail `
    -Password       $secrets.DefaultPassword `
    -OrganizationId $subjectOrgId

$subjectWallet = New-SorchaWallet `
    -WalletUrl  $sorchaEnv.WalletUrl `
    -Name       "CE Subject Wallet" `
    -Headers    $subjectSession.Headers `
    -Algorithm  ED25519 `
    -FetchPublicKey
Write-WtSuccess "Subject wallet: $($subjectWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -WalletUrl      $sorchaEnv.WalletUrl `
    -OrganizationId $subjectOrgId `
    -WalletAddress  $subjectWallet.Address `
    -DisplayName    "Subject Operator" `
    -Headers        $subjectSession.Headers
Write-WtInfo "Subject participant registered (wallet linked)"

# Re-login
$subjectSession = Connect-SorchaUser `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -Email          $subjectAdminEmail `
    -Password       $secrets.DefaultPassword `
    -OrganizationId $subjectOrgId
Write-WtInfo "Subject session refreshed (wallet_address claim now present)"

Write-WtStep "Step 3c: Insurer — Wallet + Participant + Re-login"

$insurerSession = Connect-SorchaUser `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -Email          $insurerAdminEmail `
    -Password       $secrets.DefaultPassword `
    -OrganizationId $insurerOrgId

$insurerWallet = New-SorchaWallet `
    -WalletUrl  $sorchaEnv.WalletUrl `
    -Name       "CE Insurer Wallet" `
    -Headers    $insurerSession.Headers `
    -Algorithm  ED25519 `
    -FetchPublicKey
Write-WtSuccess "Insurer wallet: $($insurerWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -WalletUrl      $sorchaEnv.WalletUrl `
    -OrganizationId $insurerOrgId `
    -WalletAddress  $insurerWallet.Address `
    -DisplayName    "Insurer Operator" `
    -Headers        $insurerSession.Headers
Write-WtInfo "Insurer participant registered (wallet linked)"

# Re-login
$insurerSession = Connect-SorchaUser `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -Email          $insurerAdminEmail `
    -Password       $secrets.DefaultPassword `
    -OrganizationId $insurerOrgId
Write-WtInfo "Insurer session refreshed (wallet_address claim now present)"

# ============================================================================
# Step 4: Create Register (owned by assessor)
# ============================================================================
# The assessor operator holds Administrator role on the assessor org, and their
# wallet is linked — so they pass both the role gate and the F142 wallet_address
# gate (publish requires wallet to match an Owner/Admin/Designer on the roster).

Write-WtStep "Step 4: Create Cyber Essentials UAC Register"

$register = New-SorchaRegister `
    -RegisterUrl        $sorchaEnv.RegisterUrl `
    -WalletUrl          $sorchaEnv.WalletUrl `
    -Name               "Cyber Essentials UAC Register" `
    -Description        "CE UAC posture + cyber-insurance demo" `
    -TenantId           $assessorOrgId `
    -OwnerUserId        $assessorSession.UserId `
    -OwnerWalletAddress $assessorWallet.Address `
    -Headers            $assessorSession.Headers `
    -TenantUrl          $sorchaEnv.TenantUrl

$registerId = $register.RegisterId
Write-WtSuccess "Register: $registerId"

# ============================================================================
# Step 5: Subscribe Subject Org + Insurer
# ============================================================================
# Owner (assessor) is auto-subscribed server-side by the finalize endpoint.
# Subject-org MUST be subscribed — it receives and holds the posture credential.
# Insurer is subscribed so it can see insurance-application workflow instances.

Write-WtStep "Step 5: Subscribe Subject Org + Insurer"

try {
    # participant org (assessor owns the register) — Public, matches ConstructionPermit
    $null = New-SorchaRegisterSubscription `
        -TenantUrl       $sorchaEnv.TenantUrl `
        -OrganizationId  $subjectOrgId `
        -RegisterId      $registerId `
        -RegisterName    "Cyber Essentials UAC Register" `
        -SubscriptionType "Public" `
        -Headers         $subjectSession.Headers
} catch {
    Write-WtWarn "Subject-org subscription failed (may already exist): $($_.Exception.Message)"
}

try {
    # participant org (assessor owns the register) — Public, matches ConstructionPermit
    $null = New-SorchaRegisterSubscription `
        -TenantUrl       $sorchaEnv.TenantUrl `
        -OrganizationId  $insurerOrgId `
        -RegisterId      $registerId `
        -RegisterName    "Cyber Essentials UAC Register" `
        -SubscriptionType "Public" `
        -Headers         $insurerSession.Headers
} catch {
    Write-WtWarn "Insurer subscription failed (may already exist): $($_.Exception.Message)"
}

Write-WtSuccess "Register subscriptions established"

# ============================================================================
# Step 6: Wait for Genesis Governance Roster to Seal
# ============================================================================
# Blueprint publish races the genesis seal and returns 403 if the roster is
# empty. Poll until at least one member is present (≤ 60 s).

Write-WtStep "Step 6: Wait for Genesis Governance Roster to Seal"

$rosterUrl     = "$($sorchaEnv.GatewayUrl)/api/registers/$registerId/governance/roster"
$rosterTimeout = 60
$rosterDeadline = (Get-Date).AddSeconds($rosterTimeout)
$rosterReady   = $false

while ((Get-Date) -lt $rosterDeadline) {
    try {
        $roster = Invoke-SorchaApi `
            -Method  GET `
            -Uri     $rosterUrl `
            -Headers $assessorSession.Headers
        if ($roster -and $roster.members -and ($roster.members | Measure-Object).Count -gt 0) {
            $rosterReady = $true
            Write-WtSuccess "Governance roster sealed ($($($roster.members | Measure-Object).Count) member(s))"
            break
        }
    } catch {
        # 404 is expected during the pre-seal window — keep polling silently
        $sc = $null
        try { $sc = $_.Exception.Response.StatusCode.value__ } catch {}
        if ($sc -and $sc -ne 404) {
            Write-WtWarn "  Roster poll error ($sc): $($_.Exception.Message)"
        }
    }
    Start-Sleep -Seconds 2
}

if (-not $rosterReady) {
    Write-WtFail "Genesis governance roster did not seal within ${rosterTimeout}s for register $registerId"
    exit 1
}

# ============================================================================
# Step 6b: Publish Participants to Register
# ============================================================================
# Each participant's public key must be ON THE REGISTER so the issuer can
# X25519-encrypt a SorchaLocalWallet credential to the recipient (subject-org).
# Without this, issuance logs "Public key not found on register ... recipient
# skipped (no external key provided)" — the credential is minted but never
# delivered to the wallet. Mirrors ConstructionPermit's participant-publish loop.

Write-WtStep "Step 6b: Publish Participants to Register"

$participantDefs = @(
    @{ Name = "assessor";    OrgName = "Cyber Assessor Co."; OrgId = $assessorOrgId; Wallet = $assessorWallet; Headers = $assessorSession.Headers }
    @{ Name = "subject-org"; OrgName = "Assessed SME";       OrgId = $subjectOrgId;  Wallet = $subjectWallet;  Headers = $subjectSession.Headers }
    @{ Name = "insurer";     OrgName = "Cyber Insurer";      OrgId = $insurerOrgId;  Wallet = $insurerWallet;  Headers = $insurerSession.Headers }
)
foreach ($p in $participantDefs) {
    try {
        $null = Publish-SorchaParticipant `
            -TenantUrl        $sorchaEnv.TenantUrl `
            -OrganizationId   $p.OrgId `
            -RegisterId       $registerId `
            -ParticipantName  $p.Name `
            -OrganizationName $p.OrgName `
            -WalletAddress    $p.Wallet.Address `
            -PublicKey        $p.Wallet.PublicKey `
            -Headers          $p.Headers
        Write-WtSuccess "Published participant '$($p.Name)' -> $($p.Wallet.Address)"
    } catch {
        Write-WtWarn "Publish participant '$($p.Name)' failed (may already exist): $($_.Exception.Message)"
    }
}

# ============================================================================
# Step 6c: Provision the Feature-083 org master key for the credential ISSUER org.
# ============================================================================
# Blueprint A's action 1 (assessor) issues a CyberEssentialsUacPosture credential
# (targetAudience: SorchaLocalWallet) signed by the assessor org. Without a master
# key, IssuanceKeyService.GetActiveSigningMaterialAsync returns null and the mint
# FAILS (400 "Failed to issue credential"). Re-login first so the session JWT
# carries wallet_address (the master-key endpoint is wallet-authorized); the
# assessor's wallet was created above. Idempotent (409 on re-run is fine).
# See the walkthrough-builder skill.

Write-WtStep "Step 6c: Provision credential-issuer org master key"

$assessorIssuerSession = Connect-SorchaUser `
    -TenantUrl      $sorchaEnv.TenantUrl `
    -Email          $assessorAdminEmail `
    -Password       $secrets.DefaultPassword `
    -OrganizationId $assessorOrgId

Set-SorchaOrgMasterKey `
    -WalletUrl      $sorchaEnv.WalletUrl `
    -OrganizationId $assessorOrgId `
    -Headers        $assessorIssuerSession.Headers

# ============================================================================
# Step 7: Substitute Assessor Issuer DID into Blueprint B
# ============================================================================
# Blueprint B's credentialRequirements.trustPolicy.allowedIssuers contains the
# literal placeholder "{{ASSESSOR_ISSUER_DID}}" that must be replaced with the
# assessor's real DID before publish. The DID is did:sorcha:org:<walletAddress>.

Write-WtStep "Step 7: Resolve Assessor Issuer DID → Blueprint B"

$assessorDid  = "did:sorcha:org:$($assessorWallet.Address)"
Write-WtInfo "Assessor issuer DID: $assessorDid"

$bpBTemplatePath  = Join-Path $scriptDir "cyber-insurance-application-template.json"
$bpBResolvedPath  = Join-Path $scriptDir ".bpB.resolved.json"
$bpBRaw           = Get-Content -Path $bpBTemplatePath -Raw
$bpBResolved      = $bpBRaw.Replace("{{ASSESSOR_ISSUER_DID}}", $assessorDid)
$bpBResolved | Set-Content -Path $bpBResolvedPath -Encoding UTF8
Write-WtInfo "Resolved blueprint B written to $bpBResolvedPath"

# ============================================================================
# Step 8: Publish Blueprint A — CE UAC Assessment
# ============================================================================
# assessor is the OPEN starting action sender → omit from walletMap (late-bound).
# subject-org is the pre-bound credential recipient → include in walletMap.

Write-WtStep "Step 8: Publish Blueprint A — CE UAC Assessment"

$walletMapA = @{
    "subject-org" = $subjectWallet.Address
    # "assessor" intentionally absent — open starting-action sender (VAL_BP_010)
}

$blueprintA = Publish-SorchaBlueprint `
    -BlueprintUrl  $sorchaEnv.BlueprintUrl `
    -TemplatePath  (Join-Path $scriptDir "ce-uac-assessment-template.json") `
    -WalletMap     $walletMapA `
    -Headers       $assessorSession.Headers `
    -IdPrefix      "ce-uac-assessment" `
    -RegisterId    $registerId

Write-WtSuccess "Blueprint A: $($blueprintA.BlueprintId)"
if ($blueprintA.Warnings -and ($blueprintA.Warnings | Measure-Object).Count -gt 0) {
    foreach ($w in $blueprintA.Warnings) { Write-WtWarn "  $w" }
}

# ============================================================================
# Step 9: Publish Blueprint B — Cyber Insurance Application
# ============================================================================
# subject-org is the OPEN credential-gated starting action sender → omit from walletMap.
# insurer is the pre-bound quoting party → include in walletMap.

Write-WtStep "Step 9: Publish Blueprint B — Cyber Insurance Application"

$walletMapB = @{
    "insurer" = $insurerWallet.Address
    # "subject-org" intentionally absent — open credential-gated starting-action sender (VAL_BP_010)
}

try {
    $blueprintB = Publish-SorchaBlueprint `
        -BlueprintUrl  $sorchaEnv.BlueprintUrl `
        -TemplatePath  $bpBResolvedPath `
        -WalletMap     $walletMapB `
        -Headers       $assessorSession.Headers `
        -IdPrefix      "cyber-insurance-application" `
        -RegisterId    $registerId

    Write-WtSuccess "Blueprint B: $($blueprintB.BlueprintId)"
    if ($blueprintB.Warnings -and ($blueprintB.Warnings | Measure-Object).Count -gt 0) {
        foreach ($w in $blueprintB.Warnings) { Write-WtWarn "  $w" }
    }
} finally {
    # Always clean up the resolved temp file, even if Publish-SorchaBlueprint throws
    Remove-Item -Path $bpBResolvedPath -ErrorAction SilentlyContinue
}

# ============================================================================
# Step 10: HAIP Trust Prerequisites (for the selective-disclosure variant)
# ============================================================================
# These two calls are required before the assessor org can issue OID4VCI
# credentials via /api/v1/offers/. They establish the trust anchor for the
# assessor's tenant and enrol the assessor wallet as a HAIP-capable issuer.
# Mirror pattern: walkthroughs/AssuredIdentity/setup.ps1:274-291.
# Auth: RequireAdministrator + RequirePlatformAudience → use $sysAdmin.Headers.

Write-WtStep "Step 10: HAIP trust prerequisites (for the selective-disclosure variant)"

try {
    Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$assessorOrgId/provision" `
        -Headers $sysAdmin.Headers `
        -Body @{} | Out-Null
    Write-WtSuccess "Trust anchor provisioned for assessor org $assessorOrgId"
} catch {
    Write-WtWarn "Trust anchor provision failed (may already exist): $($_.Exception.Message)"
}

try {
    Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.GatewayUrl)/api/v1/trust/tenants/$assessorOrgId/orgs/$($assessorWallet.Address)/enrol" `
        -Headers $sysAdmin.Headers `
        -Body @{
            orgPublicKeyBase64 = $assessorWallet.PublicKey
            orgDisplayName     = "Cyber Assessor Co."
        } | Out-Null
    Write-WtSuccess "Assessor org enrolled as HAIP issuer (wallet: $($assessorWallet.Address))"
} catch {
    Write-WtWarn "Assessor org enrolment failed (may already exist): $($_.Exception.Message)"
}

# ============================================================================
# Step 11: Register HAIP Service Principal (for the selective-disclosure variant)
# ============================================================================
# The /api/v1/offers/ and /api/v1/verifier/requests endpoints on the HAIP
# service require a token with a client_id claim (RequireService policy,
# HAIP Program.cs:38-40). A service token is obtained via the client_credentials
# OAuth2 grant against /api/service-auth/token — this requires a registered
# service principal. We register one here and persist the credentials so
# run-haip-sd.ps1 can exchange them for a service token at run time.

Write-WtStep "Step 11: Register HAIP walkthrough service principal"

$svcPrincipal = $null
try {
    $svcPrincipal = Invoke-SorchaApi -Method POST `
        -Uri "$($sorchaEnv.GatewayUrl)/api/service-principals/" `
        -Headers $sysAdmin.Headers `
        -Body @{
            serviceName = "ce-uac-haip-walkthrough"
            scopes      = @("haip:issue", "haip:verify")
        }
    Write-WtSuccess "Service principal registered: $($svcPrincipal.clientId)"
} catch {
    Write-WtWarn "Service principal registration failed (may already exist — will attempt lookup): $($_.Exception.Message)"
    # Attempt lookup by name — list all and find by serviceName
    try {
        $spList = Invoke-SorchaApi -Method GET `
            -Uri "$($sorchaEnv.GatewayUrl)/api/service-principals/" `
            -Headers $sysAdmin.Headers
        $existing = $spList.servicePrincipals | Where-Object { $_.serviceName -eq "ce-uac-haip-walkthrough" }
        if ($existing) {
            Write-WtWarn "Found existing service principal '$($existing.clientId)' but cannot recover the secret — delete it via the admin API and re-run setup to obtain fresh credentials."
        }
    } catch {
        Write-WtWarn "Service principal lookup also failed: $($_.Exception.Message)"
    }
}

# Persist client credentials so run-haip-sd.ps1 can exchange them for a service token.
# If registration failed above, $svcPrincipal is $null and the state block records nulls
# — run-haip-sd.ps1 will detect this and exit with a clear error.
$haipClientId     = if ($svcPrincipal) { $svcPrincipal.clientId }     else { $null }
$haipClientSecret = if ($svcPrincipal) { $svcPrincipal.clientSecret } else { $null }

# ============================================================================
# Save State
# ============================================================================
Write-WtStep "Saving State"

$state = @{
    profile      = $Profile
    gatewayUrl   = $sorchaEnv.GatewayUrl
    tenantUrl    = $sorchaEnv.TenantUrl
    walletUrl    = $sorchaEnv.WalletUrl
    blueprintUrl = $sorchaEnv.BlueprintUrl
    registerUrl  = $sorchaEnv.RegisterUrl
    registerId   = $registerId
    assessorDid  = $assessorDid
    haip         = @{
        clientId     = $haipClientId
        clientSecret = $haipClientSecret
    }
    blueprints   = @{
        "ce-uac-assessment"          = @{ id = $blueprintA.BlueprintId }
        "cyber-insurance-application" = @{ id = $blueprintB.BlueprintId }
    }
    roles = @{
        "assessor"   = @{
            organizationId = $assessorOrgId
            walletAddress  = $assessorWallet.Address
            publicKey      = $assessorWallet.PublicKey
            email          = $assessorAdminEmail
            password       = $secrets.DefaultPassword
            userId         = $assessorSession.UserId
        }
        "subject-org" = @{
            organizationId = $subjectOrgId
            walletAddress  = $subjectWallet.Address
            publicKey      = $subjectWallet.PublicKey
            email          = $subjectAdminEmail
            password       = $secrets.DefaultPassword
            userId         = $subjectSession.UserId
        }
        "insurer"    = @{
            organizationId = $insurerOrgId
            walletAddress  = $insurerWallet.Address
            publicKey      = $insurerWallet.PublicKey
            email          = $insurerAdminEmail
            password       = $secrets.DefaultPassword
            userId         = $insurerSession.UserId
        }
    }
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile
Write-WtSuccess "State saved to $stateFile"

Write-Host ""
Write-WtInfo "Next step: run the walkthrough phases."
Write-WtInfo "  Blueprint A (CE UAC Assessment):      $($blueprintA.BlueprintId)"
Write-WtInfo "  Blueprint B (Cyber Insurance):        $($blueprintB.BlueprintId)"
Write-WtInfo "  Register:                             $registerId"
Write-WtInfo "  Assessor DID:                         $assessorDid"
Write-Host ""

Write-WtBanner "CyberEssentialsUac — Setup Complete"
