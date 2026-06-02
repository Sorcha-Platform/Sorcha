#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# CyberEssentialsUac — HAIP/OID4VP Selective-Disclosure Variant
# Feature: Prove genuine on-the-wire selective disclosure of the CyberEssentialsUacPosture
# credential — the SorchaInternal core cannot do this as it discloses the full credential.
#
# Flow:
#   1. Assessor issues CyberEssentialsUacPosture via OID4VCI → agent file wallet
#   2. Agent presents via OID4VP disclosing ONLY 4 of 9 evidence claims
#   3. Assertions:
#      POSITIVE: verifiedClaims = exactly {compliant, assessmentDate, passwordApproach, mfaAdminEnforced}
#      NEGATIVE: a second verifier request requiring "policyEvidenceHash" is REJECTED as invalid
#
# Prerequisites:
#   - setup.ps1 must have completed (state.json present with haip.clientId + clientSecret)
#   - Docker stack running with docker-compose.ce-uac-local.yml applied
#     (haip-service needs Haip__IssuerUrl=http://127.0.0.1)
#   - .NET SDK installed (sorcha-agent is run via `dotnet run`)
#
# SERVICE TOKEN NOTE:
#   The /api/v1/offers/ and /api/v1/verifier/requests + /result endpoints require a token
#   with a client_id claim (HAIP Service, Program.cs:38-40: RequireService policy =
#   RequireAuthenticatedUser + RequireClaim("client_id")). This script uses a real
#   client_credentials service token obtained from /api/service-auth/token using the
#   service principal credentials persisted by setup.ps1 in state.json → haip.clientId
#   + haip.clientSecret.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [string]$GatewayUrl,
    [switch]$ShowJson
)

$ErrorActionPreference = "Stop"

$modulePath = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) "modules/SorchaWalkthrough/SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "CyberEssentialsUac — HAIP/OID4VP Selective-Disclosure Variant"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# ============================================================================
# Helper: Assert
# ============================================================================
function Assert {
    param(
        [bool]$Condition,
        [string]$Message
    )
    if (-not $Condition) {
        Write-WtFail "ASSERTION FAILED: $Message"
        exit 1
    }
    Write-WtSuccess "ASSERT OK: $Message"
}

# Helper: defensively enumerate claim names from verifiedClaims regardless of
# whether it came back as a PSCustomObject or a Hashtable (profile/version variation).
function Get-ClaimNames {
    param($verifiedClaims)
    if ($null -eq $verifiedClaims) { return @() }
    if ($verifiedClaims -is [hashtable]) {
        return @($verifiedClaims.Keys)
    }
    return @($verifiedClaims.PSObject.Properties.Name)
}

# ============================================================================
# Load State
# ============================================================================
$stateFile = Join-Path $scriptDir "state.json"
if (-not (Test-Path $stateFile)) {
    Write-WtFail "state.json not found — run setup.ps1 first"
    exit 1
}

$state = Get-Content -Path $stateFile -Raw | ConvertFrom-Json
Write-WtSuccess "State loaded (gateway: $($state.gatewayUrl))"

# Validate that the HAIP service principal credentials were persisted by setup.ps1
if (-not $state.haip -or -not $state.haip.clientId -or -not $state.haip.clientSecret) {
    Write-WtFail @"
state.json is missing haip.clientId / haip.clientSecret.
These are written by setup.ps1 Step 11 (service principal registration).
Re-run setup.ps1 with -Force to re-provision and regenerate the credentials.
"@
    exit 1
}

# ============================================================================
# Resolve environment (gateway URL override)
# ============================================================================
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -GatewayUrl $GatewayUrl

# Use the gateway URL from state if not overridden via -GatewayUrl / -Profile
$baseUrl   = if ($GatewayUrl) { $GatewayUrl.TrimEnd('/') } else { $state.gatewayUrl.TrimEnd('/') }
$tenantUrl = $state.tenantUrl.TrimEnd('/')

# ============================================================================
# Step 1: Authenticate assessor + acquire service token
# ============================================================================
Write-WtStep "Step 1: Authenticate assessor + acquire HAIP service token"

$walletDir = Join-Path $scriptDir "agent-wallet"
if (-not (Test-Path $walletDir)) {
    New-Item -ItemType Directory -Path $walletDir -Force | Out-Null
}

$secrets = Get-SorchaSecrets -WalkthroughName "cyber-essentials-uac"

# Assessor session (needed so we have the wallet address for the offer body)
$assessorSession = Connect-SorchaUser `
    -TenantUrl      $tenantUrl `
    -Email          $state.roles.assessor.email `
    -Password       $secrets.DefaultPassword `
    -OrganizationId $state.roles.assessor.organizationId
Write-WtSuccess "Assessor authenticated"

