# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Scripted holder for a server-custody Sorcha wallet — the OpenID4VP side of the AIAS demo.
#
# Extracted verbatim from demos/AIAS/rehearse.ps1 so it can be driven WITHOUT running a rehearsal
# (AIAS M3 needs the same holder to answer a *verifier* request, not just a blueprint credential
# gate). The flow is transport-agnostic by construction: nonce, client_id and response_uri are all
# read FROM the request object rather than rebuilt, so it does not care who asked.
#
# WHY THIS IS SCRIPTABLE AT ALL: an AIAS credential is issued `SorchaLocalWallet` — an SD-JWT
# encrypted to the citizen wallet's key and sealed into the transaction — so the holder private key
# lives in server custody and the KB-JWT is signed by POST /api/v1/wallet/presentations/sign-kb
# (#1195 Phase 2). There is no device to drive. Note this is why the `sorcha-agent haip present`
# CLI holder canNOT stand in: that path consumes an OpenID4VCI *offer* into a file wallet
# (TargetAudience.HaipExternalWallet), and no offer exists for a SorchaLocalWallet credential.
#
# Dot-source this file; do not duplicate the logic. The wire shapes below (RFC 9901 sd_hash, the
# 120s KB-JWT window, the OpenID4VP 1.0 object-keyed vp_token envelope) mirror
# Sorcha.Wallet.Pwa.Services.Presentation.PresentationEngine and Pages/Present.razor exactly — the
# point is to certify the path the phone actually uses, not a parallel mechanism.


function ConvertFrom-Base64Url {
    # Minimal base64url -> UTF8 string decoder (pads to a multiple of 4, swaps the URL-safe chars).
    param([Parameter(Mandatory)][string]$Value)
    $s = $Value.Replace('-', '+').Replace('_', '/')
    $pad = (4 - ($s.Length % 4)) % 4
    $s += ('=' * $pad)
    return [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($s))
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

function Resolve-DemoServerUri {
    # PresentationLifecycleOptions.PublicBaseUrl is unset on every rehearsal target — its documented
    # behaviour is "null => relative URIs" (Sorcha.Blueprint.Service.Configuration.
    # PresentationLifecycleOptions), so SorchaWalletPresentationConsumer.BuildInitiationAsync emits
    # BOTH request_uri and response_uri as relative paths, e.g.
    # "/api/presentations/{id}/request-object". The real wallet (Sorcha.Wallet.Pwa) never notices:
    # its HttpClient carries BaseAddress set to the gateway origin, so the relative URI resolves
    # same-origin before the request leaves the client (see
    # Sorcha.Wallet.Pwa.Services.Presentation.PresentationDirectPostClient's doc comment — it makes
    # no assumption either way, relative or absolute, because HttpClient resolves both). PowerShell's
    # Invoke-RestMethod/Invoke-WebRequest have no BaseAddress concept — a bare relative URI throws
    # "Invalid URI: The hostname could not be parsed." This script is standing in for the wallet, so
    # it must do the SAME resolution HttpClient.BaseAddress does. A deployment that DOES set
    # PublicBaseUrl emits absolute URIs instead — those must pass through unchanged, so do not remove
    # this helper because it looks like a no-op on such a deployment.
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$GatewayBase
    )
    $parsed = [System.Uri]$Uri
    if ($parsed.IsAbsoluteUri) { return $Uri }
    return ([System.Uri]::new([System.Uri]$GatewayBase, $Uri)).AbsoluteUri
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
        [Parameter(Mandatory)][string]$AuthorizationRequestUri,
        # Were script-scoped ($gateway/$api) when this lived in rehearse.ps1; a dot-sourced lib
        # cannot see those, so callers pass them explicitly.
        [Parameter(Mandatory)][string]$Gateway,
        [Parameter(Mandatory)][string]$ApiBase,
        # direct_post `state`. The blueprint credential-gate flow uses the presentation-request id
        # as the state, which is why that was hardcoded; an OpenID4VP verifier request declares its
        # own `state` in the request object. Defaults to the old value, so existing callers are
        # byte-identical.
        [string]$State
    )

    $requestObjectUri = Get-QueryStringValue -Url $AuthorizationRequestUri -Name 'request_uri'
    if (-not $requestObjectUri) { throw "AuthorizationRequestUri carried no request_uri: $AuthorizationRequestUri" }
    $requestObjectUri = Resolve-DemoServerUri -Uri $requestObjectUri -GatewayBase $Gateway

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

    # response_uri is the direct_post target the request object itself names (mirrors
    # PresentationEngine.ParseAsync reading the same field) — read it from the server rather than
    # rebuilding the callback path locally, and resolve it exactly like request_uri above.
    $responseUri = $requestPayload.response_uri
    if (-not $responseUri) { throw "Request object payload for $PresentationRequestId is missing response_uri." }
    $responseUri = Resolve-DemoServerUri -Uri $responseUri -GatewayBase $Gateway

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
        -Uri "$ApiBase/v1/wallets/$walletAddr/credentials/$($Credential.id)/export" `
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
    $holderKeys = Invoke-SorchaApi -Method GET -Uri "$ApiBase/v1/wallet/holder-keys" -Headers $Applicant.Session.Headers
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
        -Uri "$ApiBase/v1/wallet/presentations/sign-kb" `
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
    $effectiveState = if ($State) { $State } else { $PresentationRequestId }
    $formBody = "vp_token=$([Uri]::EscapeDataString($vpTokenEnvelope))&state=$([Uri]::EscapeDataString($effectiveState))"

    $callbackResp = Invoke-SorchaApi -Method POST `
        -Uri $responseUri `
        -Body $formBody `
        -ContentType 'application/x-www-form-urlencoded' `
        -Headers $Applicant.Session.Headers

    # The blueprint sorcha-wallet callback answers {kind:...}; an OpenID4VP verifier's direct_post
    # need not. Probe the property (Set-StrictMode-safe) rather than assuming the shape, and leave
    # shape-specific assertions to the caller when it is absent.
    $kindProp = if ($null -ne $callbackResp) { $callbackResp.PSObject.Properties['kind'] } else { $null }
    if ($kindProp -and $kindProp.Value -ne 'Success') {
        throw "Presentation callback for $PresentationRequestId returned kind='$($kindProp.Value)' (expected 'Success')."
    }
    return $callbackResp
}
