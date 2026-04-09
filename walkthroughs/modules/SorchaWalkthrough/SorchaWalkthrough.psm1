# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# SorchaWalkthrough.psm1 — Shared module for all Sorcha walkthrough scripts.
# Eliminates ~150 lines of duplicated helper code per script.

# ============================================================================
# T002: Console Output Functions
# ============================================================================

function Write-WtStep {
    <#
    .SYNOPSIS
        Display a major step header.
    #>
    param([Parameter(Mandatory)][string]$Message)
    Write-Host ""
    Write-Host "================================================================================" -ForegroundColor Gray
    Write-Host "  $Message" -ForegroundColor White
    Write-Host "================================================================================" -ForegroundColor Gray
}

function Write-WtSuccess {
    <#
    .SYNOPSIS
        Display a success message.
    #>
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-WtFail {
    <#
    .SYNOPSIS
        Display a failure message.
    #>
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[X] $Message" -ForegroundColor Red
}

function Write-WtInfo {
    <#
    .SYNOPSIS
        Display an informational message.
    #>
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[i] $Message" -ForegroundColor Cyan
}

function Write-WtWarn {
    <#
    .SYNOPSIS
        Display a warning message.
    #>
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[!] $Message" -ForegroundColor Yellow
}

function Write-WtBanner {
    <#
    .SYNOPSIS
        Display a framed banner for walkthrough start/end.
    #>
    param(
        [Parameter(Mandatory)][string]$Title,
        [string]$Color = "Cyan"
    )
    $width = 80
    $pad = $width - 4
    $titlePadded = "  $Title  "
    if ($titlePadded.Length -gt $pad) { $titlePadded = $titlePadded.Substring(0, $pad) }
    $titleCentered = $titlePadded.PadLeft(([math]::Floor(($pad + $titlePadded.Length) / 2))).PadRight($pad)

    Write-Host ""
    Write-Host ("+" + "=" * ($width - 2) + "+") -ForegroundColor $Color
    Write-Host ("| " + $titleCentered + " |") -ForegroundColor $Color
    Write-Host ("+" + "=" * ($width - 2) + "+") -ForegroundColor $Color
    Write-Host ""
}

# ============================================================================
# T003: Invoke-SorchaApi — Consolidated HTTP Caller
# ============================================================================

function Invoke-SorchaApi {
    <#
    .SYNOPSIS
        Make an HTTP request to a Sorcha service endpoint.
    .DESCRIPTION
        Unified HTTP caller supporting JSON and form-urlencoded bodies.
        Handles URL-encoding of passwords with special characters.
        Returns parsed JSON by default or raw WebResponse with -RawResponse.
    .PARAMETER Method
        HTTP method (GET, POST, PUT, DELETE, PATCH).
    .PARAMETER Uri
        Full endpoint URL.
    .PARAMETER Body
        Request body — hashtable/PSObject (auto-serialized to JSON) or string.
    .PARAMETER Headers
        HTTP headers hashtable.
    .PARAMETER ContentType
        Content-Type header. Defaults to "application/json".
    .PARAMETER RawResponse
        Return raw Invoke-WebRequest response (includes StatusCode).
    .PARAMETER ShowJson
        Print request/response bodies for debugging.
    #>
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Uri,
        [object]$Body = $null,
        [hashtable]$Headers = @{},
        [string]$ContentType = "application/json",
        [switch]$RawResponse,
        [switch]$ShowJson
    )

    $params = @{
        Uri            = $Uri
        Method         = $Method
        Headers        = $Headers
        UseBasicParsing = $true
    }

    if ($Body) {
        if ($ContentType -eq "application/json") {
            $jsonBody = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 30 }
            $params.Body = $jsonBody
        } else {
            $params.Body = $Body
        }
        $params.ContentType = $ContentType
    }

    if ($ShowJson -and $Body) {
        Write-Host "  >> $Method $Uri" -ForegroundColor DarkGray
        $displayBody = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 10 }
        Write-Host "  $displayBody" -ForegroundColor DarkGray
    }

    if ($RawResponse) {
        $response = Invoke-WebRequest @params
        if ($ShowJson) {
            Write-Host "  << $($response.StatusCode)" -ForegroundColor DarkGray
            Write-Host "  $($response.Content)" -ForegroundColor DarkGray
        }
        return $response
    } else {
        $response = Invoke-RestMethod @params
        if ($ShowJson) {
            Write-Host "  << Response:" -ForegroundColor DarkGray
            Write-Host "  $($response | ConvertTo-Json -Depth 10)" -ForegroundColor DarkGray
        }
        return $response
    }
}

# ============================================================================
# T004: Decode-SorchaJwt — JWT Decode with Base64 Padding Fix
# ============================================================================

function Decode-SorchaJwt {
    <#
    .SYNOPSIS
        Decode a JWT token payload.
    .RETURNS
        PSObject with JWT claims (sub, org_id, role, iss, etc.).
    #>
    param([Parameter(Mandatory)][string]$Token)

    $parts = $Token.Split('.')
    if ($parts.Length -lt 2) { throw "Invalid JWT token format" }

    $base64 = $parts[1].Replace('-', '+').Replace('_', '/')
    switch ($base64.Length % 4) {
        1 { $base64 += '===' }
        2 { $base64 += '==' }
        3 { $base64 += '=' }
    }

    $payloadBytes = [System.Convert]::FromBase64String($base64)
    $payload = [System.Text.Encoding]::UTF8.GetString($payloadBytes)
    return ($payload | ConvertFrom-Json)
}

# ============================================================================
# T005: Get-SorchaErrorBody — HTTP Error Body Extraction
# ============================================================================

function Get-SorchaErrorBody {
    <#
    .SYNOPSIS
        Extract the response body from an HTTP error.
    .PARAMETER ErrorRecord
        The $_ error record from a catch block.
    .RETURNS
        Error body string, or $null if unavailable.
    #>
    param([Parameter(Mandatory)]$ErrorRecord)

    # Try ErrorDetails first (PS 7+ populates this)
    if ($ErrorRecord.ErrorDetails.Message) {
        return $ErrorRecord.ErrorDetails.Message
    }

    # Fall back to reading the response stream
    try {
        if ($ErrorRecord.Exception.Response) {
            $errorStream = $ErrorRecord.Exception.Response.GetResponseStream()
            $errorReader = New-Object System.IO.StreamReader($errorStream)
            return $errorReader.ReadToEnd()
        }
    } catch { }

    return $null
}

