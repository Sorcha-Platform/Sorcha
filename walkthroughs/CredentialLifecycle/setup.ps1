#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# CredentialLifecycle — Setup
#
# Provisions the two orgs, two wallets, one register and two blueprints the
# credential-lifecycle conformance run needs.
#
# Two participants only, both PRE-BOUND. A conformance check should fail for one
# reason at a time, so everything not under test — open participants, late
# binding, multi-org routing — is deliberately left out.
#
#   authority : issues the credential, owns its status, owns + publishes the
#               register. Every suspend/reinstate/revoke authenticates as this
#               org, because only the original issuer may drive lifecycle.
#   holder    : receives the credential and presents it at the gate.
#
# Idempotent: re-running reuses an existing valid state.json unless -Force.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    # Deploy-anywhere override — target ANY node by gateway base URL.
    [string]$GatewayUrl,
    [switch]$SkipHealthCheck,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) `
    "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "CredentialLifecycle — Conformance Setup"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$stateFile = Join-Path $scriptDir "state.json"

$initParams = @{ Profile = $Profile }
if ($GatewayUrl)      { $initParams.GatewayUrl = $GatewayUrl }
if ($SkipHealthCheck) { $initParams.SkipHealthCheck = $true }
$sorchaEnv = Initialize-SorchaEnvironment @initParams

$secrets = Get-SorchaSecrets -WalkthroughName "credential-lifecycle"

# ============================================================================
# Reuse existing state unless -Force
# ============================================================================
if ((Test-Path $stateFile) -and -not $Force) {
    Write-WtInfo "state.json exists — reusing. Use -Force to re-provision."
    Write-WtInfo "Run: pwsh walkthroughs/CredentialLifecycle/run-conformance.ps1 -Profile $Profile"
    exit 0
}

# ============================================================================
# Step 1: System Admin
# ============================================================================
Write-WtStep "Step 1: Login as System Admin"

$sysAdmin = Connect-SorchaAdmin `
    -TenantUrl     $sorchaEnv.TenantUrl `
    -AdminEmail    $secrets.adminEmail `
    -AdminPassword $secrets.adminPassword

Write-WtSuccess "Connected (org: $($sysAdmin.OrganizationId))"

# ============================================================================
# Step 2: Two single-org operator accounts
# ============================================================================
# Single-org operators avoid the multi-org OAuth password-grant 401 — a public
# user later added to an org becomes multi-org, and the grant has no org-selection
# step. Provision each operator directly into its own org instead.

Write-WtStep "Step 2: Create the authority and holder organisations"

$authorityEmail = "ops@clc-authority.test"
$holderEmail    = "ops@clc-holder.test"

$authorityOrg = New-SorchaOrganization `
    -TenantUrl        $sorchaEnv.TenantUrl `
    -Headers          $sysAdmin.Headers `
    -Name             "Lifecycle Authority" `
    -Subdomain        "clc-authority" `
    -AdminEmail       $authorityEmail `
    -AdminPassword    $secrets.DefaultPassword `
    -AdminDisplayName "Authority Operator" `
    -AdminEmailVerified
$authorityOrgId = $authorityOrg.OrganizationId
Write-WtSuccess "Authority org: $authorityOrgId"

$holderOrg = New-SorchaOrganization `
    -TenantUrl        $sorchaEnv.TenantUrl `
    -Headers          $sysAdmin.Headers `
    -Name             "Lifecycle Holder" `
    -Subdomain        "clc-holder" `
    -AdminEmail       $holderEmail `
    -AdminPassword    $secrets.DefaultPassword `
    -AdminDisplayName "Holder Operator" `
    -AdminEmailVerified
$holderOrgId = $holderOrg.OrganizationId
Write-WtSuccess "Holder org: $holderOrgId"

# ============================================================================
# Step 3: Wallets + participants + RE-LOGIN
# ============================================================================
# `wallet_address` is added to the JWT only at LOGIN, from the user's first active
# linked wallet. We log in, then create and link the wallet — so the cached token
# has no wallet_address and every wallet-authorised call fails for it. Re-login
# after linking or the blueprint publish 403s on the F142 governance gate.

Write-WtStep "Step 3: Authority wallet + participant + re-login"

$authoritySession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl -Email $authorityEmail `
    -Password $secrets.DefaultPassword -OrganizationId $authorityOrgId

$authorityWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl -Name "Lifecycle Authority Wallet" `
    -Headers $authoritySession.Headers -Algorithm ED25519 -FetchPublicKey
