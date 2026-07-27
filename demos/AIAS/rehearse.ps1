# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AIAS Assured Identity demo (Feature 174 / M1, extended M2) — rehearsal / test hook (FR-011, SC-004).
#
# Two independent scenarios, selected by -Scenario:
#
#   -Scenario identity (default) — against the already-provisioned environment, runs ONE APPROVAL
#     and TWO REJECTIONS end to end on the Assured Identity register/blueprint:
#       approval    -> the autonomous Assure-ID agent approves; an AssuredIdentityCredential
#                      (offer) lands in the applicant's wallet, carrying the submitted portrait.
#       rejection   -> a bad postcode ("ZZ99 9ZZ") is rejected; the recorded decision is
#                      "rejected" with the on-brand reason and NO credential is issued.
#       unverified  -> an applicant who never verified their email is rejected by the
#                      email gate; NO credential is issued.
#
#   -Scenario cyber (M2) — against the SEPARATE Cyber register/blueprint (New-AiasOrg step 6a /
#     Publish-AiasBlueprint), runs FOUR paths that together prove the scored questionnaire produces
#     a real spread rather than a single top band, AND that the portrait hard-gate fires before any
#     scoring happens:
#       platinum    -> a perfect card (24/24) -> CyberLevelCredential delivered at Platinum,
#                      carrying the portrait mapped forward from the presented Assured Identity.
#       silver      -> the perfect card minus BOTH deliberate traps (-2 each, 24 -> 20) ->
#                      CyberLevelCredential delivered at Silver. THIS is the path that proves the
#                      design's central claim: the traps genuinely cost points and the questionnaire
#                      does not just hand everyone Platinum.
#       fail        -> a dishonest-but-consistent low-scoring card (0/24, below the Bronze floor
#                      of 12) -> NO credential; a durable inbox decision notice matches the
#                      'cyber-fail' catalogue entry.
#       no-portrait -> the SAME perfect card, but presenting an Assured Identity issued WITHOUT a
#                      portrait -> the agent hard-rejects before scoring; NO credential; the inbox
#                      decision notice matches the 'no-portrait' catalogue entry.
#     Each cyber path first mints (and, where required, portrait-strips) its own Assured Identity
#     credential on the IDENTITY register, then presents it into the Cyber register's questionnaire
#     action — the one new cross-register assumption M2 introduces (credential issued on one
#     register, presented into a workflow on another). The presentation is driven through the
#     ASYNC F111 Timebound Presentation Lifecycle (presentationSource: "SorchaWallet") — the same
#     mechanism the live demo exercises: action 1 submits with no inline credentialPresentations,
#     parks awaiting a presentation, this script builds + signs a vp_token as the holder (via the
#     server-custody sign-kb endpoint — #1195 Phase 2 — since the holder key never leaves server
#     custody) and posts it to the sorcha-wallet callback, exactly as Sorcha.Wallet.Pwa's
#     Present.razor would. This deliberately does NOT take the synchronous internal-verifier
#     shortcut (inline credentialPresentations) that TradeFinance's walkthrough uses — that path
#     never seals a PresentationOutcome transaction, so /presentedCredential would never populate
#     and every cyber submission would hard-reject no-portrait regardless of the actual card.
#
# Submission is driven via the gateway HTTP API exactly like the proven
# walkthroughs/AssuredIdentity/run-phase1-identity.ps1 (instance create -> Action 1
# submit with holder keys); the agent (not this script) performs Action 2.
#
# Exit 0 on success, non-zero on any assertion failure.
#
# Usage:
#   ./demos/AIAS/rehearse.ps1                       # identity scenario, Docker
#   ./demos/AIAS/rehearse.ps1 -Target n1
#   ./demos/AIAS/rehearse.ps1 -Scenario cyber        # cyber scenario, Docker
#   ./demos/AIAS/rehearse.ps1 -Scenario cyber -Target n1
[CmdletBinding()]
param(
    [ValidateSet('docker', 'n1')][string]$Target = 'docker',
    [ValidateSet('identity', 'cyber')][string]$Scenario = 'identity',
    [string]$StateFile = (Join-Path $PSScriptRoot "state.json"),
    [int]$DecisionTimeoutSeconds = 60,
    # The first credential delivery on a freshly-genesis'd node (cold register seal + wallet sync)
    # can take longer than a warm node — 120s keeps the happy path reliable right after a re-genesis.
    # M2: the cyber-scenario credential-delivery assertions (Test-CyberCredentialDelivered) share
    # this SAME parameter rather than a fresh, shorter one — that mismatch is exactly the class of
    # bug #1239 fixed on the identity path (a correct-but-slow mint reading as a script failure).
    [int]$DeliveryTimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot "AiasDemo.psm1") -Force -DisableNameChecking
# SorchaWalkthrough LAST: AiasDemo nested-imports it with -Force, which evicts it from the caller's
# scope, so re-import it here to win the script-scoped copy (Write-Wt* cmdlets). Same fix as run-demo.
Import-Module (Join-Path $PSScriptRoot "../../walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1") -Force -DisableNameChecking

# The demo's generic lib helpers (Read-DemoState, Get-DemoApiBase, Import-DemoSecrets,
# Connect-DemoNodeAdmin) are dot-sourced by AiasDemo into ITS module scope and are NOT re-exported,
# so this script's own script-level calls to them fail with "not recognized". Dot-source them here
# too (same units AiasDemo uses) so rehearse resolves them in its own scope.
$script:AssuredLib = Join-Path $PSScriptRoot "../AssuredIdentity/lib"
. (Join-Path $script:AssuredLib "Common.ps1")     # Get-DemoApiBase
. (Join-Path $script:AssuredLib "DemoState.ps1")  # Read/Write/Merge-DemoState
. (Join-Path $script:AssuredLib "Auth.ps1")       # Import-DemoSecrets, Connect-DemoNodeAdmin

$state = Read-DemoState -Path $StateFile
if (-not $state) { Write-WtFail "No state.json — run ./run-demo.ps1 -Target $Target first."; exit 2 }

$gateway = $state.gateway
$api     = Get-DemoApiBase -Gateway $gateway
$failures = @()

# M2: the cyber questionnaire lives on its OWN register/blueprint (New-AiasOrg step 6a /
# Publish-AiasBlueprint's 'cyberLevel' spec) — resolve both up front, indexed PSObject.Properties['x']
# access throughout (NOT `.Properties.Name -contains`) so a state.json written before M2 (or before
# Initialize-AiasDemo has run since) degrades to a clear, actionable failure instead of a
# Set-StrictMode crash on a missing property.
$cyberRegisterId  = $null
$cyberBlueprintId = $null
if ($Scenario -eq 'cyber') {
    $crProp = $state.PSObject.Properties['cyberRegisterId']
    if ($crProp) { $cyberRegisterId = $crProp.Value }
    $bidsProp = $state.PSObject.Properties['blueprintIds']
    if ($bidsProp -and $bidsProp.Value) {
        $clProp = $bidsProp.Value.PSObject.Properties['cyberLevel']
        if ($clProp) { $cyberBlueprintId = $clProp.Value }
    }
    if (-not $cyberRegisterId -or -not $cyberBlueprintId) {
        Write-WtFail "state.json has no cyber register/blueprint recorded — run Initialize-AiasDemo (New-AiasOrg + Publish-AiasBlueprint) first, then re-run rehearse.ps1 -Scenario cyber."
        exit 2
    }
}

# ---------------------------------------------------------------------------
# Cyber answer fixtures (M2). Answer keys MUST match the enum values in
# blueprints/aias-cyber-level.template.json verbatim, and score against agent/cyber.checks.json —
# see the file's own $comment. Point totals are stated in comments so a retune that breaks a band
# is obvious at the call site.
# ---------------------------------------------------------------------------