# ============================================================================
# Utility: Hex to Base64 Conversion
# ============================================================================

function ConvertFrom-HexToBase64 {
    <#
    .SYNOPSIS
        Convert a hex string to base64 (used for attestation signing).
    #>
    param([Parameter(Mandatory)][string]$HexString)

    $hashBytes = [byte[]]::new($HexString.Length / 2)
    for ($i = 0; $i -lt $hashBytes.Length; $i++) {
        $hashBytes[$i] = [Convert]::ToByte($HexString.Substring($i * 2, 2), 16)
    }
    return [Convert]::ToBase64String($hashBytes)
}

# ============================================================================
# T006: Initialize-SorchaEnvironment — Docker Health + URL Config
# ============================================================================

function Initialize-SorchaEnvironment {
    <#
    .SYNOPSIS
        Check Docker health and configure service URLs for the given profile.
    .PARAMETER Profile
        Connection profile: 'gateway' (Docker, port 80), 'direct' (Docker, native ports),
        or 'aspire' (Aspire, HTTPS ports).
    .PARAMETER SkipHealthCheck
        Skip Docker and API Gateway health checks.
    .RETURNS
        Hashtable with keys: GatewayUrl, TenantUrl, BlueprintUrl, RegisterUrl, WalletUrl, Profile.
    #>
    param(
        [ValidateSet('gateway', 'direct', 'aspire', 'n1')]
        [string]$Profile = 'gateway',
        [switch]$SkipHealthCheck
    )

    $env = @{ Profile = $Profile }

    switch ($Profile) {
        'gateway' {
            $env.GatewayUrl   = "http://127.0.0.1"
            $env.TenantUrl    = "http://127.0.0.1/api"
            $env.BlueprintUrl = "http://127.0.0.1/api"
            $env.RegisterUrl  = "http://127.0.0.1/api"
            $env.WalletUrl    = "http://127.0.0.1/api"
        }
        'direct' {
            $env.GatewayUrl   = "http://127.0.0.1"
            $env.TenantUrl    = "http://127.0.0.1:5450/api"
            $env.BlueprintUrl = "http://127.0.0.1:5000/api"
            $env.RegisterUrl  = "http://127.0.0.1:5380/api"
            $env.WalletUrl    = "http://127.0.0.1/api"  # wallet has no direct port
        }
        'aspire' {
            $env.GatewayUrl   = "https://localhost:7082"
            $env.TenantUrl    = "https://localhost:7110/api"
            $env.BlueprintUrl = "https://localhost:7000/api"
            $env.RegisterUrl  = "https://localhost:7290/api"
            $env.WalletUrl    = "https://localhost:7082/api"
        }
        'n1' {
            $env.GatewayUrl   = "https://n1.sorcha.dev"
            $env.TenantUrl    = "https://n1.sorcha.dev/api"
            $env.BlueprintUrl = "https://n1.sorcha.dev/api"
            $env.RegisterUrl  = "https://n1.sorcha.dev/api"
            $env.WalletUrl    = "https://n1.sorcha.dev/api"
        }
    }

    if (-not $SkipHealthCheck) {
        # Check Docker
        Write-WtInfo "Checking Docker availability..."
        $dockerInfo = docker info 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Docker is not running. Start Docker Desktop and run: docker-compose up -d"
        }
        Write-WtSuccess "Docker is running"

        # Check API Gateway health
        Write-WtInfo "Checking API Gateway health..."
        try {
            $null = Invoke-RestMethod -Uri "$($env.GatewayUrl)/api/health" -Method GET -TimeoutSec 10 -UseBasicParsing
            Write-WtSuccess "API Gateway is healthy"
        } catch {
            Write-WtWarn "API Gateway health check failed — services may still be starting"
        }
    }

    Write-WtInfo "Profile: $Profile"
    Write-WtInfo "  Tenant:    $($env.TenantUrl)"
    Write-WtInfo "  Blueprint: $($env.BlueprintUrl)"
    Write-WtInfo "  Register:  $($env.RegisterUrl)"
    Write-WtInfo "  Wallet:    $($env.WalletUrl)"

    return $env
}

# ============================================================================
# T007: Get-SorchaSecrets — Read Credentials from .secrets or Env Vars
# ============================================================================

function Get-SorchaSecrets {
    <#
    .SYNOPSIS
        Load walkthrough credentials from .secrets/passwords.json or environment variables.
    .PARAMETER WalkthroughName
        Name of the walkthrough (key in passwords.json).
    .PARAMETER SecretsDir
        Path to the .secrets directory. Defaults to walkthroughs/.secrets/.
    .RETURNS
        Hashtable with credential key-value pairs.
    #>
    param(
        [Parameter(Mandatory)][string]$WalkthroughName,
        [string]$SecretsDir = ""
    )

    # Default secrets dir relative to module location
    if (-not $SecretsDir) {
        $moduleDir = Split-Path -Parent $PSScriptRoot
        $SecretsDir = Join-Path (Split-Path -Parent $moduleDir) ".secrets"
    }

    # Check environment variable override first
    $envKey = "SORCHA_WT_SECRETS_$($WalkthroughName.ToUpper().Replace('-', '_'))"
    $envVal = [System.Environment]::GetEnvironmentVariable($envKey)
    if ($envVal) {
        Write-WtInfo "Loading secrets from environment variable $envKey"
        return ($envVal | ConvertFrom-Json -AsHashtable)
    }

    # Read from passwords.json
    $passwordsFile = Join-Path $SecretsDir "passwords.json"
    if (-not (Test-Path $passwordsFile)) {
        throw "Secrets file not found: $passwordsFile`nRun: pwsh walkthroughs/initialize-secrets.ps1"
    }

    $allSecrets = Get-Content -Path $passwordsFile -Raw | ConvertFrom-Json -Depth 10
    $wtSecrets = $allSecrets.$WalkthroughName
    if (-not $wtSecrets) {
        throw "No secrets found for walkthrough '$WalkthroughName' in $passwordsFile`nRun: pwsh walkthroughs/initialize-secrets.ps1"
    }

    # Convert PSObject to hashtable
    $result = @{}
    foreach ($prop in $wtSecrets.PSObject.Properties) {
        $result[$prop.Name] = $prop.Value
    }

    return $result
}