Write-WtSuccess "Authority wallet: $($authorityWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $authorityOrgId -WalletAddress $authorityWallet.Address `
    -DisplayName "Authority Operator" -Headers $authoritySession.Headers

$authoritySession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl -Email $authorityEmail `
    -Password $secrets.DefaultPassword -OrganizationId $authorityOrgId
Write-WtInfo "Authority session refreshed (wallet_address claim now present)"

Write-WtStep "Step 3b: Holder wallet + participant + re-login"

$holderSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl -Email $holderEmail `
    -Password $secrets.DefaultPassword -OrganizationId $holderOrgId

$holderWallet = New-SorchaWallet `
    -WalletUrl $sorchaEnv.WalletUrl -Name "Lifecycle Holder Wallet" `
    -Headers $holderSession.Headers -Algorithm ED25519 -FetchPublicKey
Write-WtSuccess "Holder wallet: $($holderWallet.Address)"

$null = Register-SorchaParticipant `
    -TenantUrl $sorchaEnv.TenantUrl -WalletUrl $sorchaEnv.WalletUrl `
    -OrganizationId $holderOrgId -WalletAddress $holderWallet.Address `
    -DisplayName "Holder Operator" -Headers $holderSession.Headers

$holderSession = Connect-SorchaUser `
    -TenantUrl $sorchaEnv.TenantUrl -Email $holderEmail `
    -Password $secrets.DefaultPassword -OrganizationId $holderOrgId
Write-WtInfo "Holder session refreshed (wallet_address claim now present)"

# ============================================================================
# Step 4: Register, owned by the authority
# ============================================================================
# The register OWNER must hold Administrator AND own the wallet on the roster —
# publishing requires both. The authority operator is both, so it owns and
# publishes; the holder is only a subscriber.

Write-WtStep "Step 4: Create the conformance register"

$register = New-SorchaRegister `
    -RegisterUrl        $sorchaEnv.RegisterUrl `
    -WalletUrl          $sorchaEnv.WalletUrl `
    -Name               "Credential Lifecycle Conformance" `
    -Description        "Issuance + status-lifecycle conformance checks" `
    -TenantId           $authorityOrgId `
    -OwnerUserId        $authoritySession.UserId `
    -OwnerWalletAddress $authorityWallet.Address `
    -Headers            $authoritySession.Headers `
    -TenantUrl          $sorchaEnv.TenantUrl

$registerId = $register.RegisterId
Write-WtSuccess "Register: $registerId"