# 24 -> Platinum. Every top answer, both sliders at zero.
$script:CyberAnswersPerfect = @{
    passwordStorage          = "A password manager"
    emailSecondStep          = "Yes — an app or a hardware key"
    passwordChangeHabit      = "Only when I think one's been exposed"
    phoneUpdates             = "It installs automatically"
    suspiciousEmail          = "Ignore the email and open the bank's app myself"
    laptopLoss               = "Nothing — it backs up automatically"
    sharedPasswordCount      = 0
    streamingPasswordSharers = 0
}

# 20 -> Silver. Perfect card minus BOTH traps (-2 each): the confident-but-wrong answers. This is
# the path that proves the design's central claim — the spread is real and both traps bite.
$script:CyberAnswersTrapped = $script:CyberAnswersPerfect.Clone()
$script:CyberAnswersTrapped.passwordChangeHabit = "Every 30 days, like clockwork"
$script:CyberAnswersTrapped.suspiciousEmail     = "Check the sender address carefully, then decide"

# 0 -> Fail, no credential (well below the 12-point Bronze floor).
$script:CyberAnswersDire = @{
    passwordStorage          = "The same one everywhere, and hope"
    emailSecondStep          = "No, just the password"
    passwordChangeHabit      = "Change them?"
    phoneUpdates             = "There's a red badge I've been ignoring since spring"
    suspiciousEmail          = "Click it — it looked legitimate"
    laptopLoss               = "Everything. Please don't say that."
    sharedPasswordCount      = 10
    streamingPasswordSharers = 10
}

# ---------------------------------------------------------------------------
# Helpers (local to the rehearsal — submission + decision-read, gateway HTTP API)
# ---------------------------------------------------------------------------

function New-RehearsalApplicant {
    param([string]$Tag, [switch]$SkipEmailVerification)
    # Anonymous signup on the public org + a wallet so the credential can be delivered.
    $email = "aias-rehearse-$Tag-$([guid]::NewGuid().ToString('N').Substring(0,8))@example.test"
    $pw = "Rehearse_Pass_2026!"
    Register-SorchaPublicUser -TenantUrl $api -Email $email -Password $pw -DisplayName "Rehearsal $Tag" | Out-Null
    $publicOrgId = "00000000-0000-0000-0000-000000000002"

    # Verify the email so the agent's emailVerified gate passes — UNLESS this is the F183
    # unverified-applicant rehearsal, which must reach the email-gate reject. The returned
    # EmailVerified flag drives what the submission carries (mirrors the web client's
    # x-claim-source stamp of the real email_verified JWT claim).
    $emailVerified = $false
    if (-not $SkipEmailVerification) {
        $secrets = Import-DemoSecrets
        $node = [pscustomobject]@{ id = $state.target; adminEmail = 'admin@sorcha.local'; gateway = $gateway }
        $admin = Connect-DemoNodeAdmin -Node $node -Secrets $secrets
        $pu = Invoke-SorchaApi -Method GET -Uri "$api/organizations/$publicOrgId/users?includeInactive=true" -Headers $admin.Headers
        $u = $pu.users | Where-Object { $_.email -eq $email } | Select-Object -First 1
        if ($u) {
            $null = Confirm-SorchaUserEmail -TenantUrl $api -OrganizationId $publicOrgId -UserId $u.id -Headers $admin.Headers
            $emailVerified = $true
        }
    }

    # Login succeeds even for an unverified user (the token just carries email_verified=false).
    $session = Connect-SorchaUser -TenantUrl $api -Email $email -Password $pw -OrganizationId $publicOrgId
    $wallet = New-SorchaWallet -WalletUrl $api -Name "Rehearsal $Tag Wallet" -Headers $session.Headers -FetchPublicKey
    return [pscustomobject]@{
        Email = $email; Session = $session; Wallet = $wallet; PublicOrgId = $publicOrgId; EmailVerified = $emailVerified
    }
}