# ============================================================================
# T008: Connect-SorchaAdmin — Bootstrap-with-409-Fallback + Login
# ============================================================================

function Connect-SorchaAdmin {
    <#
    .SYNOPSIS
        Login as the platform seed admin.
    .DESCRIPTION
        Logs in via POST /service-auth/token using the seed admin credentials
        (created by DatabaseInitializer on Tenant Service startup). The OrgName
        and OrgSubdomain parameters are accepted for compatibility but are not
        used — all walkthroughs share the seed admin's org (sorcha-local).
    .RETURNS
        Hashtable with Token, OrganizationId, AdminUserId, Headers.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [string]$OrgName = "Sorcha Local",
        [string]$OrgSubdomain = "sorcha-local",
        [Parameter(Mandatory)][string]$AdminEmail,
        [string]$AdminName = "System Administrator",
        [Parameter(Mandatory)][string]$AdminPassword
    )

    $token = ""
    $organizationId = ""
    $adminUserId = ""

    $encodedPassword = [Uri]::EscapeDataString($AdminPassword)
    $loginBody = "grant_type=password&username=$AdminEmail&password=$encodedPassword&client_id=sorcha-cli"

    $loginResponse = Invoke-SorchaApi -Method POST `
        -Uri "$TenantUrl/service-auth/token" `
        -Body $loginBody `
        -ContentType "application/x-www-form-urlencoded"

    $token = $loginResponse.access_token
    Write-WtSuccess "Logged in as $AdminEmail"

    # Extract org info from JWT if not available from bootstrap
    if (-not $organizationId -and $token) {
        $jwt = Decode-SorchaJwt -Token $token
        $organizationId = $jwt.org_id
        $adminUserId = $jwt.sub
    }

    Write-WtInfo "Organization ID: $organizationId"
    Write-WtInfo "Admin User ID:   $adminUserId"

    return @{
        Token          = $token
        OrganizationId = $organizationId
        AdminUserId    = $adminUserId
        Headers        = @{ Authorization = "Bearer $token" }
    }
}

# ============================================================================
# Connect-SorchaUser — Two-step login (login + select-org) for any user
# ============================================================================

function Connect-SorchaUser {
    <#
    .SYNOPSIS
        Login as any user and select a specific organisation.
    .DESCRIPTION
        Uses the two-step auth flow:
        1. POST /auth/login → platform_login_token + org list
        2. POST /auth/select-org → org-scoped JWT
        Handles both single-org (direct token) and multi-org (org selection) cases.
    .RETURNS
        Hashtable with Token, OrganizationId, UserId, Headers.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$Email,
        [Parameter(Mandatory)][string]$Password,
        [Parameter(Mandatory)][string]$OrganizationId
    )

    # Step 1: Login
    $loginBody = @{
        email    = $Email
        password = $Password
    }

    $loginResponse = Invoke-SorchaApi -Method POST `
        -Uri "$TenantUrl/auth/login" `
        -Body $loginBody

    # Single-org user: token returned directly
    if ($loginResponse.access_token) {
        $token = $loginResponse.access_token
        $jwt = Decode-SorchaJwt -Token $token
        Write-WtSuccess "Logged in as $Email (single-org: $($jwt.org_id))"
        return @{
            Token          = $token
            OrganizationId = $jwt.org_id
            UserId         = $jwt.sub
            Headers        = @{ Authorization = "Bearer $token" }
        }
    }

    # Multi-org user: select the target org
    if ($loginResponse.requires_org_selection -and $loginResponse.platform_login_token) {
        $selectBody = @{
            platform_login_token = $loginResponse.platform_login_token
            organization_id      = $OrganizationId
        }

        $selectResponse = Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/auth/select-org" `
            -Body $selectBody

        $token = $selectResponse.access_token
        $jwt = Decode-SorchaJwt -Token $token
        Write-WtSuccess "Logged in as $Email -> org $($jwt.org_id)"
        return @{
            Token          = $token
            OrganizationId = $jwt.org_id
            UserId         = $jwt.sub
            Headers        = @{ Authorization = "Bearer $token" }
        }
    }

    throw "Unexpected login response for $Email — no token or org selection flow returned."
}

# ============================================================================
# T009: Get-OrCreateOrganization — Idempotent Org Creation
# ============================================================================

function Get-OrCreateOrganization {
    <#
    .SYNOPSIS
        Find an existing organization by subdomain, or create it via bootstrap.
    .RETURNS
        Hashtable with Token, OrganizationId, AdminUserId, Headers.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$OrgName,
        [Parameter(Mandatory)][string]$OrgSubdomain,
        [Parameter(Mandatory)][string]$AdminEmail,
        [Parameter(Mandatory)][string]$AdminName,
        [Parameter(Mandatory)][string]$AdminPassword
    )

    # Connect-SorchaAdmin already handles bootstrap + 409 fallback
    return Connect-SorchaAdmin `
        -TenantUrl $TenantUrl `
        -OrgName $OrgName `
        -OrgSubdomain $OrgSubdomain `
        -AdminEmail $AdminEmail `
        -AdminName $AdminName `
        -AdminPassword $AdminPassword
}

# ============================================================================
# T010: Get-OrCreateUser — Idempotent User Creation
# ============================================================================