# The genesis control tx that records the owner governance roster seals ASYNC after
# New-SorchaRegister returns. Publishing before it seals reads an EMPTY roster and
# fail-closes with the same 403 as a missing wallet_address.
Write-WtStep "Step 4b: Wait for the register-genesis roster to seal"
Wait-SorchaRegisterRoster `
    -GatewayUrl $sorchaEnv.GatewayUrl `
    -RegisterId $registerId `
    -Headers    $authoritySession.Headers
Write-WtSuccess "Roster sealed — safe to publish"

# ============================================================================
# Step 5: Subscribe the holder
# ============================================================================
# The owner is auto-subscribed server-side. The holder MUST be subscribed — it
# receives and holds the credential.
Write-WtStep "Step 5: Subscribe the holder org"
try {
    $null = New-SorchaRegisterSubscription `
        -TenantUrl        $sorchaEnv.TenantUrl `
        -OrganizationId   $holderOrgId `
        -RegisterId       $registerId `
        -RegisterName     "Credential Lifecycle Conformance" `
        -SubscriptionType "Public" `
        -Headers          $holderSession.Headers
    Write-WtSuccess "Holder subscribed"
} catch {
    Write-WtWarn "Holder subscription failed (may already exist): $($_.Exception.Message)"
}

# ============================================================================
# Step 5b: PUBLISH both participants onto the register
# ============================================================================
# Register-SorchaParticipant links a wallet to an identity in the TENANT. It does
# NOT put the participant's public key on the REGISTER — that is what publishing
# does, and without it `resolve-public-keys` returns notFound for every wallet.
#
# The consequence is a four-step silent chain, all behind HTTP 202/200:
#   1. no resolvable key   -> every recipient is skipped
#   2. no recipients       -> the action payload has no disclosure-group envelope
#   3. cannot decrypt it   -> claim mappings find nothing and are DROPPED, so the
#                             credential mints with no claims
#   4. no recipients       -> the minted credential is never delivered
#
# Observed end to end on n1 while building this suite: "Claim mapping source
# '/subjectName' has no value in action data; dropping claim" is the tell, and it
# points at the schema rather than at the missing publish.
Write-WtStep "Step 5b: Publish both participants onto the register"

$authorityPublish = Publish-SorchaParticipant `
    -TenantUrl        $sorchaEnv.TenantUrl `
    -OrganizationId   $authorityOrgId `
    -RegisterId       $registerId `
    -ParticipantName  "Authority Operator" `
    -OrganizationName "Lifecycle Authority" `
    -WalletAddress    $authorityWallet.Address `
    -PublicKey        $authorityWallet.PublicKey `
    -Headers          $authoritySession.Headers

$holderPublish = Publish-SorchaParticipant `
    -TenantUrl        $sorchaEnv.TenantUrl `
    -OrganizationId   $holderOrgId `
    -RegisterId       $registerId `
    -ParticipantName  "Holder Operator" `
    -OrganizationName "Lifecycle Holder" `
    -WalletAddress    $holderWallet.Address `
    -PublicKey        $holderWallet.PublicKey `
    -Headers          $holderSession.Headers

# The publish tx seals ASYNC. Publishing a blueprint or issuing against an unsealed
# participant reads a register that does not know the wallet yet.
foreach ($pub in @(
    @{ Name = "authority"; Result = $authorityPublish; Headers = $authoritySession.Headers },
    @{ Name = "holder";    Result = $holderPublish;    Headers = $holderSession.Headers }
)) {
    $txId = $pub.Result.transactionId
    if ($txId) {
        Wait-SorchaActorReady -Mode ParticipantSealed -TxId $txId `
            -RegisterId $registerId -Headers $pub.Headers -GatewayUrl $sorchaEnv.GatewayUrl | Out-Null
        Write-WtSuccess "$($pub.Name) participant published + sealed"
    } else {
        Write-WtWarn "$($pub.Name) publish returned no transactionId — cannot confirm it sealed"
    }
}

# Prove it rather than assume it: if the keys do not resolve here, every credential
# this suite issues will be empty and undeliverable, and the failure will surface
# far away as a claim-mapping warning.
$resolved = Invoke-SorchaApi `
    -Method POST `
    -Uri "$($sorchaEnv.RegisterUrl)/registers/$registerId/participants/resolve-public-keys" `
    -Body @{ walletAddresses = @($authorityWallet.Address, $holderWallet.Address) } `
    -Headers $authoritySession.Headers

if ($resolved.notFound -and @($resolved.notFound).Count -gt 0) {
    throw ("Participant public keys are NOT resolvable on register $registerId after publishing: " +
           "$($resolved.notFound -join ', '). Credentials issued now would mint with no claims and " +
           "never reach the holder.")
}
Write-WtSuccess "Both participant public keys resolve on the register"

# ============================================================================
# Step 6: Feature 083 master key for the authority
# ============================================================================
# WITHOUT THIS the mint silently falls back to the org's ROOT WALLET key and
# produces a credential whose `iss` is a bare wallet address with no `kid` and no
# `jwk` — unverifiable, and the gate then refuses every presentation with
# "issuer signature not verified". It looks like a platform bug; it is a missing
# setup step. A conformance suite that cannot issue a verifiable credential
# cannot test anything.
Write-WtStep "Step 6: Provision the authority's issuance master key"