function Submit-RehearsalApplication {
    param(
        [Parameter(Mandatory)][object]$Applicant,
        [Parameter(Mandatory)][string]$Postcode,
        [switch]$WithPortrait
    )
    $instance = Invoke-SorchaApi -Method POST `
        -Uri "$api/instances/" `
        -Body @{
            blueprintId = $state.blueprintId
            registerId  = $state.registerId
            tenantId    = $Applicant.PublicOrgId
            metadata    = @{ source = "rehearsal"; demo = "AIAS" }
        } `
        -Headers $Applicant.Session.Headers
    $instanceId = $instance.id

    $payload = @{
        name = @{ givenName = "Ada"; middleName = ""; familyName = "Rehearsal"; fullName = "Ada Rehearsal" }
        dob  = @{ dateOfBirth = "1990-01-01" }
        email = @{ email = $Applicant.Email }
        address = @{
            line1 = "1 Demo Street"; town = "Testington"; region = "Testshire"
            postcode = $Postcode; country = "GB"
        }
    }
    # F183: carry the applicant's REAL email-verified status, exactly as the web client's
    # x-claim-source seeder stamps it from the email_verified JWT claim — NOT a hardcoded true.
    # Verified → emailVerified:true; unverified → key omitted (the absent-on-the-wire shape the
    # agent's email gate rejects, which the old hardcoded true masked).
    if ($Applicant.EmailVerified) { $payload.emailVerified = $true }
    if ($WithPortrait) {
        # Real, decodable 240x320 JPEG fixture (fixtures/generate-sample-portrait.py) — a flat
        # background with a synthetic geometric head-and-shoulders silhouette, well under the
        # F107 ~27KB base64 gate. Three of the four cyber paths need a portrait-bearing
        # credential and the fourth needs a portrait-less one (Test-CyberCredentialDelivered /
        # the no-portrait hard-gate), so a missing or oversized fixture must fail loudly here
        # rather than silently degrading -WithPortrait into a no-portrait submission that would
        # look exactly like a genuine agent rejection. Unlike run-phase1-identity.ps1's
        # warn-and-continue on a missing sample, this is fail-fast by design.
        $samplePortraitPath = Join-Path $PSScriptRoot "fixtures/sample-portrait.jpg"
        if (-not (Test-Path $samplePortraitPath)) {
            Write-WtFail "Portrait fixture not found at $samplePortraitPath — cannot submit -WithPortrait. Regenerate with: python demos/AIAS/fixtures/generate-sample-portrait.py"
            exit 2
        }
        $bytes = [System.IO.File]::ReadAllBytes($samplePortraitPath)
        $base64 = [Convert]::ToBase64String($bytes)
        if ($base64.Length -gt 27000) {
            Write-WtFail "sample-portrait.jpg is $($base64.Length) base64 chars — over the F107 ~27KB gate. Regenerate a smaller fixture (fixtures/generate-sample-portrait.py) before rehearsing."
            exit 2
        }
        $payload.portrait = @{ tokenImageBase64 = $base64 }
    }

    # Carry holder keys so the issued credential can be bound + delivered (F137).
    $holderKeys = Invoke-SorchaApi -Method GET -Uri "$api/v1/wallet/holder-keys" -Headers $Applicant.Session.Headers
    $payload.holderKeys = @{
        holderJwk           = $holderKeys.holderJwk
        encryptionPublicKey = $holderKeys.encryptionPublicKey
        algorithm           = $holderKeys.algorithm
    }

    $null = Invoke-SorchaAction `
        -BlueprintUrl $api -InstanceId $instanceId -ActionId "1" `
        -BlueprintId $state.blueprintId -SenderWallet $Applicant.Wallet.Address `
        -RegisterId $state.registerId -Token $Applicant.Session.Token `
        -PayloadData $payload -WaitForSeal
    return $instanceId
}

function Wait-AiasDecision {
    # Poll the instance until the autonomous agent's Action 2 decision folds in.
    # Returns the decision string ('approved'|'rejected') or $null on timeout.
    param([Parameter(Mandatory)][string]$InstanceId, [Parameter(Mandatory)][hashtable]$Headers)
    $deadline = (Get-Date).AddSeconds($DecisionTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $inst = Invoke-SorchaApi -Method GET -Uri "$api/instances/$InstanceId" -Headers $Headers

            $currentActions = @()
            if ($inst.PSObject.Properties.Name -contains 'currentActionIds') { $currentActions = @($inst.currentActionIds) }

            # Approval routes to the citizen Claim action (Action 3); the instance parks there awaiting
            # the claim, so a pending Action 3 is the unambiguous approved signal.
            if ($currentActions -contains 3 -or $currentActions -contains '3') { return 'approved' }

            # Explicit decision payload, when the projection surfaces it (disclosed to the applicant).
            if ($inst.PSObject.Properties.Name -contains 'data' -and $inst.data -and
                ($inst.data.PSObject.Properties.Name -contains 'decision')) {
                $d = [string]$inst.data.decision
                if ($d) { return $d }
            }

            # Rejection routes to a terminal route (nextActionIds: []). Under the Feature-145
            # ledger-derived projection that folds as a COMPLETED instance with no current actions and
            # the claim action (3) never reached — distinct from an approval, which parks at Action 3.
            # (InstanceState: 1 = Completed, 2 = Rejected.) accumulatedData is not surfaced on the
            # projection, so the terminal shape — not a decision string — is the reliable reject signal.
            $stateVal = if ($inst.PSObject.Properties.Name -contains 'state') { "$($inst.state)" } else { '' }
            $terminal = ($stateVal -match '^(1|2)$') -or ($stateVal -match 'complete|reject')
            if ($terminal -and $currentActions.Count -eq 0) { return 'rejected' }
        } catch { }
        Start-Sleep -Seconds 3
    }
    return $null
}

function Test-CredentialDelivered {
    param([Parameter(Mandatory)][object]$Applicant)
    $deadline = (Get-Date).AddSeconds($DeliveryTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $snap = Invoke-SorchaApi -Method GET -Uri "$api/v1/wallet/credentials" -Headers $Applicant.Session.Headers
            if ($snap.credentials -and $snap.credentials.Count -gt 0) {
                # Match the AssuredIdentity credential across both the pre- and post-VCT-decoupling
                # (#1187) shapes: the vct is now the URI ".../assured-identity/v1" (hyphenated), not
                # the bare "AssuredIdentityCredential" the old matcher looked for.
                #
                # C3 fix (AIAS M2 review): CachedCredentialPayload (the WIRE shape of
                # GET /api/v1/wallet/credentials — src/Common/Sorcha.CitizenWallet.Abstractions/
                # Models/CachedCredentialPayload.cs) carries only id/vct/jwt/issuerDid/issuedAt/
                # expiresAt/statusListUri/statusListIndex/displayMeta — there is NO credentialType
                # or displayLabel property. Under this script's Set-StrictMode -Version Latest,
                # referencing $_.credentialType / $_.displayLabel throws PropertyNotFoundException.
                # It previously went unnoticed here only because -or short-circuits on the first
                # true vct match; match on vct alone.
                $hit = $snap.credentials | Where-Object {
                    $_.vct -match "assured-identity" -or $_.vct -match "AssuredIdentity"
                } | Select-Object -First 1
                if ($hit) { return $hit }
            }
        } catch { }
        Start-Sleep -Seconds 2
    }
    return $null
}

# ---------------------------------------------------------------------------
# Cyber (M2) helpers
# ---------------------------------------------------------------------------

function ConvertFrom-Base64Url {
    # Minimal base64url -> UTF8 string decoder (pads to a multiple of 4, swaps the URL-safe chars).
    param([Parameter(Mandatory)][string]$Value)
    $s = $Value.Replace('-', '+').Replace('_', '/')
    $pad = (4 - ($s.Length % 4)) % 4
    $s += ('=' * $pad)
    return [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($s))
}

function Get-SdJwtDisclosedClaims {
    # SD-JWT compact form: <issuer-signed JWT>~<disclosure>~<disclosure>~...~[<KB-JWT>]. Each
    # disclosure segment is base64url([salt, claimName, claimValue]) for a top-level/object claim.
    # This deliberately does NOT do full digest-verified reconstruction (compare
    # src/Apps/Sorcha.Wallet.Pwa/Services/SdJwtReader.cs, the production on-device reader, which
    # resolves nested `_sd` arrays at their correct depth against the JWT body) — every claim this
    # rehearsal asserts on (level, portrait, givenName, familyName) is a flat top-level scalar per
    # the AIAS credentialIssuanceConfig claimMappings, so a flat name->value fold of every disclosure
    # segment is sufficient. Not signature-checked here either: the platform already verified +
    # minted the credential (that's what Test-Cyber/AssuredIdentity...Delivered proves) — this just
    # reads the disclosed content back out of the wallet's own copy for the level/portrait asserts.
    param([Parameter(Mandatory)][string]$SdJwt)
    $claims = @{}
    $parts = $SdJwt -split '~'
    for ($i = 1; $i -lt $parts.Count; $i++) {
        $segment = $parts[$i]
        if ([string]::IsNullOrWhiteSpace($segment)) { continue }
        try {
            $decoded = ConvertFrom-Base64Url -Value $segment
            $arr = $decoded | ConvertFrom-Json
            if ($arr -and $arr.Count -ge 3) { $claims[[string]$arr[1]] = $arr[2] }
        } catch { }
    }
    return $claims
}

function New-CyberIdentityApplicant {
    # Full identity mint (submit -> autonomous Assure-ID agent approves -> credential auto-delivered,
    # SorchaLocalWallet targetAudience) on the ASSURED IDENTITY register — a fresh applicant + wallet
    # holding a claimable AssuredIdentityCredential, portrait-bearing unless -WithoutPortrait (the
    # ingredient path 4's no-portrait hard-reject needs).
    param([Parameter(Mandatory)][string]$Tag, [switch]$WithoutPortrait)
    $applicant = New-RehearsalApplicant -Tag $Tag
    $goodPostcode = "SW1A 1AA"   # present in fixtures/postcodes.offline.json + postcodes.io
    $instId = Submit-RehearsalApplication -Applicant $applicant -Postcode $goodPostcode -WithPortrait:(-not $WithoutPortrait)
    $decision = Wait-AiasDecision -InstanceId $instId -Headers $applicant.Session.Headers
    if ($decision -ne 'approved') { throw "identity mint for '$Tag' did not approve within ${DecisionTimeoutSeconds}s (got '$decision')." }
    $cred = Test-CredentialDelivered -Applicant $applicant
    if (-not $cred) { throw "AssuredIdentityCredential was not delivered to '$Tag' within ${DeliveryTimeoutSeconds}s." }
    return [pscustomobject]@{ Applicant = $applicant; Credential = $cred }
}

function ConvertTo-Base64Url {
    # The encode-side twin of ConvertFrom-Base64Url above.
    param([Parameter(Mandatory)][byte[]]$Bytes)
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Get-QueryStringValue {
    # Minimal query-string reader — avoids a System.Web dependency for one deep-link parse.
    param([Parameter(Mandatory)][string]$Url, [Parameter(Mandatory)][string]$Name)
    $queryStart = $Url.IndexOf('?')
    if ($queryStart -lt 0) { return $null }
    foreach ($pair in ($Url.Substring($queryStart + 1) -split '&')) {
        $kv = $pair -split '=', 2
        if ($kv.Count -eq 2 -and $kv[0] -eq $Name) { return [System.Uri]::UnescapeDataString($kv[1]) }
    }
    return $null
}

function Select-SdJwtDisclosures {
    # Filter an SD-JWT's raw disclosure segments down to the ones whose claim name is approved,
    # returning the ORIGINAL base64url segment strings (never decoded/re-encoded — the presentation
    # carries them verbatim). Mirrors Sorcha.Wallet.Pwa.Services.Presentation.PresentationEngine's
    # ReadDisclosureName + BuildSinglePresentationAsync disclosure filter. A claim with no matching
    # disclosure (e.g. 'portrait' on a portrait-less credential) is silently absent from the
    # result — that IS the no-portrait rehearsal path's point, not an error here.
    param([Parameter(Mandatory)][AllowEmptyCollection()][array]$AllDisclosures, [Parameter(Mandatory)][string[]]$ApprovedClaims)
    $approved = [System.Collections.Generic.HashSet[string]]::new([string[]]$ApprovedClaims, [System.StringComparer]::Ordinal)
    $selected = @()
    foreach ($seg in $AllDisclosures) {
        if ([string]::IsNullOrWhiteSpace($seg)) { continue }
        try {
            $decoded = ConvertFrom-Base64Url -Value $seg
            $arr = $decoded | ConvertFrom-Json
            if ($arr -and $arr.Count -ge 3 -and $approved.Contains([string]$arr[1])) { $selected += $seg }
        } catch { }
    }
    return $selected
}

function Get-JoseAlgorithmForWalletAlgorithm {
    # Mirrors Sorcha.Wallet.Service.Endpoints.CitizenWalletEndpoints.ToJoseAlgorithm exactly — the
    # sign-kb endpoint 400s when the KB-JWT header's declared alg doesn't match this mapping of the
    # holder key's real (wallet-style) algorithm name.
    param([Parameter(Mandatory)][string]$WalletAlgorithm)
    switch ($WalletAlgorithm.ToUpperInvariant()) {
        { $_ -in @('ED25519', 'EDDSA') } { return 'EdDSA' }
        { $_ -in @('ES256', 'P-256', 'P256', 'NIST-P256', 'NISTP256', 'ECDSA-P256', 'SECP256R1') } { return 'ES256' }
        default { throw "Holder key algorithm '$WalletAlgorithm' has no JOSE mapping (only Ed25519 and P-256 are supported)." }
    }
}

function Get-JwkThumbprint {
    # RFC 7638 thumbprint — mirrors PresentationEngine.ComputeJwkThumbprint's canonical member
    # order (OKP: crv/kty/x; EC: crv/kty/x/y). Used only as the KB-JWT header's informational
    # 'kid' claim — the verifier authenticates the KB-JWT signature directly against the
    # credential's own cnf.jwk, it does not re-derive or check this thumbprint.
    param([Parameter(Mandatory)]$Jwk)
    $kty = $Jwk.kty
    $crv = $Jwk.crv
    $x = $Jwk.x
    $canonical = if ($kty -eq 'OKP') {
        "{`"crv`":`"$crv`",`"kty`":`"$kty`",`"x`":`"$x`"}"
    } else {
        $y = $Jwk.y
        "{`"crv`":`"$crv`",`"kty`":`"$kty`",`"x`":`"$x`",`"y`":`"$y`"}"
    }
    return ConvertTo-Base64Url -Bytes ([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($canonical)))
}