function Get-OrCreateUser {
    <#
    .SYNOPSIS
        Create a user in an organization, or return existing if 409/400.
    .RETURNS
        User ID string.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$OrganizationId,
        [Parameter(Mandatory)][string]$Email,
        [Parameter(Mandatory)][string]$DisplayName,
        [Parameter(Mandatory)][hashtable]$Headers,
        [string[]]$Roles = @("Consumer")
    )

    $userBody = @{
        email              = $Email
        displayName        = $DisplayName
        externalIdpSubject = "$($DisplayName.ToLower().Replace(' ', '-'))-" + [guid]::NewGuid().ToString().Substring(0, 8)
        roles              = $Roles
    }

    try {
        $response = Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/organizations/$OrganizationId/users" `
            -Body $userBody `
            -Headers $Headers

        Write-WtSuccess "User created: $DisplayName (ID: $($response.id))"
        return $response.id
    } catch {
        $statusCode = $null
        try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}

        if ($statusCode -eq 409 -or $statusCode -eq 400) {
            Write-WtWarn "User '$DisplayName' already exists — continuing"
            # Cannot reliably get the existing user ID here; caller may need to list users
            return $null
        }
        throw
    }
}

# ============================================================================
# Register-SorchaPublicUser — Self-register a user on the public org
# ============================================================================

function Register-SorchaPublicUser {
    <#
    .SYNOPSIS
        Register a user on the public org via POST /auth/register.
        Creates both PlatformUser and UserIdentity in the public org.
    .RETURNS
        User ID string, or $null if user already exists.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$Email,
        [Parameter(Mandatory)][string]$Password,
        [Parameter(Mandatory)][string]$DisplayName,
        [string]$OrgSubdomain = "public"
    )

    $body = @{
        orgSubdomain = $OrgSubdomain
        email        = $Email
        password     = $Password
        displayName  = $DisplayName
    }

    try {
        $response = Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/auth/register" `
            -Body $body

        if ($response.success) {
            Write-WtSuccess "Registered '$DisplayName' ($Email) -> userId: $($response.userId)"
            return $response.userId
        } else {
            Write-WtWarn "Registration returned: $($response.message)"
            return $null
        }
    } catch {
        $statusCode = $null
        try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}

        if ($statusCode -eq 409 -or $statusCode -eq 400 -or $statusCode -eq 503) {
            # 409/400: user already exists. 503: public org registration disabled or
            # maxOrgsPerUser limit reached (user already registered in a private org).
            Write-WtInfo "User '$Email' already registered (status $statusCode) — continuing"
            return [Guid]::Empty.ToString()
        }
        throw
    }
}

# ============================================================================
# Confirm-SorchaUserEmail — Admin verify email (bypasses SMTP loop)
# ============================================================================

function Confirm-SorchaUserEmail {
    <#
    .SYNOPSIS
        Administratively mark a user's email as verified (no SMTP required).
    .DESCRIPTION
        Calls POST /organizations/{orgId}/users/{userId}/verify-email.
        Returns $true if verified, $false if already verified.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$OrganizationId,
        [Parameter(Mandatory)][string]$UserId,
        [Parameter(Mandatory)][hashtable]$Headers
    )

    try {
        Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/organizations/$OrganizationId/users/$UserId/verify-email" `
            -Headers $Headers
        Write-WtSuccess "Email verified for user $UserId"
        return $true
    } catch {
        $statusCode = $null
        try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}

        if ($statusCode -eq 400) {
            Write-WtInfo "Email already verified for user $UserId"
            return $false
        }
        if ($statusCode -eq 404) {
            Write-WtWarn "User $UserId not found — skipping verify"
            return $false
        }
        throw
    }
}

# ============================================================================
# T011: New-SorchaWallet — Create ED25519 Wallet
# ============================================================================

function New-SorchaWallet {
    <#
    .SYNOPSIS
        Create a new ED25519 wallet.
    .RETURNS
        Hashtable with Address, Mnemonic, PublicKey (PublicKey populated if -FetchPublicKey).
    #>
    param(
        [Parameter(Mandatory)][string]$WalletUrl,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][hashtable]$Headers,
        [string]$Algorithm = "ED25519",
        [int]$WordCount = 12,
        [switch]$FetchPublicKey
    )

    $walletBody = @{
        name      = $Name
        algorithm = $Algorithm
        wordCount = $WordCount
    }

    $response = Invoke-SorchaApi -Method POST `
        -Uri "$WalletUrl/v1/wallets" `
        -Body $walletBody `
        -Headers $Headers

    $address = $response.wallet.address
    $mnemonic = ($response.mnemonicWords -join " ")

    Write-WtSuccess "Wallet '$Name' created: $address"
    Write-WtWarn "Mnemonic (BACKUP!): $mnemonic"

    $result = @{
        Address   = $address
        Mnemonic  = $mnemonic
        PublicKey = $null
    }

    # Optionally fetch public key by signing a probe message
    if ($FetchPublicKey) {
        $probeBody = @{
            transactionData = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("key-probe"))
            isPreHashed     = $false
        }
        $signResponse = Invoke-SorchaApi -Method POST `
            -Uri "$WalletUrl/v1/wallets/$address/sign" `
            -Body $probeBody `
            -Headers $Headers

        $result.PublicKey = $signResponse.publicKey
    }

    return $result
}

# ============================================================================
# T012: Register-SorchaParticipant — Participant Registration + Wallet Link
# ============================================================================