# SERVICE TOKEN — client_credentials grant using the service principal registered by setup.ps1.
# The resulting token carries a client_id claim and token_type=service, satisfying the
# RequireService policy on /api/v1/offers/ and /api/v1/verifier/requests + /result.
$encodedSecret = [Uri]::EscapeDataString($state.haip.clientSecret)
$ccBody = "grant_type=client_credentials&client_id=$($state.haip.clientId)&client_secret=$encodedSecret&scope=haip:issue haip:verify"
$tokenResp = Invoke-SorchaApi -Method POST `
    -Uri "$tenantUrl/api/service-auth/token" `
    -Body $ccBody `
    -ContentType "application/x-www-form-urlencoded"

Assert ($tokenResp -and $tokenResp.access_token) "client_credentials grant returned an access token"
$svcHeaders = @{ Authorization = "Bearer $($tokenResp.access_token)" }
Write-WtSuccess "Service token acquired (client: $($state.haip.clientId))"

# ============================================================================
# Step 2: Create the OID4VCI credential offer (all 9 claims, all disclosable)
# ============================================================================
# CRITICAL: ALL 9 evidence claims MUST be in disclosablePaths.
# Any claim NOT listed is minted as always-plaintext (not wrapped in an SD-JWT
# disclosure) and would be visible in the credential header regardless of --disclose,
# breaking the negative assertion (withheld claims must be absent from the wire).

Write-WtStep "Step 2: Create OID4VCI credential offer (9 claims, all in disclosablePaths)"

$offerBody = @{
    issuerWalletAddress = $state.roles.assessor.walletAddress
    tenantId            = $state.roles.assessor.organizationId
    credentialType      = "CyberEssentialsUacPosture"
    claims              = @{
        compliant          = $true
        assessmentDate     = "2026-06-01"
        infraVersion       = "v3.3"
        passwordApproach   = "denylist+12"
        mfaAdminEnforced   = $true
        assessorType       = "consultant"
        scopeDeviceCount   = 42
        mfaCoverage        = 6
        staleAccounts      = 0
        policyEvidenceHash = "sha256:9f2c1a"
    }
    disclosablePaths    = @(
        "compliant",
        "assessmentDate",
        "infraVersion",
        "passwordApproach",
        "mfaAdminEnforced",
        "assessorType",
        "scopeDeviceCount",
        "mfaCoverage",
        "staleAccounts",
        "policyEvidenceHash"
    )
}

if ($ShowJson) { Write-Host "Offer body: $($offerBody | ConvertTo-Json -Depth 5)" }

$offer = Invoke-SorchaApi -Method POST `
    -Uri "$baseUrl/api/v1/offers/" `
    -Headers $svcHeaders `
    -Body $offerBody

Assert ($offer -and $offer.offerId)             "offer created (offerId present)"
Assert ($offer.credentialOfferUri -ne $null)    "offer has credentialOfferUri"
Write-WtSuccess "Offer created: $($offer.offerId)"
Write-WtInfo   "Credential offer URI: $($offer.credentialOfferUri)"

# ============================================================================
# Step 3: Agent receives the credential into the file wallet
# ============================================================================
Write-WtStep "Step 3: Agent haip receive → file wallet"

$agentProject = Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDir)) "src/Apps/Sorcha.Agent/Sorcha.Agent.csproj"
if (-not (Test-Path $agentProject)) {
    Write-WtFail "Sorcha.Agent project not found at: $agentProject"
    exit 1
}

$credentialFile = Join-Path $walletDir "credentials/CyberEssentialsUacPosture.sdjwt"

& dotnet run --project $agentProject -- haip receive `
    --offer-uri $offer.credentialOfferUri `
    --wallet-dir $walletDir

Assert (Test-Path $credentialFile) "agent received the credential into its file wallet ($credentialFile)"
Write-WtSuccess "Credential written to file wallet: $credentialFile"

# ============================================================================
# Step 4: Create OID4VP verifier request (4 required claims)
# ============================================================================
Write-WtStep "Step 4: Create OID4VP verifier request (requiring 4 claims)"

$vreqBody = @{
    credentialType  = "CyberEssentialsUacPosture"
    requiredClaims  = @("compliant", "assessmentDate", "passwordApproach", "mfaAdminEnforced")
    acceptedIssuers = @($state.assessorDid)
}

if ($ShowJson) { Write-Host "Verifier request body: $($vreqBody | ConvertTo-Json -Depth 3)" }