function Complete-SorchaWalletPresentation {
    # Drive the F111 async Timebound Presentation Lifecycle's "sorcha-wallet" consumer to
    # completion, AS THE HOLDER — the same server-custody signing path
    # Sorcha.Wallet.Pwa.Pages.Present.razor uses (ConfirmAsync -> SignWithHolderKeyAsync ->
    # POST /api/v1/wallet/presentations/sign-kb), since the citizen's holder private key never
    # leaves server custody (#1195 Phase 2, Task 6a). Nothing here talks to a device; there is no
    # device to talk to for a server-custody root credential — this is precisely why the flow is
    # scriptable at all (contrast the retired HAIP-device walkthrough stub in
    # walkthroughs/AssuredIdentity/run-phase2-licence.ps1, which needed a physical wallet app).
    #
    # 1. GET the request-object JWT (anonymous) and decode nonce + client_id (aud) from it — the
    #    same fetch Sorcha.Wallet.Pwa.Services.Presentation.PresentationEngine.ParseAsync does.
    # 2. Export the applicant's own held credential's raw SD-JWT (issuer-signed + disclosures, no
    #    KB-JWT yet).
    # 3. Select disclosures for the approved claim names — EXPLICIT: no Sorcha wallet surface
    #    reads optionalClaims from the request today, so nothing discloses portrait unless this
    #    script does it deliberately.
    # 4. Build + sign a Key Binding JWT via the server-custody sign-kb endpoint.
    # 5. POST the assembled vp_token, wrapped in the OpenID4VP 1.0 object-keyed envelope, as
    #    application/x-www-form-urlencoded vp_token + state to
    #    POST /api/presentations/callbacks/sorcha-wallet/{id} (Sorcha.Blueprint.Service.Endpoints.
    #    PresentationEndpoints / Services.Implementation.SorchaWalletPresentationConsumer) — the
    #    EXACT wire shape Sorcha.Wallet.Pwa.Pages.Present.razor's ConfirmAsync/ConfirmMultiAsync
    #    posts to this same response_uri (#1310: the callback now accepts this form-encoded
    #    direct_post alongside the legacy JSON {vpToken} shape DeviceBindingService still uses for
    #    bind-to-device; this rehearsal deliberately switched from that JSON shape to THIS one so
    #    it certifies the wire path the phone actually uses, not a different mechanism).
    param(
        [Parameter(Mandatory)][object]$Applicant,
        [Parameter(Mandatory)][object]$Credential,
        [Parameter(Mandatory)][string[]]$ApprovedClaims,
        [Parameter(Mandatory)][string]$PresentationRequestId,
        [Parameter(Mandatory)][string]$AuthorizationRequestUri
    )

    $requestObjectUri = Get-QueryStringValue -Url $AuthorizationRequestUri -Name 'request_uri'
    if (-not $requestObjectUri) { throw "AuthorizationRequestUri carried no request_uri: $AuthorizationRequestUri" }

    # NOT -RawResponse: the endpoint serves 'application/oauth-authz-req+jwt'
    # (PresentationEndpoints.cs), a content type Invoke-WebRequest does not classify as text, so
    # -RawResponse's .Content comes back as a raw System.Byte[] rather than the JWT string — every
    # byte then becomes its own "part" under -split, and the old '-lt 2' guard could never fire
    # (byte-count is always >= 2). Invoke-RestMethod (the default, non-raw call) returns the body
    # as a plain string for this content type, so fetch it that way instead.
    $requestObjectJwt = Invoke-SorchaApi -Method GET -Uri $requestObjectUri
    if ($requestObjectJwt -isnot [string]) {
        throw "Request object at $requestObjectUri did not return a string body (got $($requestObjectJwt.GetType().FullName)) — cannot parse as a JWT."
    }
    $jwtParts = $requestObjectJwt -split '\.'
    if ($jwtParts.Count -ne 3) {
        throw "Request object at $requestObjectUri is not a 3-part JWT (got $($jwtParts.Count) segment(s) after splitting on '.'): '$requestObjectJwt'"
    }
    $requestPayload = (ConvertFrom-Base64Url -Value $jwtParts[1]) | ConvertFrom-Json
    $nonce = $requestPayload.nonce
    $clientId = $requestPayload.client_id
    if (-not $nonce -or -not $clientId) { throw "Request object payload for $PresentationRequestId is missing nonce/client_id." }

    # The DCQL credential-query id the response envelope must be keyed by (Present.razor reads the
    # SAME field off its parsed request — `_request.Query.Credentials[0].Id`). Single-ask requests
    # (this flow) declare exactly one; fall back to the "credential" constant
    # (SorchaWalletPresentationConsumer.ResolveDeclaredQuery's single-ask build) if the request
    # object is somehow missing dcql_query — belt-and-braces, never expected in practice.
    $queryId = $requestPayload.dcql_query.credentials[0].id
    if (-not $queryId) { $queryId = 'credential' }

    # Export the applicant's own held credential — mirrors the proven
    # walkthroughs/TradeFinance/run.ps1 present-a-held-credential export pattern.
    $walletAddr = $Applicant.Wallet.Address
    $exported = Invoke-SorchaApi -Method GET `
        -Uri "$api/v1/wallets/$walletAddr/credentials/$($Credential.id)/export" `
        -Headers $Applicant.Session.Headers
    $rawToken = if ($exported.rawToken) { $exported.rawToken } elseif ($exported.sdJwt) { $exported.sdJwt } else { $exported.token }
    if (-not $rawToken) { throw "credential export for $($Credential.id) returned no raw token." }

    $segments = $rawToken -split '~'
    $credentialJwt = $segments[0]
    $allDisclosures = if ($segments.Count -gt 1) { @($segments[1..($segments.Count - 1)] | Where-Object { $_ }) } else { @() }

    $selected = Select-SdJwtDisclosures -AllDisclosures $allDisclosures -ApprovedClaims $ApprovedClaims
    if ($selected.Count -eq 0) {
        throw "None of the approved claims ($($ApprovedClaims -join ', ')) matched a disclosure on the exported credential for $($Credential.id)."
    }

    # RFC 9901 sd_hash — SHA-256 over the to-be-presented hashable prefix (credentialJwt~sel1~..~selN~).
    # Mirrors PresentationEngine.BuildSinglePresentationAsync exactly.
    $hashable = $credentialJwt
    foreach ($seg in $selected) { $hashable += "~$seg" }
    $hashable += "~"
    $sdHash = ConvertTo-Base64Url -Bytes ([System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::ASCII.GetBytes($hashable)))

    # Holder key — server custody (#1195 Phase 2). This credential was minted bound to the SAME
    # holder key (holderKeySourceField "/holderKeys/holderJwk" on the identity blueprint), so its
    # cnf.jwk resolves to this key and the sign-kb endpoint signs on the applicant's behalf.
    $holderKeys = Invoke-SorchaApi -Method GET -Uri "$api/v1/wallet/holder-keys" -Headers $Applicant.Session.Headers
    $joseAlg = Get-JoseAlgorithmForWalletAlgorithm -WalletAlgorithm $holderKeys.algorithm
    $kid = Get-JwkThumbprint -Jwk $holderKeys.holderJwk

    $header = [ordered]@{ alg = $joseAlg; typ = 'kb+jwt'; kid = $kid }
    $iat = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    # 120s KB-JWT lifetime — matches PresentationEngine.BuildSinglePresentationAsync (Feature 138 US5).
    $kbPayload = [ordered]@{ iat = $iat; exp = ($iat + 120); aud = $clientId; nonce = $nonce; sd_hash = $sdHash }

    $headerSeg = ConvertTo-Base64Url -Bytes ([System.Text.Encoding]::UTF8.GetBytes(($header | ConvertTo-Json -Compress)))
    $payloadSeg = ConvertTo-Base64Url -Bytes ([System.Text.Encoding]::UTF8.GetBytes(($kbPayload | ConvertTo-Json -Compress)))
    $signingInput = "$headerSeg.$payloadSeg"

    # Server-custody KB-JWT signing — POST /api/v1/wallet/presentations/sign-kb (#1195 Phase 2,
    # Task 6a; Sorcha.Wallet.Service.Endpoints.CitizenWalletEndpoints.SignKbJwt). The holder
    # private key never leaves server custody; this is the EXACT endpoint
    # Sorcha.Wallet.Pwa.Pages.Present.razor's SignWithHolderKeyAsync calls.
    $signResp = Invoke-SorchaApi -Method POST `
        -Uri "$api/v1/wallet/presentations/sign-kb" `
        -Body @{ signingInput = $signingInput } `
        -Headers $Applicant.Session.Headers

    if ($signResp.algorithm -ne $joseAlg) {
        throw "sign-kb returned algorithm '$($signResp.algorithm)' but the KB-JWT header declared '$joseAlg' — these must match or the KB-JWT will fail verification downstream."
    }
    $kbJwt = "$signingInput.$($signResp.signature)"
    $vpToken = $hashable + $kbJwt

    # Wrap in the OpenID4VP 1.0 object-keyed vp_token envelope — byte-shape-identical to what
    # Present.razor's DcqlVpToken wrapper builds ({ "<queryId>": ["<presentation>"] }) — then POST
    # as application/x-www-form-urlencoded vp_token + state, exactly like the PWA's direct_post.
    # Sorcha.Blueprint.Service.Endpoints.PresentationEndpoints unwraps this envelope server-side
    # (#1310) and hands the sorcha-wallet consumer the bare compact string it expects. Consumer-tier
    # auth: the applicant's own bearer token (RequireConsumerAudience).
    $envelopeHash = @{}
    $envelopeHash[$queryId] = @($vpToken)
    $vpTokenEnvelope = $envelopeHash | ConvertTo-Json -Compress -Depth 5
    $formBody = "vp_token=$([Uri]::EscapeDataString($vpTokenEnvelope))&state=$([Uri]::EscapeDataString($PresentationRequestId))"

    $callbackUri = "$api/presentations/callbacks/sorcha-wallet/$PresentationRequestId"
    $callbackResp = Invoke-SorchaApi -Method POST `
        -Uri $callbackUri `
        -Body $formBody `
        -ContentType 'application/x-www-form-urlencoded' `
        -Headers $Applicant.Session.Headers

    if ($callbackResp.kind -ne 'Success') {
        throw "Presentation callback for $PresentationRequestId returned kind='$($callbackResp.kind)' (expected 'Success')."
    }
    return $callbackResp
}

function Submit-CyberQuestionnaire {
    param(
        [Parameter(Mandatory)][object]$Applicant,
        [Parameter(Mandatory)][hashtable]$Answers,
        [Parameter(Mandatory)][object]$Credential
    )

    # Fresh clone per applicant — $Answers is one of the shared script-scoped fixture hashtables
    # (CyberAnswersPerfect/Trapped/Dire), reused across every rehearsal path; mutating it in place
    # with THIS applicant's holderKeys would leak into the next path's submission.
    $payload = $Answers.Clone()

    # Action 1's holderKeys field (format sorcha-holder-key, x-holder-key.required:true — commit
    # 2cbf3347) — same pattern Submit-RehearsalApplication already uses for the identity blueprint.
    $holderKeys = Invoke-SorchaApi -Method GET -Uri "$api/v1/wallet/holder-keys" -Headers $Applicant.Session.Headers
    $payload.holderKeys = @{
        holderJwk           = $holderKeys.holderJwk
        encryptionPublicKey = $holderKeys.encryptionPublicKey
        algorithm           = $holderKeys.algorithm
    }

    $instance = Invoke-SorchaApi -Method POST `
        -Uri "$api/instances/" `
        -Body @{
            blueprintId = $cyberBlueprintId
            registerId  = $cyberRegisterId
            tenantId    = $Applicant.PublicOrgId
            metadata    = @{ source = "rehearsal"; demo = "AIAS-Cyber" }
        } `
        -Headers $Applicant.Session.Headers
    $instanceId = $instance.id

    # Action 1 carries a SorchaWallet credentialRequirements gate — submit WITHOUT
    # credentialPresentations so ActionExecutionService takes the ASYNC F111 branch (the
    # PresentationLifecycleService.InitiateAsync call gated on
    # CredentialRequirementModel.PresentationSource == SorchaWallet) instead of the synchronous
    # internal verifier. The response parks (IsComplete=false, AwaitingPresentation=true) and
    # carries the presentation request the citizen must now satisfy.
    $initResponse = Invoke-SorchaAction `
        -BlueprintUrl $api -InstanceId $instanceId -ActionId "1" `
        -BlueprintId $cyberBlueprintId -SenderWallet $Applicant.Wallet.Address `
        -RegisterId $cyberRegisterId -Token $Applicant.Session.Token `
        -PayloadData $payload -WaitForSeal

    $awaiting = ($initResponse.PSObject.Properties.Name -contains 'awaitingPresentation') -and $initResponse.awaitingPresentation
    if (-not $awaiting) {
        throw "Cyber action 1 (instance=$instanceId) did not enter the async presentation lifecycle " +
              "(awaitingPresentation was not true) — check the blueprint's credentialRequirements.presentationSource is 'SorchaWallet'."
    }
    $presentationRequestId = $initResponse.presentationRequest.requestId
    $authorizationRequestUri = $initResponse.presentationRequest.presentationRequestUri

    $null = Complete-SorchaWalletPresentation -Applicant $Applicant -Credential $Credential `
        -ApprovedClaims @('givenName', 'familyName', 'portrait') `
        -PresentationRequestId $presentationRequestId -AuthorizationRequestUri $authorizationRequestUri

    return $instanceId
}