function Register-SorchaParticipant {
    <#
    .SYNOPSIS
        Register a participant and link a wallet via challenge-sign-verify.
    .DESCRIPTION
        1. Self-register (or fetch existing) as participant in org
        2. Initiate wallet link challenge
        3. Sign challenge with wallet
        4. Verify signed challenge
    .RETURNS
        Hashtable with ParticipantId, PublicKey.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$WalletUrl,
        [Parameter(Mandatory)][string]$OrganizationId,
        [Parameter(Mandatory)][string]$WalletAddress,
        [Parameter(Mandatory)][string]$DisplayName,
        [Parameter(Mandatory)][hashtable]$Headers
    )

    $participantId = ""

    # Step 1: Self-register as participant
    try {
        $encodedName = [Uri]::EscapeDataString($DisplayName)
        $selfRegResponse = Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/me/organizations/$OrganizationId/self-register?displayName=$encodedName" `
            -Headers $Headers

        $participantId = $selfRegResponse.id
        Write-WtSuccess "Participant registered: $DisplayName ($participantId)"
    } catch {
        $statusCode = $null
        try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}

        if ($statusCode -eq 409) {
            Write-WtWarn "Participant '$DisplayName' already exists — fetching profile"

            $profiles = Invoke-SorchaApi -Method GET `
                -Uri "$TenantUrl/me/participant-profiles" `
                -Headers $Headers

            $orgProfile = $profiles | Where-Object { $_.organizationId -eq $OrganizationId } | Select-Object -First 1
            if ($orgProfile) {
                $participantId = $orgProfile.id
                Write-WtSuccess "Found existing participant: $participantId"
            } else {
                throw "No participant profile found for organization $OrganizationId"
            }
        } else {
            throw
        }
    }

    # Step 2-4: Link wallet via challenge-sign-verify
    $publicKey = $null
    try {
        # Initiate challenge
        $challengeBody = @{
            walletAddress = $WalletAddress
            algorithm     = "ED25519"
        }
        $challengeResponse = Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/organizations/$OrganizationId/participants/$participantId/wallet-links" `
            -Body $challengeBody `
            -Headers $Headers

        $challengeId = $challengeResponse.challengeId
        $challengeMessage = $challengeResponse.challenge

        # Sign challenge (NOT pre-hashed — raw challenge text)
        $challengeBytes = [System.Text.Encoding]::UTF8.GetBytes($challengeMessage)
        $challengeBase64 = [Convert]::ToBase64String($challengeBytes)

        $signBody = @{
            transactionData = $challengeBase64
            isPreHashed     = $false
        }
        $signResponse = Invoke-SorchaApi -Method POST `
            -Uri "$WalletUrl/v1/wallets/$WalletAddress/sign" `
            -Body $signBody `
            -Headers $Headers

        $publicKey = $signResponse.publicKey

        # Verify
        $verifyBody = @{
            signature = $signResponse.signature
            publicKey = $signResponse.publicKey
        }
        $null = Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/organizations/$OrganizationId/participants/$participantId/wallet-links/$challengeId/verify" `
            -Body $verifyBody `
            -Headers $Headers

        Write-WtSuccess "Wallet $WalletAddress linked to participant $participantId"
    } catch {
        $errMsg = $_.Exception.Message
        if ($errMsg -match "already linked" -or $errMsg -match "409") {
            Write-WtWarn "Wallet already linked — continuing"

            # Fetch public key if we didn't get it from signing
            if (-not $publicKey) {
                $probeBody = @{
                    transactionData = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("key-probe"))
                    isPreHashed     = $false
                }
                $probeResponse = Invoke-SorchaApi -Method POST `
                    -Uri "$WalletUrl/v1/wallets/$WalletAddress/sign" `
                    -Body $probeBody `
                    -Headers $Headers
                $publicKey = $probeResponse.publicKey
            }
        } else {
            throw
        }
    }

    return @{
        ParticipantId = $participantId
        PublicKey      = $publicKey
    }
}

# ============================================================================
# T013: Publish-SorchaParticipant — Publish Participant Record to Register
# ============================================================================