$vreq = Invoke-SorchaApi -Method POST `
    -Uri "$baseUrl/api/v1/verifier/requests" `
    -Headers $svcHeaders `
    -Body $vreqBody

Assert ($vreq -and $vreq.requestId) "verifier request created (requestId present)"
Assert ($vreq.requestUri -ne $null)  "verifier request has requestUri"
Write-WtSuccess "Verifier request created: $($vreq.requestId)"
Write-WtInfo   "Request URI: $($vreq.requestUri)"

# ============================================================================
# Step 5: Agent presents the credential — disclosing ONLY 4 of 9 claims
# ============================================================================
Write-WtStep "Step 5: Agent haip present (disclose: compliant,assessmentDate,passwordApproach,mfaAdminEnforced)"

& dotnet run --project $agentProject -- haip present `
    --request-uri $vreq.requestUri `
    --credential CyberEssentialsUacPosture `
    --disclose "compliant,assessmentDate,passwordApproach,mfaAdminEnforced" `
    --wallet-dir $walletDir

Write-WtSuccess "Agent presentation complete"

# ============================================================================
# Step 6: THE ASSERTION — verifier received EXACTLY the 4 disclosed claims
# ============================================================================
Write-WtStep "Step 6: Read-back and assert selective-disclosure (positive path)"

$res = Invoke-SorchaApi -Method GET `
    -Uri "$baseUrl/api/v1/verifier/requests/$($vreq.requestId)/result" `
    -Headers $svcHeaders

if ($ShowJson) { Write-Host "Verifier result: $($res | ConvertTo-Json -Depth 10)" }

Assert ($res -and $res.result)             "result payload returned"
Assert ($res.result.isValid -eq $true)     "verifier accepted the presentation (isValid = true)"

# Strip JWT envelope claims — we only want to inspect the credential-domain claims
$envelope = @('iss', 'iat', 'exp', 'nbf', 'sub', 'cnf', 'vct', 'status')
$allNames = Get-ClaimNames -verifiedClaims $res.result.verifiedClaims
$got      = @($allNames | Where-Object { $_ -notin $envelope } | Sort-Object)
$expected = @('assessmentDate', 'compliant', 'mfaAdminEnforced', 'passwordApproach')

Assert (-not (Compare-Object $got $expected)) "verifier received EXACTLY the 4 disclosed claims (got: $($got -join ','))"

# Negative presence check — none of the 5 withheld evidence claims should appear
$withheld = @('assessorType', 'mfaCoverage', 'policyEvidenceHash', 'scopeDeviceCount', 'staleAccounts')
$leaked   = @($withheld | Where-Object { $allNames -contains $_ })
Assert ($leaked.Count -eq 0) "withheld evidence claims NOT present in verifiedClaims (selective disclosure holds — nothing leaked: $($withheld -join ','))"

Write-WtSuccess "Positive assertion: PASS — exactly 4 claims disclosed; 5 withheld claims absent from the wire"

# ============================================================================
# Step 7: NEGATIVE TEST — verifier rejects when a withheld claim is required
# ============================================================================
# Issue a SECOND verifier request requiring policyEvidenceHash (one of the 5
# withheld claims). Present the SAME credential with the SAME --disclose list
# (still only 4 claims). The verifier must reject it as invalid because the
# required claim was not disclosed — proving the selective disclosure is genuine,
# not just omitted from the response.

Write-WtStep "Step 7: Negative test — verifier requires withheld claim, should reject"

$vreq2Body = @{
    credentialType  = "CyberEssentialsUacPosture"
    requiredClaims  = @("compliant", "assessmentDate", "passwordApproach", "mfaAdminEnforced", "policyEvidenceHash")
    acceptedIssuers = @($state.assessorDid)
}

$vreq2 = Invoke-SorchaApi -Method POST `
    -Uri "$baseUrl/api/v1/verifier/requests" `
    -Headers $svcHeaders `
    -Body $vreq2Body

Assert ($vreq2 -and $vreq2.requestId) "second verifier request created"

# Present again — same 4-claim disclosure, withholding policyEvidenceHash
& dotnet run --project $agentProject -- haip present `
    --request-uri $vreq2.requestUri `
    --credential CyberEssentialsUacPosture `
    --disclose "compliant,assessmentDate,passwordApproach,mfaAdminEnforced" `
    --wallet-dir $walletDir

$res2 = Invoke-SorchaApi -Method GET `
    -Uri "$baseUrl/api/v1/verifier/requests/$($vreq2.requestId)/result" `
    -Headers $svcHeaders

if ($ShowJson) { Write-Host "Second verifier result: $($res2 | ConvertTo-Json -Depth 10)" }

Assert ($res2 -and $res2.result) "second result payload returned"
Assert ($res2.result.isValid -eq $false) "verifier rejects when a withheld claim is required (policyEvidenceHash not disclosed — selective disclosure genuinely absent from the wire)"

Write-WtSuccess "Negative assertion: PASS — verifier correctly rejects presentation missing required withheld claim"

# ============================================================================
# Done
# ============================================================================
Write-Host ""
Write-WtBanner "HAIP selective-disclosure variant — PASS"
Write-Host ""
Write-WtInfo "Summary:"
Write-WtInfo "  Credential type : CyberEssentialsUacPosture"
Write-WtInfo "  Total claims     : 9"
Write-WtInfo "  Disclosed        : 4  (compliant, assessmentDate, passwordApproach, mfaAdminEnforced)"
Write-WtInfo "  Withheld         : 5  (assessorType, mfaCoverage, policyEvidenceHash, scopeDeviceCount, staleAccounts)"
Write-WtInfo "  Positive test    : verifier accepted the presentation, saw exactly 4 claims"
Write-WtInfo "  Negative test    : verifier rejected presentation when it required a withheld claim"
Write-WtInfo "  Agent wallet dir : $walletDir"
Write-Host ""
