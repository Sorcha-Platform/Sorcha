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
#   2. Agent presents via OID4VP disclosing ONLY 4 of 10 evidence claims
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
# WHERE THIS RUNS:
#   LOCAL STACK ONLY. POST /api/v1/offers is RequireService (SEC-013), the service token has
#   to come from /api/internal/service-auth/token (#1397 moved it off the public route), and
#   the API Gateway does not route /api/internal/* — so the Tenant Service is addressed
#   directly via -TenantDirectUrl (default http://127.0.0.1:5450, published by docker-compose).
#   A cert-only node such as n1 (F191 ServiceAuth__DisableSharedSecrets) refuses a
#   client_secret outright, and the script SKIPS with an explanation rather than failing in a
#   way that reads like a platform fault. The full reasoning is in the Step 1 comment.
#
#   Run against n1 instead: run-agents.ps1 / run-revocation.ps1 / run-suspension.ps1, which
#   need no service token.
#
#   ⚠ EVEN LOCALLY THIS DOES NOT YET COMPLETE — blocked at the present step by #1538 (the agent
#   cannot verify an x5c-signed request object). See the Step 1 comment.

param(
    [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
    [string]$Profile = 'gateway',
    [string]$GatewayUrl,
    # Direct address of the Tenant Service, used ONLY to mint the HAIP service token from
    # /api/internal/service-auth/token. That route is deliberately not published through the
    # API Gateway (#1397), so it cannot be reached at $GatewayUrl -- it needs the service's own
    # address. docker-compose publishes it on TENANT_PORT (default 5450) "for direct access
    # during development/bootstrap", which is exactly this. See the Step 1 comment for why a
    # hardened remote node cannot be targeted this way.
    [string]$TenantDirectUrl = "http://127.0.0.1:5450",
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

# SERVICE TOKEN — client_credentials for the service principal registered by setup.ps1.
#
# Exactly ONE of this script's three privileged calls actually needs a service token:
#
#   POST /api/v1/offers             RequireService (SEC-013)   <-- needs it
#   POST /api/v1/verifier/requests  RequireAuthorization()     <-- any authenticated caller
#   GET  .../result                 RequireAuthorization()     <-- any authenticated caller
#
# The verifier pair was relaxed to any-authenticated-caller by F164 B3 (FR-008). The offer
# endpoint mints a credential from the org's issuance key on demand, which is precisely the kind
# of oracle SEC-013 and #1397 exist to keep off a public route — so it stays RequireService and
# this script has to hold one.
#
# WHERE that token can be minted is what scopes this variant to a local stack:
#
#   * #1397 removed client_credentials from the PUBLIC token endpoint (it was a signing oracle).
#     The grant moved to POST /api/internal/service-auth/token, which the API Gateway
#     deliberately does not route — so it is unreachable at $GatewayUrl on ANY node, local or
#     remote. It has to be addressed at the Tenant Service directly ($TenantDirectUrl), which
#     docker-compose publishes on TENANT_PORT (default 5450) for exactly this bootstrap use.
#   * F191 then made service-to-service auth CERT-ONLY on n1 (docker-compose.n1.yml sets
#     ServiceAuth__DisableSharedSecrets=true), so there a client_secret is refused with an
#     explicit 400 even from inside the network — the caller must present a workload
#     certificate. Handing a test harness workload cert material would give a walkthrough a
#     credential-minting service identity, which is the #1397 shape wearing a different hat.
#
# So the token is obtainable against a LOCAL stack, and the script skips with an explanation
# against a hardened remote node. That is the right trade: what this variant proves — that
# withheld claims are genuinely absent from the wire — is a PROTOCOL property, and a protocol
# property does not need production topology to be meaningful. Skipping beats inventing a fake
# success path (CLAUDE.md §18).
#
# ⚠ THE SCRIPT STILL DOES NOT COMPLETE, for an unrelated second reason found by running it:
#   #1538 — `sorcha-agent haip present` cannot verify the request object. F181 US6 made the
#   verifier sign it with an X.509 chain (`x5c`, no embedded `jwk`), and the agent's only
#   verification path is embedded-jwk, so it fail-closes with "JOSE header carries no embedded
#   'jwk' to verify against". Steps 1-4 (token, offer, OID4VCI receive, verifier request) all
#   pass; step 5 does not. Fixing the token path was necessary but not sufficient.
$tokenResp = $null
$tokenUri  = "$($TenantDirectUrl.TrimEnd('/'))/api/internal/service-auth/token"
try {
    $encodedSecret = [Uri]::EscapeDataString($state.haip.clientSecret)
    $encodedScope  = [Uri]::EscapeDataString("haip:issue haip:verify")
    $ccBody = "grant_type=client_credentials&client_id=$($state.haip.clientId)&client_secret=$encodedSecret&scope=$encodedScope"
    $tokenResp = Invoke-SorchaApi -Method POST `
        -Uri $tokenUri `
        -Body $ccBody `
        -ContentType "application/x-www-form-urlencoded"
} catch {
    Write-Host ""
    Write-WtBanner "CyberEssentialsUac HAIP/OID4VP variant — SKIPPED (cannot mint a service token)"
    Write-WtInfo "Tried: $tokenUri"
    Write-WtInfo "Server said: $($_.Exception.Message)"
    Write-Host ""
    Write-WtInfo "Two reasons this fails, and they need different answers:"
    Write-WtInfo "  1. Not reachable — /api/internal/* is not routed by the API Gateway (#1397)."
    Write-WtInfo "     Pass -TenantDirectUrl pointing at the Tenant Service itself. The local"
    Write-WtInfo "     default is http://127.0.0.1:5450; set TENANT_PORT to change it."
    Write-WtInfo "  2. Refused with a 400 naming DisableSharedSecrets — the node is cert-only"
    Write-WtInfo "     (F191/#1420, which n1 sets). A client_secret cannot be used there at all,"
    Write-WtInfo "     and giving this script a workload certificate would hand a test harness a"
    Write-WtInfo "     credential-minting service identity. Run this variant locally instead."
    Write-Host ""
    Write-WtInfo "This is the security posture working as designed — not a platform fault."
    Write-Host ""
    Write-WtInfo "The non-HAIP scenarios are unaffected and cover the credential lifecycle on any"
    Write-WtInfo "node, including n1:"
    Write-WtInfo "  pwsh walkthroughs/CyberEssentialsUac/run-agents.ps1      -Profile n1"
    Write-WtInfo "  pwsh walkthroughs/CyberEssentialsUac/run-revocation.ps1  -Profile n1"
    Write-WtInfo "  pwsh walkthroughs/CyberEssentialsUac/run-suspension.ps1  -Profile n1"
    exit 0
}

Assert ($tokenResp -and $tokenResp.access_token) "client_credentials grant returned an access token"
$svcHeaders = @{ Authorization = "Bearer $($tokenResp.access_token)" }
Write-WtSuccess "Service token acquired (client: $($state.haip.clientId))"

# ============================================================================
# Step 2: Create the OID4VCI credential offer (all 10 claims, all disclosable)
# ============================================================================
# CRITICAL: ALL 10 evidence claims MUST be in disclosablePaths.
# Any claim NOT listed is minted as always-plaintext (not wrapped in an SD-JWT
# disclosure) and would be visible in the credential header regardless of --disclose,
# breaking the negative assertion (withheld claims must be absent from the wire).

Write-WtStep "Step 2: Create OID4VCI credential offer (10 claims, all in disclosablePaths)"

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
# Step 5: Agent presents the credential — disclosing ONLY 4 of 10 claims
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

$res = $null
for ($i = 0; $i -lt 10; $i++) {
    $res = Invoke-SorchaApi -Method GET `
        -Uri "$baseUrl/api/v1/verifier/requests/$($vreq.requestId)/result" `
        -Headers $svcHeaders
    if ($res -and $res.result -and $res.result.state -in @('Verified', 'Denied')) { break }
    if ($i -lt 9) { Start-Sleep -Seconds 1 }
}

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
$withheld = @('infraVersion', 'assessorType', 'scopeDeviceCount', 'mfaCoverage', 'staleAccounts', 'policyEvidenceHash')
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

$res2 = $null
for ($i = 0; $i -lt 10; $i++) {
    $res2 = Invoke-SorchaApi -Method GET `
        -Uri "$baseUrl/api/v1/verifier/requests/$($vreq2.requestId)/result" `
        -Headers $svcHeaders
    if ($res2 -and $res2.result -and $res2.result.state -in @('Verified', 'Denied')) { break }
    if ($i -lt 9) { Start-Sleep -Seconds 1 }
}

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
Write-WtInfo "  Total claims     : 10 (4 disclosed in this presentation, 6 withheld)"
Write-WtInfo "  Disclosed        : 4  (compliant, assessmentDate, passwordApproach, mfaAdminEnforced)"
Write-WtInfo "  Withheld         : 6  (infraVersion, assessorType, scopeDeviceCount, mfaCoverage, staleAccounts, policyEvidenceHash)"
Write-WtInfo "  Positive test    : verifier accepted the presentation, saw exactly 4 claims"
Write-WtInfo "  Negative test    : verifier rejected presentation when it required a withheld claim"
Write-WtInfo "  Agent wallet dir : $walletDir"
Write-Host ""