function Publish-SorchaParticipant {
    <#
    .SYNOPSIS
        Publish a participant record to a register (on-ledger identity).
    .RETURNS
        Publish response or $null if already published (409).
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$OrganizationId,
        [Parameter(Mandatory)][string]$RegisterId,
        [Parameter(Mandatory)][string]$ParticipantName,
        [Parameter(Mandatory)][string]$OrganizationName,
        [Parameter(Mandatory)][string]$WalletAddress,
        [Parameter(Mandatory)][string]$PublicKey,
        [Parameter(Mandatory)][hashtable]$Headers
    )

    $publishBody = @{
        registerId       = $RegisterId
        participantName  = $ParticipantName
        organizationName = $OrganizationName
        addresses        = @(
            @{
                walletAddress = $WalletAddress
                publicKey     = $PublicKey
                algorithm     = "ED25519"
                primary       = $true
            }
        )
        signerWalletAddress = $WalletAddress
    }

    try {
        $response = Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/organizations/$OrganizationId/participants/publish" `
            -Body $publishBody `
            -Headers $Headers

        Write-WtSuccess "Participant '$ParticipantName' published (TX: $($response.transactionId))"
        return $response
    } catch {
        $statusCode = $null
        try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}

        if ($statusCode -eq 409) {
            Write-WtWarn "Participant '$ParticipantName' already published (409) — continuing"
            return $null
        }
        throw
    }
}

# ============================================================================
# T014: New-SorchaRegister — 2-Phase Register Creation
# ============================================================================

function New-SorchaRegister {
    <#
    .SYNOPSIS
        Create a register using the 2-phase initiate → sign → finalize flow.
    .PARAMETER RegisterUrl
        Register service base URL.
    .PARAMETER WalletUrl
        Wallet service base URL (for signing attestations).
    .PARAMETER Name
        Register display name.
    .PARAMETER Description
        Register description.
    .PARAMETER TenantId
        Organization/tenant ID.
    .PARAMETER OwnerUserId
        Owner user ID for the register.
    .PARAMETER OwnerWalletAddress
        Owner wallet address for signing attestations.
    .PARAMETER Headers
        Authorization headers.
    .PARAMETER Metadata
        Optional metadata hashtable.
    .RETURNS
        Hashtable with RegisterId, GenesisTransactionId.
    #>
    param(
        [Parameter(Mandatory)][string]$RegisterUrl,
        [Parameter(Mandatory)][string]$WalletUrl,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Description,
        [Parameter(Mandatory)][string]$TenantId,
        [Parameter(Mandatory)][string]$OwnerUserId,
        [Parameter(Mandatory)][string]$OwnerWalletAddress,
        [Parameter(Mandatory)][hashtable]$Headers,
        [hashtable]$Metadata = @{}
    )

    # Phase 1: Initiate
    Write-WtInfo "Initiating register '$Name'..."

    $defaultMeta = @{ source = "walkthrough" }
    foreach ($key in $Metadata.Keys) { $defaultMeta[$key] = $Metadata[$key] }

    $initiateBody = @{
        name        = $Name
        description = $Description
        tenantId    = $TenantId
        advertise   = $true
        isPublic    = $true
        owners      = @(
            @{
                userId   = $OwnerUserId
                walletId = $OwnerWalletAddress
            }
        )
        metadata = $defaultMeta
    }

    $initiateResponse = Invoke-SorchaApi -Method POST `
        -Uri "$RegisterUrl/registers/initiate" `
        -Body $initiateBody `
        -Headers $Headers

    $registerId = $initiateResponse.registerId
    $nonce = $initiateResponse.nonce
    $attestations = $initiateResponse.attestationsToSign

    Write-WtSuccess "Register initiated: $registerId"
    Write-WtInfo "Attestations to sign: $(($attestations | Measure-Object).Count)"

    # Phase 2: Sign attestations
    $signedAttestations = @()

    foreach ($att in $attestations) {
        $dataToSignBase64 = ConvertFrom-HexToBase64 -HexString $att.dataToSign

        $signBody = @{
            transactionData = $dataToSignBase64
            isPreHashed     = $true
        }

        $signResponse = Invoke-SorchaApi -Method POST `
            -Uri "$WalletUrl/v1/wallets/$($att.walletId)/sign" `
            -Body $signBody `
            -Headers $Headers

        $signedAttestations += @{
            attestationData = $att.attestationData
            publicKey       = $signResponse.publicKey
            signature       = $signResponse.signature
            algorithm       = "ED25519"
        }

        Write-WtSuccess "Attestation signed for $($att.role)"
    }

    # Phase 3: Finalize
    Write-WtInfo "Finalizing register..."

    $finalizeBody = @{
        registerId         = $registerId
        nonce              = $nonce
        signedAttestations = $signedAttestations
    }

    $finalizeResponse = Invoke-SorchaApi -Method POST `
        -Uri "$RegisterUrl/registers/finalize" `
        -Body $finalizeBody `
        -Headers $Headers

    Write-WtSuccess "Register '$Name' created: $registerId"

    return @{
        RegisterId           = $registerId
        GenesisTransactionId = $finalizeResponse.genesisTransactionId
    }
}

# ============================================================================
# T015: Publish-SorchaBlueprint — Load Template, Patch, Upload, Publish
# ============================================================================

function Publish-SorchaBlueprint {
    <#
    .SYNOPSIS
        Load a blueprint template, patch wallet addresses, create in service, and publish.
    .PARAMETER BlueprintUrl
        Blueprint service base URL.
    .PARAMETER TemplatePath
        Path to the blueprint template JSON file.
    .PARAMETER WalletMap
        Hashtable mapping participant IDs to wallet addresses.
        Example: @{ "ping" = "addr1"; "pong" = "addr2" }
    .PARAMETER Headers
        Authorization headers.
    .PARAMETER IdPrefix
        Prefix for generated blueprint ID (timestamp appended).
    .RETURNS
        Hashtable with BlueprintId, Title, Warnings.
    #>
    param(
        [Parameter(Mandatory)][string]$BlueprintUrl,
        [Parameter(Mandatory)][string]$TemplatePath,
        [Parameter(Mandatory)][hashtable]$WalletMap,
        [Parameter(Mandatory)][hashtable]$Headers,
        [string]$IdPrefix = "wt",
        [string]$RegisterId
    )

    # Load template
    Write-WtInfo "Loading blueprint template: $TemplatePath"

    if (-not (Test-Path $TemplatePath)) {
        throw "Blueprint template not found: $TemplatePath"
    }

    $templateJson = Get-Content -Path $TemplatePath -Raw | ConvertFrom-Json -Depth 30
    $blueprint = $templateJson.template

    # Patch wallet addresses in-memory
    foreach ($participant in $blueprint.participants) {
        if ($WalletMap.ContainsKey($participant.id)) {
            $participant | Add-Member -NotePropertyName "walletAddress" -NotePropertyValue $WalletMap[$participant.id] -Force
            Write-WtInfo "  Patched $($participant.id) -> $($WalletMap[$participant.id])"
        }
    }

    # Generate unique ID
    $timestamp = Get-Date -Format "yyyyMMddHHmmss"
    $blueprint.id = "$IdPrefix-$timestamp"

    # Create blueprint
    Write-WtInfo "Creating blueprint..."
    $blueprintJson = $blueprint | ConvertTo-Json -Depth 30

    $createResponse = Invoke-SorchaApi -Method POST `
        -Uri "$BlueprintUrl/blueprints/" `
        -Body $blueprintJson `
        -Headers $Headers

    $blueprintId = $createResponse.id
    Write-WtSuccess "Blueprint created: $blueprintId"

    # Publish blueprint
    Write-WtInfo "Publishing blueprint..."

    $publishBody = if ($RegisterId) { @{ registerId = $RegisterId } | ConvertTo-Json } else { $null }

    $publishRaw = Invoke-SorchaApi -Method POST `
        -Uri "$BlueprintUrl/blueprints/$blueprintId/publish" `
        -Body $publishBody `
        -Headers $Headers `
        -RawResponse

    $publishResponse = $publishRaw.Content | ConvertFrom-Json

    $warnings = @()
    if ($publishResponse.warnings -and ($publishResponse.warnings | Measure-Object).Count -gt 0) {
        $warnings = @($publishResponse.warnings)
        foreach ($w in $warnings) {
            Write-WtWarn "  $w"
        }
    }

    Write-WtSuccess "Blueprint published: $blueprintId"

    return @{
        BlueprintId = $blueprintId
        Title       = $createResponse.title
        Warnings    = $warnings
    }
}

# ============================================================================
# T016: Invoke-SorchaAction — Execute or Reject an Action
# ============================================================================

function Invoke-SorchaAction {
    <#
    .SYNOPSIS
        Execute or reject an action on a workflow instance.
    .PARAMETER BlueprintUrl
        Blueprint service base URL.
    .PARAMETER InstanceId
        Workflow instance ID.
    .PARAMETER ActionId
        Action ID (string or int).
    .PARAMETER BlueprintId
        Blueprint ID.
    .PARAMETER SenderWallet
        Wallet address of the action sender.
    .PARAMETER RegisterId
        Register ID.
    .PARAMETER PayloadData
        Hashtable of payload data for the action.
    .PARAMETER Token
        Bearer token (used for both Authorization and X-Delegation-Token).
    .PARAMETER Reject
        If set, reject the action instead of executing it.
    .PARAMETER RejectionReason
        Reason for rejection (required when -Reject).
    .RETURNS
        Action response object.
    #>
    param(
        [Parameter(Mandatory)][string]$BlueprintUrl,
        [Parameter(Mandatory)][string]$InstanceId,
        [Parameter(Mandatory)][string]$ActionId,
        [Parameter(Mandatory)][string]$BlueprintId,
        [Parameter(Mandatory)][string]$SenderWallet,
        [Parameter(Mandatory)][string]$RegisterId,
        [string]$Token,
        [hashtable]$PayloadData = @{},
        [switch]$Reject,
        [string]$RejectionReason = ""
    )

    $executeHeaders = @{
        Authorization        = "Bearer $Token"
        "X-Delegation-Token" = $Token
    }

    if ($Reject) {
        $rejectBody = @{
            reason          = $RejectionReason
            senderWallet    = $SenderWallet
            registerAddress = $RegisterId
        }

        $response = Invoke-SorchaApi -Method POST `
            -Uri "$BlueprintUrl/instances/$InstanceId/actions/$ActionId/reject" `
            -Body $rejectBody `
            -Headers $executeHeaders

        Write-WtSuccess "Action $ActionId REJECTED"
        return $response
    } else {
        $actionBody = @{
            blueprintId     = $BlueprintId
            actionId        = "$ActionId"
            instanceId      = $InstanceId
            senderWallet    = $SenderWallet
            registerAddress = $RegisterId
            payloadData     = $PayloadData
        }

        $response = Invoke-SorchaApi -Method POST `
            -Uri "$BlueprintUrl/instances/$InstanceId/actions/$ActionId/execute" `
            -Body $actionBody `
            -Headers $executeHeaders

        $nextAction = if ($response.nextAction) { $response.nextAction } else { "complete" }
        Write-WtSuccess "Action $ActionId executed (next: $nextAction)"
        return $response
    }
}

# ============================================================================
# New-SorchaOrganization — Create org via Platform Admin API
# ============================================================================

function New-SorchaOrganization {
    <#
    .SYNOPSIS
        Create a private organization via the platform admin API.
    .DESCRIPTION
        Uses POST /api/platform/organizations (requires SystemAdmin role).
        If the admin email matches an existing PlatformUser, they are added
        directly; otherwise a pending invitation is created.
    .RETURNS
        Hashtable with OrganizationId, AdminDirectlyAdded.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Subdomain,
        [Parameter(Mandatory)][string]$AdminEmail,
        [Parameter(Mandatory)][hashtable]$Headers,
        [string]$Description = ""
    )

    $body = @{
        name        = $Name
        subdomain   = $Subdomain
        adminEmail  = $AdminEmail
        description = $Description
    }

    try {
        $response = Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/platform/organizations" `
            -Body $body `
            -Headers $Headers

        if ($response.success) {
            Write-WtSuccess "Organization '$Name' created: $($response.organizationId)"
            return @{
                OrganizationId    = $response.organizationId
                AdminDirectlyAdded = $response.adminDirectlyAdded
            }
        } else {
            throw "Organization creation failed: $($response.error)"
        }
    } catch {
        $statusCode = $null
        try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}

        if ($statusCode -eq 409) {
            Write-WtWarn "Organization '$Name' already exists — fetching"
            # List orgs to find existing
            $orgs = Invoke-SorchaApi -Method GET `
                -Uri "$TenantUrl/platform/organizations" `
                -Headers $Headers
            $existing = $orgs.items | Where-Object { $_.subdomain -eq $Subdomain } | Select-Object -First 1
            if ($existing) {
                Write-WtSuccess "Found existing org: $($existing.id)"
                return @{
                    OrganizationId    = "$($existing.id)"
                    AdminDirectlyAdded = $true
                }
            }
            throw "Organization '$Name' returned 409 but could not be found"
        }
        throw
    }
}

# ============================================================================
# Connect-SorchaUser — Login as a specific user and get JWT
# ============================================================================

# (Connect-SorchaUser defined earlier in this file — two-step login + select-org flow)

# ============================================================================
# Get-SorchaRegisterByName — Look up an existing register the user is
# subscribed to by its display name. Used to make walkthrough setup scripts
# idempotent so re-runs don't create orphan registers.
# ============================================================================

function Get-SorchaRegisterByName {
    <#
    .SYNOPSIS
        Returns the register_id of an existing register the current user is
        subscribed to that matches the given name, or $null if none found.
    .PARAMETER TenantUrl
        Tenant service base URL.
    .PARAMETER Name
        Register display name to match (case-insensitive).
    .PARAMETER Headers
        Authorization headers for the user whose subscriptions to query.
    .RETURNS
        The string register_id of the matching subscription, or $null.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][hashtable]$Headers
    )

    try {
        $subscriptions = Invoke-SorchaApi -Method GET `
            -Uri "$TenantUrl/me/subscribed-registers" `
            -Headers $Headers
    } catch {
        return $null
    }

    if ($null -eq $subscriptions) { return $null }

    # The endpoint returns an array; coerce single objects to arrays
    if ($subscriptions -isnot [System.Collections.IEnumerable] -or $subscriptions -is [string]) {
        $subscriptions = @($subscriptions)
    }

    foreach ($sub in $subscriptions) {
        $subName = $sub.register_name
        if ([string]::IsNullOrEmpty($subName)) { continue }
        if ($subName -ieq $Name) {
            return $sub.register_id
        }
    }

    return $null
}

# ============================================================================
# New-SorchaRegisterSubscription — Subscribe an org to a register
# ============================================================================

function New-SorchaRegisterSubscription {
    <#
    .SYNOPSIS
        Subscribe an organization to a register.
    .DESCRIPTION
        Uses POST /api/organizations/{orgId}/register-subscriptions.
        Subscription type "Owner" is immediately Active;
        "Public" starts as Pending.
    .RETURNS
        Subscription response or $null if already subscribed (409).
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$OrganizationId,
        [Parameter(Mandatory)][string]$RegisterId,
        [Parameter(Mandatory)][hashtable]$Headers,
        [string]$RegisterName = "",
        [string]$SubscriptionType = "Owner"
    )

    $body = @{
        register_id       = $RegisterId
        register_name     = $RegisterName
        subscription_type = $SubscriptionType
    }

    try {
        $response = Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/organizations/$OrganizationId/register-subscriptions" `
            -Body $body `
            -Headers $Headers

        Write-WtSuccess "Org $OrganizationId subscribed to register $RegisterId ($SubscriptionType)"
        return $response
    } catch {
        $statusCode = $null
        try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}

        if ($statusCode -eq 409) {
            Write-WtWarn "Org already subscribed to register $RegisterId — continuing"
            return $null
        }
        throw
    }
}

# ============================================================================
# Add-SorchaPublicOrgSubscription — Subscribe the well-known Public org to
# a public register, idempotently adding the system admin as an Administrator
# of the Public org first so subsequent calls (and audit trails) have a
# real membership record to lean on.
# ============================================================================

function Add-SorchaPublicOrgSubscription {
    <#
    .SYNOPSIS
        Subscribe the Public organisation (00000000-0000-0000-0000-000000000002)
        to a public register. Ensures the system admin is a member/admin of
        the Public org before creating the subscription.
    .DESCRIPTION
        Walkthroughs that create public registers should also surface those
        registers under the Public org so consumer users (who default to the
        Public org) see them in their register list. This helper:

        1. Lists users in the Public org to check if the sysadmin is already
           a member. If not, it calls POST /organizations/{publicOrgId}/users
           using the sysadmin's email + "Administrator" role. 409 is treated
           as success.
        2. Calls POST /organizations/{publicOrgId}/register-subscriptions
           with SubscriptionType=Public and the sysadmin headers. 409 is
           treated as success.
    .PARAMETER TenantUrl
        Tenant Service base URL.
    .PARAMETER RegisterId
        32-character hex register ID to subscribe the Public org to.
    .PARAMETER RegisterName
        Display name stored with the subscription record.
    .PARAMETER SysAdminHeaders
        Authorization headers carrying the SystemAdmin JWT. The sysadmin's
        email is read from the bearer token's `email` claim so the helper
        does not need a separate parameter.
    .PARAMETER SysAdminEmail
        Optional explicit sysadmin email. If omitted, extracted from the
        bearer token in SysAdminHeaders.
    .PARAMETER SysAdminDisplayName
        Optional display name for the sysadmin membership row. Defaults to
        "System Administrator".
    .RETURNS
        Subscription response, or $null if the subscription already existed.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$RegisterId,
        [Parameter(Mandatory)][hashtable]$SysAdminHeaders,
        [string]$RegisterName = "",
        [string]$SysAdminEmail = "",
        [string]$SysAdminDisplayName = "System Administrator"
    )

    $publicOrgId = "00000000-0000-0000-0000-000000000002"

    # Resolve the sysadmin email from the bearer token if not supplied.
    if (-not $SysAdminEmail) {
        $authHeader = $SysAdminHeaders["Authorization"]
        if ($authHeader -and $authHeader.StartsWith("Bearer ")) {
            $token = $authHeader.Substring(7)
            $parts = $token.Split('.')
            if ($parts.Count -ge 2) {
                $payload = $parts[1].Replace('-', '+').Replace('_', '/')
                switch ($payload.Length % 4) {
                    2 { $payload += '==' }
                    3 { $payload += '=' }
                }
                try {
                    $json = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload))
                    $claims = $json | ConvertFrom-Json
                    if ($claims.email) { $SysAdminEmail = $claims.email }
                } catch {
                    Write-WtWarn "Failed to decode sysadmin JWT for email claim: $_"
                }
            }
        }
    }

    if (-not $SysAdminEmail) {
        throw "Add-SorchaPublicOrgSubscription requires SysAdminEmail or a bearer token carrying an 'email' claim"
    }

    # 1. Ensure sysadmin is a member of the public org.
    Write-WtInfo "Ensuring sysadmin '$SysAdminEmail' is an admin of the Public org"
    try {
        $users = Invoke-SorchaApi -Method GET `
            -Uri "$TenantUrl/organizations/$publicOrgId/users?includeInactive=true" `
            -Headers $SysAdminHeaders
        $existing = $users.users | Where-Object { $_.email -eq $SysAdminEmail } | Select-Object -First 1
    } catch {
        Write-WtWarn "Could not list public org users: $($_.Exception.Message)"
        $existing = $null
    }

    if (-not $existing) {
        try {
            $addBody = @{
                email              = $SysAdminEmail
                displayName        = $SysAdminDisplayName
                externalIdpSubject = "sysadmin-publicorg-" + [guid]::NewGuid().ToString().Substring(0, 8)
                roles              = @("Administrator")
            }
            $null = Invoke-SorchaApi -Method POST `
                -Uri "$TenantUrl/organizations/$publicOrgId/users" `
                -Body $addBody `
                -Headers $SysAdminHeaders
            Write-WtSuccess "Sysadmin added as Administrator of the Public org"
        } catch {
            $statusCode = $null
            try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}
            if ($statusCode -eq 409 -or $statusCode -eq 400) {
                Write-WtWarn "Sysadmin already a member of the Public org — continuing"
            } else {
                throw
            }
        }
    } else {
        Write-WtInfo "Sysadmin already a member of the Public org"
    }

    # 2. Subscribe the public org to the register (idempotent — 409 = OK).
    $body = @{
        register_id       = $RegisterId
        register_name     = $RegisterName
        subscription_type = "Public"
    }

    try {
        $response = Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/organizations/$publicOrgId/register-subscriptions" `
            -Body $body `
            -Headers $SysAdminHeaders
        Write-WtSuccess "Public org subscribed to register $RegisterId"
        return $response
    } catch {
        $statusCode = $null
        try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}
        if ($statusCode -eq 409) {
            Write-WtWarn "Public org already subscribed to register $RegisterId — continuing"
            return $null
        }
        throw
    }
}

# ============================================================================
# Switch-SorchaOrganization — Switch user's active org and get new JWT
# ============================================================================

function Switch-SorchaOrganization {
    <#
    .SYNOPSIS
        Switch a user's active organization and get a new JWT scoped to that org.
    .DESCRIPTION
        Uses POST /api/auth/switch-org. The user must be a member of the target org.
    .RETURNS
        Hashtable with Token, UserId, OrganizationId, Headers.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$OrganizationId,
        [Parameter(Mandatory)][hashtable]$Headers
    )

    $body = @{ organizationId = $OrganizationId }

    $response = Invoke-SorchaApi -Method POST `
        -Uri "$TenantUrl/auth/switch-org" `
        -Body $body `
        -Headers $Headers

    $token = $response.access_token
    if (-not $token) { $token = $response.token }
    $jwt = Decode-SorchaJwt -Token $token

    Write-WtSuccess "Switched to org $($jwt.org_id)"

    return @{
        Token          = $token
        UserId         = $jwt.sub
        OrganizationId = $jwt.org_id
        WalletAddress  = $jwt.wallet_address
        Headers        = @{ Authorization = "Bearer $token" }
    }
}