function Wait-CyberOutcome {
    # Poll the instance until the cyber agent's Action 2 folds in. Unlike the identity blueprint,
    # BOTH cyber routes (approved-terminal, rejected-terminal) are terminal (nextActionIds: []) — there
    # is no Action 3 to park at, so a bare terminal projection with no surfaced 'decision' field just
    # means Action 2 sealed. Callers distinguish approved vs. rejected via credential delivery
    # (Test-CyberCredentialDelivered), not via this function's return value alone.
    param([Parameter(Mandatory)][string]$InstanceId, [Parameter(Mandatory)][hashtable]$Headers)
    $deadline = (Get-Date).AddSeconds($DecisionTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $inst = Invoke-SorchaApi -Method GET -Uri "$api/instances/$InstanceId" -Headers $Headers

            $currentActions = @()
            if ($inst.PSObject.Properties.Name -contains 'currentActionIds') { $currentActions = @($inst.currentActionIds) }

            if ($inst.PSObject.Properties.Name -contains 'data' -and $inst.data -and
                ($inst.data.PSObject.Properties.Name -contains 'decision')) {
                $d = [string]$inst.data.decision
                if ($d) { return $d }
            }

            $stateVal = if ($inst.PSObject.Properties.Name -contains 'state') { "$($inst.state)" } else { '' }
            $terminal = ($stateVal -match '^(1|2)$') -or ($stateVal -match 'complete|reject')
            if ($terminal -and $currentActions.Count -eq 0) { return 'terminal' }
        } catch { }
        Start-Sleep -Seconds 3
    }
    return $null
}