Set-SorchaOrgMasterKey `
    -WalletUrl      $sorchaEnv.WalletUrl `
    -OrganizationId $authorityOrgId `
    -Headers        $authoritySession.Headers
Write-WtSuccess "Master key provisioned (idempotent on 409)"

# ============================================================================
# Step 7: Resolve the authority's ISSUANCE DID
# ============================================================================
# This is NOT did:sorcha:org:<operational wallet>. With a master key provisioned,
# credentials are signed by a DERIVED vc-issuance child key and `iss` carries that
# child's address. Pinning the operational address yields a trust policy that
# matches nothing: the credential issues and delivers perfectly and the gate then
# refuses it, which reads exactly like a revocation failure.
#
# The org's DID document is the authority for which to pin — its `id` IS the
# issuance DID. Resolve it; never reconstruct the string.
# NB the endpoint is served at /orgs/{orgId}/did.json — NOT under /api.
Write-WtStep "Step 7: Resolve the authority issuance DID"

$didDocUrl = "$($sorchaEnv.GatewayUrl)/orgs/$authorityOrgId/did.json"
$didDoc    = Invoke-SorchaApi -Method GET -Uri $didDocUrl
$authorityDid = $didDoc.id
if (-not $authorityDid) {
    throw ("Could not resolve the authority org's issuance DID from $didDocUrl. Without it the " +
           "trust policy would pin the operational wallet DID, which no issued credential carries, " +
           "and every gate submission would be refused for the wrong reason.")
}
Write-WtInfo "Authority issuance DID: $authorityDid"
if ($authorityDid -eq "did:sorcha:org:$($authorityWallet.Address)") {
    Write-WtWarn "  Issuance DID equals the operational wallet DID — the master key may not be provisioned."
}

# ============================================================================
# Step 8: Publish both blueprints
# ============================================================================
Write-WtStep "Step 8: Publish the issuance blueprint"

$walletMap = @{
    "holder"    = $holderWallet.Address
    "authority" = $authorityWallet.Address
}

$bpIssuance = Publish-SorchaBlueprint `
    -BlueprintUrl $sorchaEnv.BlueprintUrl `
    -TemplatePath (Join-Path $scriptDir "credential-lifecycle-issuance-template.json") `
    -WalletMap    $walletMap `
    -Headers      $authoritySession.Headers `
    -IdPrefix     "credential-lifecycle-issuance" `
    -RegisterId   $registerId

Write-WtSuccess "Issuance blueprint: $($bpIssuance.BlueprintId)"
foreach ($w in @($bpIssuance.Warnings)) { if ($w) { Write-WtWarn "  $w" } }

Write-WtStep "Step 8b: Publish the gate blueprint"

$gateTemplate = Join-Path $scriptDir "credential-lifecycle-gate-template.json"
$gateResolved = Join-Path $scriptDir ".gate.resolved.json"
(Get-Content -Path $gateTemplate -Raw).Replace("{{AUTHORITY_ISSUER_DID}}", $authorityDid) |
    Set-Content -Path $gateResolved -Encoding UTF8

try {
    $bpGate = Publish-SorchaBlueprint `
        -BlueprintUrl $sorchaEnv.BlueprintUrl `
        -TemplatePath $gateResolved `
        -WalletMap    @{
            "holder"   = $holderWallet.Address
            "verifier" = $authorityWallet.Address
        } `
        -Headers      $authoritySession.Headers `
        -IdPrefix     "credential-lifecycle-gate" `
        -RegisterId   $registerId

    Write-WtSuccess "Gate blueprint: $($bpGate.BlueprintId)"
    foreach ($w in @($bpGate.Warnings)) { if ($w) { Write-WtWarn "  $w" } }
} finally {
    Remove-Item -Path $gateResolved -ErrorAction SilentlyContinue
}

# ============================================================================
# Step 9: Save state
# ============================================================================
$state = [ordered]@{
    profile      = $Profile
    gatewayUrl   = $sorchaEnv.GatewayUrl
    tenantUrl    = $sorchaEnv.TenantUrl
    blueprintUrl = $sorchaEnv.BlueprintUrl
    registerUrl  = $sorchaEnv.RegisterUrl
    walletUrl    = $sorchaEnv.WalletUrl
    registerId   = $registerId
    authorityDid = $authorityDid
    credentialType = "https://sorcha.dev/vc/credential-lifecycle-conformance/v1"
    blueprints   = [ordered]@{
        issuance = @{ id = $bpIssuance.BlueprintId }
        gate     = @{ id = $bpGate.BlueprintId }
    }
    roles        = [ordered]@{
        authority = [ordered]@{
            email          = $authorityEmail
            password       = $secrets.DefaultPassword
            organizationId = $authorityOrgId
            userId         = $authoritySession.UserId
            walletAddress  = $authorityWallet.Address
            publicKey      = $authorityWallet.PublicKey
        }
        holder = [ordered]@{
            email          = $holderEmail
            password       = $secrets.DefaultPassword
            organizationId = $holderOrgId
            userId         = $holderSession.UserId
            walletAddress  = $holderWallet.Address
            publicKey      = $holderWallet.PublicKey
        }
    }
}

$state | ConvertTo-Json -Depth 10 | Set-Content -Path $stateFile -Encoding UTF8

Write-WtBanner "CredentialLifecycle — Setup complete"
Write-WtInfo "2 orgs, 2 wallets, 1 register, 2 blueprints"
Write-WtInfo "Run: pwsh walkthroughs/CredentialLifecycle/run-conformance.ps1 -Profile $Profile"
