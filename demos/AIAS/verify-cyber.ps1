# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# AIAS M3 — drive the Cyber Level verify moment end to end, headlessly.
#
# Creates an OpenID4VP verifier request for the Cyber Level credential, answers it AS THE HOLDER
# via the shared scripted holder (lib/HolderPresentation.ps1 — the same server-custody sign-kb path
# the PWA's Present.razor uses), then reads the verifier's own result back and asserts what was
# actually disclosed.
#
# This is the design's §7.2 checkpoint list minus the QR pixels: a surface starts a session and gets
# a non-empty deep link, the holder resolves + consents + presents, the verifier reaches Verified
# with a vp_token, and the disclosed claims are exactly what the preset asked for.
#
# WHY IT IS SCRIPTABLE: an AIAS credential is issued SorchaLocalWallet — encrypted to the citizen
# wallet's key, holder private key in server custody — so there is no device in the loop. The
# `sorcha-agent haip present` CLI holder cannot substitute (it consumes an OpenID4VCI *offer* into a
# file wallet; no offer exists for a SorchaLocalWallet credential).
#
#   ./demos/AIAS/verify-cyber.ps1 -Target n1 -Email <citizen holding a Cyber Level credential>
#
# Exit 0 = the verify moment works.

[CmdletBinding()]
param(
    [ValidateSet('n1', 'docker')][string]$Target = 'n1',
    [Parameter(Mandatory)][string]$Email,
    [string]$Password = 'Rehearse_Pass_2026!',
    # The kiosk's own service principal. Its registered scope is `blueprints:read`; the verifier
    # endpoints gate on RequireService (token_type=service), NOT on a haip:* scope — asking for a
    # scope the principal does not hold is a 401, which looks exactly like "not provisioned".
    [string]$ServiceClientId = 'service-verifier',
    [string]$ServiceClientSecret = 'verifier-service-secret',
    [switch]$ShowJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot "../../walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1") -Force
. (Join-Path $PSScriptRoot "lib/HolderPresentation.ps1")

$statePath = Join-Path $PSScriptRoot "state.json"
if (-not (Test-Path $statePath)) { Write-WtFail "No state.json — run run-demo.ps1 first."; exit 2 }
$state = Get-Content $statePath -Raw | ConvertFrom-Json

$gateway = if ($Target -eq 'n1') { 'https://n1.sorcha.dev' } else { 'http://localhost' }
if ($state.PSObject.Properties['gateway'] -and $state.gateway) { $gateway = $state.gateway }
$api = "$gateway/api"

$CyberLevelVct = 'https://sorcha.dev/vc/cyber-level/v1'
# Mirrors the `cyber-level` builtin preset in DefaultPresetCatalogue (PR #1353) — level + portrait
# required, which is the design's "photo + selective level disclosure".
$RequiredClaims = @('level', 'portrait')

Write-WtBanner "AIAS M3 — verify the Cyber Level ($Target)"

# ---------------------------------------------------------------------------
# 1. Verifier side: service token + presentation request
# ---------------------------------------------------------------------------
Write-WtStep "1. Verifier creates a Cyber Level presentation request"

$ccBody = "grant_type=client_credentials&client_id=$([Uri]::EscapeDataString($ServiceClientId))" +
          "&client_secret=$([Uri]::EscapeDataString($ServiceClientSecret))&scope=blueprints%3Aread"