function Test-CyberCredentialDelivered {
    param([Parameter(Mandatory)][object]$Applicant)
    $deadline = (Get-Date).AddSeconds($DeliveryTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $snap = Invoke-SorchaApi -Method GET -Uri "$api/v1/wallet/credentials" -Headers $Applicant.Session.Headers
            if ($snap.credentials -and $snap.credentials.Count -gt 0) {
                # C3 fix (AIAS M2 review): CachedCredentialPayload carries only id/vct/jwt/issuerDid/
                # issuedAt/expiresAt/statusListUri/statusListIndex/displayMeta — no credentialType or
                # displayLabel property. Under Set-StrictMode -Version Latest, referencing either
                # throws PropertyNotFoundException on every credential — and because the citizen
                # always holds the Assured Identity credential too (issued FIRST), that throw aborted
                # this pipeline before it ever reached the cyber credential. Match on vct alone.
                $hit = $snap.credentials | Where-Object {
                    $_.vct -match "cyber-level" -or $_.vct -match "CyberLevel"
                } | Select-Object -First 1
                if ($hit) {
                    $claims = if ($hit.jwt) { Get-SdJwtDisclosedClaims -SdJwt $hit.jwt } else { @{} }
                    return [pscustomobject]@{ Credential = $hit; Claims = $claims }
                }
            }
        } catch { }
        Start-Sleep -Seconds 2
    }
    return $null
}

function Get-CyberDecisionNotice {
    # Polls the citizen's durable inbox (F118/F184 bell drawer) for an entry whose Summary matches
    # the reject route's x-decision-notice catalogue wording (blueprints/aias-cyber-level.template.json).
    param(
        [Parameter(Mandatory)][object]$Applicant,
        [Parameter(Mandatory)][string]$ExpectedSnippet,
        # T9(b) fix (AIAS M2 review): was a bare hardcoded 30 — the only timeout in the cyber
        # scenario not derived from a script parameter. Default to the script's own
        # -DecisionTimeoutSeconds so a slow node gets the same headroom every other decision-wait
        # in this script already gets.
        [int]$TimeoutSeconds = $DecisionTimeoutSeconds
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $resp = Invoke-SorchaApi -Method GET -Uri "$api/me/inbox?pageSize=20" -Headers $Applicant.Session.Headers
            $entries = @()
            if ($resp.PSObject.Properties.Name -contains 'entries') { $entries = @($resp.entries) }
            $hit = $entries | Where-Object { $_.summary -and ($_.summary -match [regex]::Escape($ExpectedSnippet)) } | Select-Object -First 1
            if ($hit) { return $hit }
        } catch { }
        Start-Sleep -Seconds 2
    }
    return $null
}

function Get-CyberDecisionReasonText {
    # C-B fix (M2 delta review): read the reject route's x-decision-notice.reasons catalogue
    # straight from the blueprint template rather than hardcoding a second copy of the wording here.
    # The inbox Summary IS this text verbatim (BlueprintInboxWriter.cs sets Summary: reason), so a
    # rehearsal assertion that hardcodes its own copy is the exact fixture/source-of-truth coupling
    # that broke once already (commit 2cbf3347 reworded the 'cyber-fail' reason and this script's
    # hardcoded snippet silently stopped matching). Walks every action's routes looking for the
    # x-decision-notice block rather than assuming a fixed action/route index, so a routes reorder
    # doesn't break this lookup.
    param(
        [Parameter(Mandatory)][string]$BlueprintPath,
        [Parameter(Mandatory)][string]$ReasonCode
    )
    if (-not (Test-Path -LiteralPath $BlueprintPath)) {
        throw "Get-CyberDecisionReasonText: blueprint template not found at '$BlueprintPath'."
    }
    $bp = Get-Content -LiteralPath $BlueprintPath -Raw | ConvertFrom-Json
    foreach ($action in $bp.template.actions) {
        if ($action.PSObject.Properties.Name -notcontains 'routes') { continue }
        foreach ($route in $action.routes) {
            if ($route.PSObject.Properties.Name -notcontains 'x-decision-notice') { continue }
            $notice = $route.'x-decision-notice'
            if ($notice.reasons -and ($notice.reasons.PSObject.Properties.Name -contains $ReasonCode)) {
                return [string]$notice.reasons.$ReasonCode
            }
        }
    }
    throw "Get-CyberDecisionReasonText: reasonCode '$ReasonCode' not found in any route's x-decision-notice.reasons in '$BlueprintPath' — blueprint/rehearsal drift."
}