$tokenResp = Invoke-SorchaApi -Method POST -Uri "$api/service-auth/token" `
    -Body $ccBody -ContentType 'application/x-www-form-urlencoded'
if (-not $tokenResp.access_token) { Write-WtFail "client_credentials grant returned no token."; exit 1 }
$svcHeaders = @{ Authorization = "Bearer $($tokenResp.access_token)" }
Write-WtSuccess "Service token acquired ($ServiceClientId)"

$vreq = Invoke-SorchaApi -Method POST -Uri "$api/v1/verifier/requests" -Headers $svcHeaders -Body @{
    credentialType = $CyberLevelVct
    requiredClaims = $RequiredClaims
}
if (-not $vreq.requestId) { Write-WtFail "verifier request creation returned no requestId."; exit 1 }
if (-not $vreq.authorizationRequestUri) { Write-WtFail "verifier request carried no authorizationRequestUri (nothing to render as a QR)."; exit 1 }
Write-WtSuccess "Request $($vreq.requestId) created"
Write-WtInfo   "Deep link: $($vreq.authorizationRequestUri.Substring(0, [Math]::Min(90, $vreq.authorizationRequestUri.Length)))..."
if ($ShowJson) { $vreq | ConvertTo-Json -Depth 6 | Write-Host }

# ---------------------------------------------------------------------------
# 2. Holder side: the citizen and their credential
# ---------------------------------------------------------------------------
Write-WtStep "2. Holder signs in and locates the Cyber Level credential"

$publicOrgId = "00000000-0000-0000-0000-000000000002"
$session = Connect-SorchaUser -TenantUrl $api -Email $Email -Password $Password -OrganizationId $publicOrgId
# Set-StrictMode makes `$x.missingProp` throw outright, so `??` never gets a chance — probe the
# property bag instead (the same pattern rehearse.ps1 uses for optional state.json fields).
function Get-Prop { param($Obj, [string]$Name)
    if ($null -eq $Obj) { return $null }
    $p = $Obj.PSObject.Properties[$Name]
    if ($p) { return $p.Value }
    return $null
}

$wallets = Invoke-SorchaApi -Method GET -Uri "$api/v1/wallets" -Headers $session.Headers
$walletList = @((Get-Prop $wallets 'wallets') ?? $wallets)
$wallet = $walletList | Select-Object -First 1
if (-not $wallet) { Write-WtFail "$Email has no wallet."; exit 1 }
Write-WtSuccess "Wallet $($wallet.address)"

# ?status=All, NOT the default. AIAS delivers register-native credentials as OFFERS
# (Feature 106: inbound SorchaLocalWallet credentials land as PendingAcceptance), and the default
# listing is Active-only — so the freshly-issued credential is invisible to a plain GET and this
# reads as "holds no Cyber Level credential" when they demonstrably do.
$creds = Invoke-SorchaApi -Method GET -Uri "$api/v1/wallets/$($wallet.address)/credentials?status=All" -Headers $session.Headers
$credList = @((Get-Prop $creds 'credentials') ?? $creds)
$cyber = $credList | Where-Object {
    (Get-Prop $_ 'vct') -eq $CyberLevelVct -or (Get-Prop $_ 'type') -eq $CyberLevelVct
} | Select-Object -First 1
if (-not $cyber) {
    $held = @($credList | ForEach-Object { (Get-Prop $_ 'type') ?? (Get-Prop $_ 'vct') })
    Write-WtFail "$Email holds no Cyber Level credential ($CyberLevelVct). Held: $($held -join ', ')"
    exit 1
}
$status = Get-Prop $cyber 'status'
Write-WtSuccess "Holds Cyber Level credential $($cyber.id) [$status]"

# Accept the offer if it is still pending — exactly what the citizen does in their wallet, and a
# genuine step of the demo journey that the rehearsals never exercise (they assert DELIVERY, i.e.
# the offer landing, and stop there). A PendingAcceptance credential is not the holder's to present
# yet, so the verify moment depends on this transition having happened.
# The wire form is kebab-case ("pending-acceptance") while the enum is PascalCase — compare with
# separators and case stripped so neither spelling silently skips the accept.
$statusKey = ($status -replace '[^a-zA-Z]', '').ToLowerInvariant()
if ($statusKey -eq 'pendingacceptance') {
    Write-WtInfo "Offer is pending — accepting it as the holder would in their wallet"
    $null = Invoke-SorchaApi -Method PATCH `
        -Uri "$api/v1/wallets/$($wallet.address)/credentials/$([Uri]::EscapeDataString($cyber.id))" `
        -Body @{ status = 'Active' } -Headers $session.Headers
    Write-WtSuccess "Credential accepted (PendingAcceptance -> Active)"
}

# ---------------------------------------------------------------------------
# 3. Present — the same server-custody path the PWA uses
# ---------------------------------------------------------------------------
Write-WtStep "3. Holder presents (server-custody KB-JWT via sign-kb)"

$applicant = [pscustomobject]@{ Email = $Email; Session = $session; Wallet = $wallet }

# Deliberately NOT passing -State. The create response HAS a `state` field, but it is the request's
# STATUS enum (PresentationRequest.State -> "Pending"), not the OAuth `state` parameter — a name
# collision that is easy to walk into. The OAuth state this request object actually declares is the
# request id, which is exactly what -State defaults to. Passing $vreq.state instead yields
# "state parameter does not match the request" from direct_post.
$callback = Complete-SorchaWalletPresentation -Applicant $applicant -Credential $cyber `
    -ApprovedClaims $RequiredClaims `
    -PresentationRequestId $vreq.requestId `
    -AuthorizationRequestUri $vreq.authorizationRequestUri `
    -Gateway $gateway -ApiBase $api
if ($null -eq $callback) {
    # Do not report success on a callback that returned nothing — the verifier-result poll below
    # would then fail with a far less obvious message.
    Write-WtFail "direct_post returned no response — the presentation was not accepted."
    exit 1
}
Write-WtSuccess "Presentation posted to the verifier's direct_post endpoint"

# ---------------------------------------------------------------------------
# 4. Verifier reads its own result back
# ---------------------------------------------------------------------------
Write-WtStep "4. Verifier result"

# `state` is on the ENVELOPE, not inside `result` — the payload is
# { requestId, state, result: { isValid, verifiedClaims, ... }, vpToken }.
$res = $null
for ($i = 0; $i -lt 15; $i++) {
    $res = Invoke-SorchaApi -Method GET -Uri "$api/v1/verifier/requests/$($vreq.requestId)/result" -Headers $svcHeaders
    if ((Get-Prop $res 'state') -in @('Verified', 'Denied')) { break }
    Start-Sleep -Seconds 1
}
if ($ShowJson) { $res | ConvertTo-Json -Depth 10 | Write-Host }

$resState = Get-Prop $res 'state'
if (-not $resState) { Write-WtFail "verifier returned no result payload."; exit 1 }
if ($resState -ne 'Verified') { Write-WtFail "verifier state = '$resState' (expected 'Verified')."; exit 1 }
Write-WtSuccess "state = Verified"

$inner = Get-Prop $res 'result'
if (-not $inner) { Write-WtFail "verifier state is Verified but carried no result body."; exit 1 }
if ((Get-Prop $inner 'isValid') -ne $true) {
    Write-WtFail "verifier reported isValid = $(Get-Prop $inner 'isValid')."
    exit 1
}
Write-WtSuccess "isValid = true"

# Key binding is the difference between "a valid credential was shown" and "the holder proved it is
# theirs" — assert it explicitly rather than trusting isValid to cover it.
if ((Get-Prop $inner 'holderKeyVerified') -ne $true) {
    Write-WtFail "holderKeyVerified = $(Get-Prop $inner 'holderKeyVerified') — key binding not proven."
    exit 1
}
Write-WtSuccess "holderKeyVerified = true (KB-JWT bound to the credential's cnf key)"

# The point of the whole exercise: the verdict has something to render. A request whose `claims`
# were omitted verifies happily and discloses NOTHING (OpenID4VP 1.0: "the Wallet MUST return only
# the claims that are mandatory to present") — a blank verdict that still says Verified. Assert the
# claims are actually there, or this proves less than it appears to.
$vc = Get-Prop $inner 'verifiedClaims'
$claimNames = @()
if ($vc) { $claimNames = @($vc.PSObject.Properties.Name) }
$envelope = @('iss', 'iat', 'exp', 'nbf', 'sub', 'cnf', 'vct', 'status', '_sd', '_sd_alg')
$domain = @($claimNames | Where-Object { $_ -notin $envelope } | Sort-Object)
Write-WtInfo "Disclosed: $($domain -join ', ')"

$missing = @($RequiredClaims | Where-Object { $_ -notin $domain })
if ($missing.Count -gt 0) {
    Write-WtFail "verified, but the verdict is missing $($missing -join ', ') — nothing to render."
    exit 1
}
Write-WtSuccess "every requested claim was disclosed ($($RequiredClaims -join ', '))"

$level = Get-Prop $vc 'level'
Write-WtBanner "VERIFY MOMENT PROVEN — Cyber Level '$level' verified from a real held credential"
exit 0