function Assert-CyberFixtureAnswersMatchBlueprint {
    # Guard against fixture/blueprint drift (M2 review finding). The fixture answer strings above
    # ARE the scoring keys: agent/cyber.checks.json's 'scored-questionnaire' check (ExternalCheckFactory)
    # matches them against the presented value with StringComparer.Ordinal — case-sensitive,
    # codepoint-exact. A fixture that drifts from the blueprint's own enum (even by a single
    # letter's case) scores that field 0 SILENTLY: no exception, no warning, just a wrong total —
    # and this rehearsal would go on to assert a band that is no longer what the fixture claims.
    #
    # This reads the blueprint template's OWN enum arrays at runtime rather than comparing against
    # a second hardcoded list — a copy-vs-copy check proves nothing about drift from the real
    # source of truth. Runs ONLY for the cyber scenario, before any live API call, so a drift
    # aborts in milliseconds instead of burning minutes against a live deployment on assertions
    # that were never going to mean what they claim.
    param(
        [Parameter(Mandatory)][string]$BlueprintPath,
        [Parameter(Mandatory)][hashtable[]]$Fixtures
    )
    if (-not (Test-Path -LiteralPath $BlueprintPath)) {
        throw "Assert-CyberFixtureAnswersMatchBlueprint: blueprint template not found at '$BlueprintPath' — cannot verify fixture answers against the enum source of truth."
    }
    $bp = Get-Content -LiteralPath $BlueprintPath -Raw | ConvertFrom-Json
    $properties = $bp.template.actions[0].dataSchemas[0].properties

    # Only the enum-typed (Selection) fields are scoring keys under case-sensitive match.
    # sharedPasswordCount/streamingPasswordSharers are integer sliders, scored by range in
    # cyber.checks.json — not applicable here.
    $enumsByField = @{}
    foreach ($fieldName in $properties.PSObject.Properties.Name) {
        $prop = $properties.$fieldName
        if ($prop.PSObject.Properties.Name -contains 'enum' -and $prop.enum) {
            $enumsByField[$fieldName] = @($prop.enum)
        }
    }

    $ordinal = [System.StringComparison]::Ordinal
    $ordinalIgnoreCase = [System.StringComparison]::OrdinalIgnoreCase
    $mismatches = @()
    foreach ($fixture in $Fixtures) {
        foreach ($fieldName in $fixture.Keys) {
            $value = $fixture[$fieldName]
            if ($value -isnot [string]) { continue }   # slider fields — not enum-scored

            if (-not $enumsByField.ContainsKey($fieldName)) {
                $mismatches += "field '$fieldName': fixture supplies a string answer ('$value') but the blueprint schema has no enum for this field at all — check the field name."
                continue
            }

            $candidates = $enumsByField[$fieldName]
            $exactHit = $false
            foreach ($candidate in $candidates) {
                if ([string]::Equals($candidate, $value, $ordinal)) { $exactHit = $true; break }
            }
            if ($exactHit) { continue }

            # Case-insensitive match, to name the fix precisely for a 9am operator.
            $closest = $candidates | Where-Object { [string]::Equals($_, $value, $ordinalIgnoreCase) } | Select-Object -First 1
            if ($closest) {
                $mismatches += "field '$fieldName': fixture answer '$value' does not match the blueprint enum verbatim (case/codepoint drift) — blueprint enum has '$closest'."
            } else {
                $mismatches += "field '$fieldName': fixture answer '$value' does not match ANY blueprint enum value. Blueprint enum for this field: '$($candidates -join "' | '")'."
            }
        }
    }

    if ($mismatches.Count -gt 0) {
        throw "Cyber fixture/blueprint enum drift detected in rehearse.ps1 — the real scoring path (StringComparer.Ordinal) would NOT match these and would silently score 0:`n - $($mismatches -join "`n - ")"
    }
}

if ($Scenario -eq 'identity') {

# ---------------------------------------------------------------------------
# Rehearsal 1 — APPROVAL (existing postcode + portrait)
# ---------------------------------------------------------------------------
Write-WtBanner "AIAS rehearsal 1/3 — APPROVAL (verified email + existing postcode + portrait)"
try {
    $approveApplicant = New-RehearsalApplicant -Tag "approve"
    $goodPostcode = "SW1A 1AA"   # present in fixtures/postcodes.offline.json + postcodes.io
    $instId = Submit-RehearsalApplication -Applicant $approveApplicant -Postcode $goodPostcode -WithPortrait
    Write-WtInfo "submitted approval application (instance=$instId)"

    $decision = Wait-AiasDecision -InstanceId $instId -Headers $approveApplicant.Session.Headers
    if ($decision -ne 'approved') { $failures += "APPROVAL: expected decision 'approved' within ${DecisionTimeoutSeconds}s, got '$decision'." }
    else { Write-WtSuccess "agent approved the application" }

    if ($decision -eq 'approved') {
        $cred = Test-CredentialDelivered -Applicant $approveApplicant
        if (-not $cred) { $failures += "APPROVAL: no AssuredIdentityCredential delivered within ${DeliveryTimeoutSeconds}s." }
        else {
            Write-WtSuccess "AssuredIdentityCredential delivered (id=$($cred.id))"
            $hasPortrait = ($cred.PSObject.Properties.Name -contains 'portrait' -and $cred.portrait) -or `
                           ($cred.PSObject.Properties.Name -contains 'displayLabel')   # offer present
            if (-not $hasPortrait) { Write-WtWarn "APPROVAL: credential offer present but portrait claim not visible in the summary projection (verify on claim)." }
        }
    }
} catch {
    $failures += "APPROVAL: threw — $($_.Exception.Message)"
}

# ---------------------------------------------------------------------------
# Rehearsal 2 — REJECTION (non-existent postcode -> on-brand reason, no credential)
# ---------------------------------------------------------------------------
Write-WtBanner "AIAS rehearsal 2/3 — REJECTION (verified email, bad postcode 'ZZ99 9ZZ')"
try {
    $rejectApplicant = New-RehearsalApplicant -Tag "reject"
    $instId = Submit-RehearsalApplication -Applicant $rejectApplicant -Postcode "ZZ99 9ZZ"
    Write-WtInfo "submitted rejection application (instance=$instId)"

    $decision = Wait-AiasDecision -InstanceId $instId -Headers $rejectApplicant.Session.Headers
    if ($decision -ne 'rejected') { $failures += "REJECTION: expected decision 'rejected' within ${DecisionTimeoutSeconds}s, got '$decision'." }
    else { Write-WtSuccess "agent rejected the application (bad postcode)" }

    # No credential must be issued on rejection.
    $cred = Test-CredentialDelivered -Applicant $rejectApplicant
    if ($cred) { $failures += "REJECTION: a credential was issued ($($cred.id)) — none should be on rejection." }
    else { Write-WtSuccess "no credential issued (correct)" }

    # The on-brand reason should be on the recorded decision / surfaced to the applicant.
    try {
        $inst = Invoke-SorchaApi -Method GET -Uri "$api/instances/$instId" -Headers $rejectApplicant.Session.Headers
        $notes = $null
        if ($inst.PSObject.Properties.Name -contains 'data' -and $inst.data -and ($inst.data.PSObject.Properties.Name -contains 'verificationNotes')) {
            $notes = [string]$inst.data.verificationNotes
        }
        if ($notes -and $notes -match 'AIAS') { Write-WtSuccess "on-brand reason recorded: $notes" }
        else { Write-WtWarn "REJECTION: could not read the on-brand reason from the instance projection (rejection still recorded)." }
    } catch { Write-WtWarn "REJECTION: reason read transient error: $($_.Exception.Message)" }
} catch {
    $failures += "REJECTION: threw — $($_.Exception.Message)"
}

# ---------------------------------------------------------------------------
# Rehearsal 3 — REJECTION (unverified email -> email-gate reject, no credential) [F183]
# Real web-form shape: an unverified applicant's client stamps NO verified emailVerified,
# so the payload omits it and the agent's email gate rejects. This is the case the old
# hardcoded emailVerified=$true masked — a genuine web applicant could never get here.
# ---------------------------------------------------------------------------
Write-WtBanner "AIAS rehearsal 3/3 — REJECTION (unverified email; no emailVerified on the wire)"
try {
    $unverifiedApplicant = New-RehearsalApplicant -Tag "unverified" -SkipEmailVerification
    $goodPostcode = "SW1A 1AA"   # good postcode so the ONLY reject reason is the email gate
    $instId = Submit-RehearsalApplication -Applicant $unverifiedApplicant -Postcode $goodPostcode
    Write-WtInfo "submitted unverified-email application (instance=$instId)"

    $decision = Wait-AiasDecision -InstanceId $instId -Headers $unverifiedApplicant.Session.Headers
    if ($decision -ne 'rejected') { $failures += "UNVERIFIED: expected decision 'rejected' within ${DecisionTimeoutSeconds}s, got '$decision'." }
    else { Write-WtSuccess "agent rejected the application (unverified email)" }

    # No credential must be issued on an email-gate reject.
    $cred = Test-CredentialDelivered -Applicant $unverifiedApplicant
    if ($cred) { $failures += "UNVERIFIED: a credential was issued ($($cred.id)) — none should be on an email-gate reject." }
    else { Write-WtSuccess "no credential issued (correct)" }
} catch {
    $failures += "UNVERIFIED: threw — $($_.Exception.Message)"
}

}
elseif ($Scenario -eq 'cyber') {

$cyberBlueprintPath = Join-Path $PSScriptRoot "blueprints/aias-cyber-level.template.json"

# Fail fast, before any live API call: the fixture answer strings above must match the blueprint's
# enum values verbatim (see Assert-CyberFixtureAnswersMatchBlueprint for why this matters).
Assert-CyberFixtureAnswersMatchBlueprint `
    -BlueprintPath $cyberBlueprintPath `
    -Fixtures @($script:CyberAnswersPerfect, $script:CyberAnswersTrapped, $script:CyberAnswersDire)
Write-WtSuccess "cyber fixture answers verified verbatim (case-sensitive) against the blueprint enums"

# ---------------------------------------------------------------------------
# Cyber rehearsal 1/4 — PLATINUM (perfect card, portrait-bearing identity presented)
# ---------------------------------------------------------------------------
Write-WtBanner "AIAS-Cyber rehearsal 1/4 — PLATINUM (perfect answers, 24/24)"
try {
    $mint = New-CyberIdentityApplicant -Tag "cyber-platinum"
    $instId = Submit-CyberQuestionnaire -Applicant $mint.Applicant -Answers $script:CyberAnswersPerfect -Credential $mint.Credential
    Write-WtInfo "submitted cyber questionnaire (instance=$instId)"

    $outcome = Wait-CyberOutcome -InstanceId $instId -Headers $mint.Applicant.Session.Headers
    if (-not $outcome) { $failures += "CYBER PLATINUM: agent did not reach a terminal decision within ${DecisionTimeoutSeconds}s." }

    $delivered = Test-CyberCredentialDelivered -Applicant $mint.Applicant
    if (-not $delivered) {
        $failures += "CYBER PLATINUM: no CyberLevelCredential delivered within ${DeliveryTimeoutSeconds}s."
    } else {
        $level = [string]$delivered.Claims['level']
        if ($level -ne 'Platinum') { $failures += "CYBER PLATINUM: expected level 'Platinum' (24/24), got '$level'." }
        else { Write-WtSuccess "CyberLevelCredential delivered at Platinum" }
        $portrait = $delivered.Claims['portrait']
        if (-not $portrait) { $failures += "CYBER PLATINUM: delivered credential carries no portrait claim." }
        else { Write-WtSuccess "portrait claim present on the delivered CyberLevelCredential" }
    }
} catch {
    $failures += "CYBER PLATINUM: threw — $($_.Exception.Message)"
}

# ---------------------------------------------------------------------------
# Cyber rehearsal 2/4 — SILVER (both traps hit -> proves the spread is real)
# ---------------------------------------------------------------------------
Write-WtBanner "AIAS-Cyber rehearsal 2/4 — SILVER (perfect card minus both traps, 24 -> 20)"
try {
    $mint = New-CyberIdentityApplicant -Tag "cyber-silver"
    $instId = Submit-CyberQuestionnaire -Applicant $mint.Applicant -Answers $script:CyberAnswersTrapped -Credential $mint.Credential
    Write-WtInfo "submitted cyber questionnaire (instance=$instId)"

    $outcome = Wait-CyberOutcome -InstanceId $instId -Headers $mint.Applicant.Session.Headers
    if (-not $outcome) { $failures += "CYBER SILVER: agent did not reach a terminal decision within ${DecisionTimeoutSeconds}s." }

    $delivered = Test-CyberCredentialDelivered -Applicant $mint.Applicant
    if (-not $delivered) {
        $failures += "CYBER SILVER: no CyberLevelCredential delivered within ${DeliveryTimeoutSeconds}s."
    } else {
        $level = [string]$delivered.Claims['level']
        if ($level -ne 'Silver') { $failures += "CYBER SILVER: expected level 'Silver' (both traps cost 2 points each, 24 -> 20), got '$level' — the spread is not behaving as designed." }
        else { Write-WtSuccess "CyberLevelCredential delivered at Silver — both traps bit as designed; the spread is real" }
    }
} catch {
    $failures += "CYBER SILVER: threw — $($_.Exception.Message)"
}

# ---------------------------------------------------------------------------
# Cyber rehearsal 3/4 — FAIL (dishonest-but-consistent low score, no credential)
# ---------------------------------------------------------------------------
Write-WtBanner "AIAS-Cyber rehearsal 3/4 — FAIL (score 0/24, below the Bronze floor)"
try {
    $mint = New-CyberIdentityApplicant -Tag "cyber-fail"
    $instId = Submit-CyberQuestionnaire -Applicant $mint.Applicant -Answers $script:CyberAnswersDire -Credential $mint.Credential
    Write-WtInfo "submitted cyber questionnaire (instance=$instId)"

    $outcome = Wait-CyberOutcome -InstanceId $instId -Headers $mint.Applicant.Session.Headers
    if (-not $outcome) { $failures += "CYBER FAIL: agent did not reach a terminal decision within ${DecisionTimeoutSeconds}s." }

    $delivered = Test-CyberCredentialDelivered -Applicant $mint.Applicant
    if ($delivered) { $failures += "CYBER FAIL: a CyberLevelCredential was delivered — none should be issued below the Bronze floor." }
    else { Write-WtSuccess "no CyberLevelCredential issued (correct)" }

    $notice = Get-CyberDecisionNotice -Applicant $mint.Applicant -ExpectedSnippet (Get-CyberDecisionReasonText -BlueprintPath $cyberBlueprintPath -ReasonCode 'cyber-fail')
    if (-not $notice) { $failures += "CYBER FAIL: no inbox decision notice matched the 'cyber-fail' catalogue entry." }
    else { Write-WtSuccess "decision notice recorded: $($notice.summary)" }
} catch {
    $failures += "CYBER FAIL: threw — $($_.Exception.Message)"
}

# ---------------------------------------------------------------------------
# Cyber rehearsal 4/4 — NO PORTRAIT (perfect answers, hard-rejected before scoring)
# ---------------------------------------------------------------------------
Write-WtBanner "AIAS-Cyber rehearsal 4/4 — NO PORTRAIT (perfect answers, portrait-less identity presented)"
try {
    $mint = New-CyberIdentityApplicant -Tag "cyber-noportrait" -WithoutPortrait
    $instId = Submit-CyberQuestionnaire -Applicant $mint.Applicant -Answers $script:CyberAnswersPerfect -Credential $mint.Credential
    Write-WtInfo "submitted cyber questionnaire (instance=$instId)"

    $outcome = Wait-CyberOutcome -InstanceId $instId -Headers $mint.Applicant.Session.Headers
    if (-not $outcome) { $failures += "CYBER NO PORTRAIT: agent did not reach a terminal decision within ${DecisionTimeoutSeconds}s." }

    $delivered = Test-CyberCredentialDelivered -Applicant $mint.Applicant
    if ($delivered) { $failures += "CYBER NO PORTRAIT: a CyberLevelCredential was delivered — the no-portrait hard-reject must fire before scoring, even on a perfect card." }
    else { Write-WtSuccess "no CyberLevelCredential issued (correct — hard-rejected before scoring)" }

    $notice = Get-CyberDecisionNotice -Applicant $mint.Applicant -ExpectedSnippet (Get-CyberDecisionReasonText -BlueprintPath $cyberBlueprintPath -ReasonCode 'no-portrait')
    if (-not $notice) { $failures += "CYBER NO PORTRAIT: no inbox decision notice matched the 'no-portrait' catalogue entry." }
    else { Write-WtSuccess "decision notice recorded: $($notice.summary)" }
} catch {
    $failures += "CYBER NO PORTRAIT: threw — $($_.Exception.Message)"
}

}

# ---------------------------------------------------------------------------
# Verdict
# ---------------------------------------------------------------------------
if ($failures.Count -eq 0) {
    if ($Scenario -eq 'cyber') {
        Write-WtBanner "AIAS-Cyber rehearsal PASSED — Platinum and Silver both delivered (the spread is real), Fail and No-Portrait both blocked correctly."
    } else {
        Write-WtBanner "AIAS rehearsal PASSED — approval issued a credential; both rejections recorded with no credential."
    }
    exit 0
} else {
    Write-WtBanner "AIAS rehearsal FAILED"
    foreach ($f in $failures) { Write-WtFail $f }
    exit 1
}
