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

    # Allow running walkthroughs against a deployed HTTPS node from a
    # developer box that cannot check CRLs (e.g. Windows Schannel offline).
    # Opt in with SORCHA_WT_SKIP_CERT_CHECK=1.
    if ([System.Environment]::GetEnvironmentVariable('SORCHA_WT_SKIP_CERT_CHECK') -eq '1' -and
        $Uri -match '^https://') {
        $params.SkipCertificateCheck = $true
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
        # Deploy-anywhere override: point the walkthrough at any node by its gateway base URL
        # (e.g. http://tiny:8090, https://n2.example.dev) without adding a new profile enum.
        # When set, all service URLs derive from it as "{GatewayUrl}/api" and the profile is ignored.
        [string]$GatewayUrl,
        [switch]$SkipHealthCheck
    )

    $env = @{ Profile = $Profile }

    # Explicit gateway URL overrides the profile presets — deploy against any node without code edits.
    if ($GatewayUrl) {
        $base = $GatewayUrl.TrimEnd('/')
        $env.Profile     = 'custom'
        $env.GatewayUrl  = $base
        $env.TenantUrl   = "$base/api"
        $env.BlueprintUrl = "$base/api"
        $env.RegisterUrl = "$base/api"
        $env.WalletUrl   = "$base/api"
    }
    else {
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
    }

    if (-not $SkipHealthCheck) {
        # Check Docker — only for local profiles. Remote profiles (n1) target a
        # hosted environment where the local Docker daemon is irrelevant.
        if ($Profile -ne 'n1') {
            Write-WtInfo "Checking Docker availability..."
            $dockerInfo = docker info 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "Docker is not running. Start Docker Desktop and run: docker-compose up -d"
            }
            Write-WtSuccess "Docker is running"
        }
        else {
            Write-WtInfo "Profile = n1 — skipping local Docker check"
        }

        # Check API Gateway health
        Write-WtInfo "Checking API Gateway health..."
        try {
            $healthParams = @{
                Uri             = "$($env.GatewayUrl)/api/health"
                Method          = 'GET'
                TimeoutSec      = 10
                UseBasicParsing = $true
            }
            if ([System.Environment]::GetEnvironmentVariable('SORCHA_WT_SKIP_CERT_CHECK') -eq '1' -and
                $env.GatewayUrl -match '^https://') {
                $healthParams.SkipCertificateCheck = $true
            }
            $null = Invoke-RestMethod @healthParams
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

    # Cache at module scope so downstream helpers (e.g. New-SorchaRegister's
    # auto public-org subscription) can find TenantUrl without requiring every
    # caller to pass it explicitly.
    $script:LastEnvironment = $env

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
    .PARAMETER Profile
        Optional deployment profile (e.g. "n1"). When supplied, overrides from
        the top-level `_profiles.<profile>` object take precedence over the
        walkthrough's base credentials. Lets a single passwords.json file
        serve both local-Docker and remote deployments without edits between
        runs.
    .RETURNS
        Hashtable with credential key-value pairs.
    #>
    param(
        [Parameter(Mandatory)][string]$WalkthroughName,
        [string]$SecretsDir = "",
        [string]$Profile = ""
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

    # Profile overrides — global `_profiles.<profile>` wins over walkthrough
    # base keys when present. Used only when a deployment has rotated the
    # seed admin password; the default case (no _profiles block, or no entry
    # for this profile) is silent because base credentials work on every
    # stack DatabaseInitializer has seeded.
    if ($Profile -and $allSecrets._profiles) {
        $profileOverrides = $allSecrets._profiles.$Profile
        if ($profileOverrides) {
            foreach ($prop in $profileOverrides.PSObject.Properties) {
                $result[$prop.Name] = $prop.Value
            }
            Write-WtInfo "Applied profile overrides from _profiles.$Profile"
        }
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

    # Well-known System Admin org id — DatabaseInitializer creates this on
    # first startup and the seed admin is always a member. Used as the
    # target for the two-step login fallback when the sysadmin has been
    # added to additional orgs (e.g. Add-SorchaPublicOrgSubscription adds
    # the sysadmin to the Public org, which breaks the OAuth password
    # grant's "single org" fast path).
    $systemAdminOrgId = "00000000-0000-0000-0000-000000000001"

    $token = ""
    $organizationId = ""
    $adminUserId = ""

    $encodedPassword = [Uri]::EscapeDataString($AdminPassword)
    $loginBody = "grant_type=password&username=$AdminEmail&password=$encodedPassword&client_id=sorcha-cli"

    try {
        $loginResponse = Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/service-auth/token" `
            -Body $loginBody `
            -ContentType "application/x-www-form-urlencoded"
        $token = $loginResponse.access_token
        Write-WtSuccess "Logged in as $AdminEmail (OAuth password grant)"
    } catch {
        $statusCode = $null
        try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}

        if ($statusCode -ne 401) { throw }

        # OAuth password grant returns 401 when the user has >1 org (the
        # Tenant Service can't auto-pick). Fall back to the two-step
        # /auth/login + /auth/select-org flow and target the System Admin
        # org explicitly.
        Write-WtWarn "OAuth password grant returned 401 — falling back to two-step login"

        $adminSession = Connect-SorchaUser `
            -TenantUrl $TenantUrl `
            -Email $AdminEmail `
            -Password $AdminPassword `
            -OrganizationId $systemAdminOrgId

        $token = $adminSession.Token
        $organizationId = $adminSession.OrganizationId
        $adminUserId = $adminSession.UserId
        Write-WtSuccess "Logged in as $AdminEmail -> System Admin org"
    }

    # Extract org info from JWT if not already populated by the fallback path.
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

    # Single-org user: token returned directly.
    #
    # The server picks the org here — there is no selection step — so the token may be scoped to an
    # org OTHER than the one the caller asked for. That is always a bug in the caller's setup (the
    # user was never added to the org they are about to act in), and it used to pass silently: the
    # session looked fine and every later call against the requested org failed with a bare 403,
    # pointing at permissions rather than at the wrong-org session. Strathcarron lost a whole setup
    # run to it — the council admin existed only in the Public org, so master-key provisioning 403'd
    # four steps later (#1427). Fail here, where the cause is still visible.
    if ($loginResponse.access_token) {
        $token = $loginResponse.access_token
        $jwt = Decode-SorchaJwt -Token $token
        if ($jwt.org_id -and $OrganizationId -and $jwt.org_id -ne $OrganizationId) {
            throw ("Connect-SorchaUser: $Email is single-org in $($jwt.org_id), but org $OrganizationId was requested. " +
                   "The user is not a member of the requested org, so every org-scoped call will 403. " +
                   "Provision them into it with New-SorchaOrgUser (org-scoped, single-org) rather than " +
                   "registering a public account and hoping org creation adopts it.")
        }
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
# New-SorchaOrgUser — provision an org-scoped password user (spec 136)
# ============================================================================

function New-SorchaOrgUser {
    <#
    .SYNOPSIS
        Provision a NEW org-scoped password user directly into an org via
        POST /organizations/{orgId}/users/provision — single-org (no public account, no
        invitation, no email loop). Use for org OPERATORS (analysts, officers, issuers) so they
        are single-org and avoid the multi-org OAuth-grant 401. Citizens use Register-SorchaPublicUser.
    .NOTES
        -EmailVerified requires the installation to enable Platform:AllowAdminVerifiedUserCreation
        (dev + n1 enable it; production does not).
    .RETURNS
        Hashtable with UserId, Email.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$OrganizationId,
        [Parameter(Mandatory)][string]$Email,
        [Parameter(Mandatory)][string]$Password,
        [Parameter(Mandatory)][string]$DisplayName,
        [Parameter(Mandatory)][hashtable]$Headers,
        [string[]]$Roles = @("Consumer"),
        [switch]$EmailVerified
    )

    $body = @{
        email         = $Email
        displayName   = $DisplayName
        password      = $Password
        roles         = $Roles
        emailVerified = [bool]$EmailVerified
    }

    $response = Invoke-SorchaApi -Method POST `
        -Uri "$TenantUrl/organizations/$OrganizationId/users/provision" `
        -Body $body `
        -Headers $Headers

    Write-WtSuccess "Org-scoped user provisioned: $DisplayName (ID: $($response.id))"
    return @{ UserId = $response.id; Email = $Email }
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
        Get-or-create an ED25519 wallet for the calling user, idempotent by name.

    .DESCRIPTION
        Lists the caller's wallets and reuses any wallet whose Name matches
        $Name. Creates a new wallet only if no match exists. This means
        walkthroughs can compose — running ForestryCertification then
        TradeFinance for the same sales-mgr@highland-timber user produces
        ONE wallet across both walkthroughs, and the DPP credential issued
        by Forestry is visible to TradeFinance's Invoice-Finance phase.

        Mnemonic is returned only when the wallet is freshly created — on
        reuse it's null (the seed was shown to the user at creation time;
        the system does not store it).

    .RETURNS
        Hashtable with Address, Mnemonic, PublicKey, Reused.
    #>
    param(
        [Parameter(Mandatory)][string]$WalletUrl,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][hashtable]$Headers,
        [string]$Algorithm = "ED25519",
        [int]$WordCount = 12,
        [switch]$FetchPublicKey
    )

    # Look for an existing wallet with this name owned by the caller.
    try {
        $existing = Invoke-SorchaApi -Method GET `
            -Uri "$WalletUrl/v1/wallets/" `
            -Headers $Headers

        $match = $null
        if ($existing) {
            $match = @($existing) | Where-Object { $_.name -eq $Name } | Select-Object -First 1
        }

        if ($match) {
            Write-WtInfo "Wallet '$Name' already exists: $($match.address) — reusing"

            $publicKey = $match.publicKey
            # Older wallet records may not include publicKey on list — fall back
            # to a probe sign so callers get a non-null value.
            if (-not $publicKey -and $FetchPublicKey) {
                $probeBody = @{
                    transactionData = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes("key-probe"))
                    isPreHashed     = $false
                }
                $signResponse = Invoke-SorchaApi -Method POST `
                    -Uri "$WalletUrl/v1/wallets/$($match.address)/sign" `
                    -Body $probeBody `
                    -Headers $Headers
                $publicKey = $signResponse.publicKey
            }

            return @{
                Address   = $match.address
                Mnemonic  = $null
                PublicKey = $publicKey
                Reused    = $true
            }
        }
    } catch {
        # Lookup failed — fall through to create. Don't fail the whole call on
        # a transient list error; New-SorchaWallet should always end with a
        # usable wallet.
        Write-WtWarn "Wallet lookup failed before create: $($_.Exception.Message) — proceeding to create"
    }

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
        Reused    = $false
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
# Set-SorchaOrgMasterKey — provision the org's Feature 083 HD master key (idempotent)
# ============================================================================

function Set-SorchaOrgMasterKey {
    <#
    .SYNOPSIS
        Provision the organisation's Feature 083 HD master key (idempotent).
    .DESCRIPTION
        POST /api/wallets/org/{orgId}/master-key. REQUIRED for any org that ISSUES credentials:
        without a master key the org can't derive its Feature 120 VC-issuance key, so blueprint-
        issued SorchaLocalWallet credentials are signed with the bare wallet key (iss = bare
        address, no did:sorcha kid). Such credentials fail cross-register presentation — the
        engine TrustEvaluator rejects them with "issuer signature not verified" (fail-closed),
        breaking credential-chain walkthroughs (TradeFinance Finance, SelfBuildHouse warrant).
        The server deliberately does NOT auto-provision master keys (they mint a recovery
        mnemonic that must be backed up — an explicit org-setup step), so the walkthrough must.
        Requires an Administrator + platform-audience token (the org admin, re-logged so it carries
        wallet_address). Idempotent: returns silently on 409 (already provisioned). The one-time
        recovery mnemonic in the response is intentionally discarded (walkthrough/dev only).
    #>
    param(
        [Parameter(Mandatory)][string]$WalletUrl,
        [Parameter(Mandatory)][string]$OrganizationId,
        [Parameter(Mandatory)][hashtable]$Headers
    )
    try {
        $null = Invoke-SorchaApi -Method POST `
            -Uri "$WalletUrl/wallets/org/$OrganizationId/master-key" `
            -Headers $Headers
        Write-WtInfo "  Org master key provisioned for $OrganizationId (enables Feature 120 VC-issuance key)"
    } catch {
        $status = $null
        try { $status = $_.Exception.Response.StatusCode.value__ } catch {}
        if ($status -eq 409) {
            Write-WtInfo "  Org master key already provisioned for $OrganizationId"
        } else {
            throw
        }
    }

    # The master key ENABLES the Feature 120 issuance key; it does not DERIVE it. Until the
    # issuance key exists, the org has no published DID document at all — GET /orgs/{id}/did.json
    # 404s — because the document is (re)generated as a side effect of a key event.
    #
    # That is a bootstrap hole for any walkthrough that must PIN the issuer's DID in a blueprint
    # trust policy: on a freshly-created org the DID cannot be resolved before the org's first
    # credential is issued, yet the blueprint that issues it needs the DID at publish time.
    # (This stayed hidden until n1 was wiped on 2026-08-17: previously these orgs had already
    # issued credentials, so the key — and the document — existed as a side effect.)
    #
    # `issuance-key/ensure` is the platform's designed answer: idempotent, derives on first call,
    # and "Triggers DID document regeneration on the Tenant side as a side effect."
    try {
        $ensure = Invoke-SorchaApi -Method POST `
            -Uri "$WalletUrl/v1/orgs/$OrganizationId/issuance-key/ensure" `
            -Headers $Headers
        Write-WtInfo "  Org issuance key ready for $OrganizationId (rotation $($ensure.rotationIndex), $($ensure.algorithm)) — DID document published"
    } catch {
        Write-WtWarn ("  Could not ensure the org issuance key for $OrganizationId — the org's " +
                      "did.json will not resolve until its first credential is issued, so any " +
                      "trust policy pinning its issuance DID cannot be built yet. $($_.Exception.Message)")
    }
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

        # Wait for the publish tx to seal before returning. Setup scripts that
        # immediately start run.ps1 will otherwise hit a 403 on Action 1 — the
        # auth check looks up the participant record, gets a 404 because the
        # publish tx hasn't sealed yet, and the auth layer wraps that as 403.
        # Setup is single-threaded and not a hot path; the few-seconds wait
        # for seal is harmless and prevents a real race-condition failure.
        if ($response.transactionId) {
            $gatewayUrl = $TenantUrl -replace '/api$', ''
            Wait-SorchaActorReady -Mode ParticipantSealed `
                -TxId $response.transactionId `
                -RegisterId $RegisterId `
                -Headers $Headers `
                -GatewayUrl $gatewayUrl `
                -TimeoutSeconds 90
        }

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
    .PARAMETER DevMode
        When set, creates the register with devMode=true so payloads are stored as
        plaintext with disclosure filtering applied at read time. Use for flows
        that have anonymous/late-bound recipients whose keys cannot be published
        (e.g. the HaipVerifiedCitizen walkthrough). Dev mode is a one-way switch —
        it can be disabled via POST /api/registers/{id}/disable-dev-mode but never
        re-enabled. That promotion is ASYNCHRONOUS: it submits a crypto-policy control
        transaction through the Validator, and the register's devMode flag flips on each
        node only once that transaction seals into a docket. Poll GET /api/registers/{id}
        rather than assuming the 200 response means it has taken effect. Payloads already
        sealed in DevMode remain plaintext permanently — promotion is not retrospective.
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
        # Optional auth context for the wallet attestation sign call. Register
        # creation (initiate/finalize) is an org-admin operation, but the
        # `/v1/wallets/{addr}/sign` call requires the wallet OWNER. When the
        # owner wallet belongs to a different user from the caller (e.g.
        # walkthroughs where each participant owns their own wallet but the
        # org admin sets up the register), pass that user's session headers
        # here. Defaults to the same Headers when not supplied — preserves
        # the older "admin owns everything" calling pattern.
        [hashtable]$WalletSignerHeaders,
        [hashtable]$Metadata = @{},
        # Optional: tenant URL used to auto-subscribe the Sorcha Public Org
        # (well-known ID 00000000-0000-0000-0000-000000000002). When provided,
        # the Public Org is subscribed as "Public" immediately after finalize
        # so consumer-persona and public-discovery flows have access by default.
        [string]$TenantUrl,
        [switch]$SkipPublicOrgSubscription,
        [switch]$DevMode
    )

    if (-not $WalletSignerHeaders) { $WalletSignerHeaders = $Headers }

    # Idempotency short-circuit: if the caller's current identity is already
    # subscribed to a register with this exact name, reuse it instead of creating
    # a duplicate. This keeps re-running setup.ps1 cheap and avoids register
    # proliferation during development. Only the current user's subscriptions
    # are consulted — cross-peer discovery is deferred until the peer network
    # exposes a lookup API.
    $lookupTenantUrl = if ($TenantUrl) { $TenantUrl } `
                       elseif ($script:LastEnvironment) { $script:LastEnvironment.TenantUrl } `
                       else { $null }

    if ($lookupTenantUrl) {
        try {
            $existingRegisterId = Get-SorchaRegisterByName `
                -TenantUrl $lookupTenantUrl `
                -Name $Name `
                -Headers $Headers
        } catch {
            $existingRegisterId = $null
        }

        if ($existingRegisterId) {
            Write-WtSuccess "Register '$Name' already exists: $existingRegisterId (reusing)"
            return @{
                RegisterId           = $existingRegisterId
                GenesisTransactionId = $null
                Reused               = $true
            }
        }
    }

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

    if ($DevMode) {
        $initiateBody.devMode = $true
        Write-WtInfo "  DevMode: enabled (payloads stored plaintext)"
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

        # derivationPath is load-bearing: slot 100 is the organisation's GOVERNANCE key. The
        # register's roster records whatever key signs the attestation, and the Validator
        # authorises later governance control transactions (crypto-policy updates, roster
        # proposals, approvals) by matching against it. Omitting it signs with the wallet's
        # primary key and the register becomes ungovernable — silently, at creation time.
        $signBody = @{
            transactionData = $dataToSignBase64
            derivationPath  = "sorcha:register-attestation"
            isPreHashed     = $true
        }

        $signResponse = Invoke-SorchaApi -Method POST `
            -Uri "$WalletUrl/v1/wallets/$($att.walletId)/sign" `
            -Body $signBody `
            -Headers $WalletSignerHeaders

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

    # Owner subscription is now created server-side by the Register Service's
    # finalize endpoint via a service-to-service call to the Tenant Service
    # internal subscription endpoint. No client-side subscribe call is needed
    # or even possible for service-principal callers (they don't have the
    # Administrator role required by the public subscribe endpoint).

    # Auto-subscribe Sorcha Public Org (well-known ID) so consumer-persona
    # and public-discovery flows can access the register by default.
    # TenantUrl resolution order: explicit parameter → cached environment.
    if (-not $SkipPublicOrgSubscription) {
        $resolvedTenantUrl = if ($TenantUrl) { $TenantUrl } `
                             elseif ($script:LastEnvironment) { $script:LastEnvironment.TenantUrl } `
                             else { $null }

        if ($resolvedTenantUrl) {
            $publicOrgId = "00000000-0000-0000-0000-000000000002"
            try {
                $null = New-SorchaRegisterSubscription `
                    -TenantUrl $resolvedTenantUrl `
                    -OrganizationId $publicOrgId `
                    -RegisterId $registerId `
                    -RegisterName $Name `
                    -SubscriptionType "Public" `
                    -Headers $Headers
            } catch {
                Write-WtWarn "  Public org auto-subscribe failed for register '$Name': $($_.Exception.Message)"
            }
        } else {
            Write-WtWarn "  Skipping public org subscription for '$Name' — no TenantUrl available (pass -TenantUrl or call Initialize-SorchaEnvironment first)"
        }
    }

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
        [string]$RegisterId,
        # F142 publish-gate: walkthroughs don't run a UI rehearsal, so they
        # default to publishing with the audited soft-gate override. The
        # caller still has to pass the F142 governance HARD gate (their wallet
        # must match an Owner/Admin/Designer on the register roster) — only
        # the rehearsal soft gate is bypassed.
        [switch]$OverrideRehearsal = $true,
        [string]$OverrideReason = "Walkthrough automation: rehearsal not wired in scripted flow."
    )

    # Load template
    Write-WtInfo "Loading blueprint template: $TemplatePath"

    if (-not (Test-Path $TemplatePath)) {
        throw "Blueprint template not found: $TemplatePath"
    }

    $templateJson = Get-Content -Path $TemplatePath -Raw | ConvertFrom-Json -Depth 30
    # Templates may be wrapped (`{ "template": {...} }`) or flat (the
    # blueprint object at the top level). Tolerate both.
    if ($templateJson.PSObject.Properties.Name -contains "template" -and $null -ne $templateJson.template) {
        $blueprint = $templateJson.template
    } else {
        $blueprint = $templateJson
    }

    # Feature 103: identify open starting-action senders. The publish-time
    # guardrail (VAL_BP_010) rejects any blueprint where an open starting
    # action's sender participant has a non-null walletAddress — the
    # participant must be late-bound to the first qualifying submitter at
    # runtime. Auto-skip patching for these participants so walkthrough
    # authors don't need to know this rule.
    $openSenders = @()
    foreach ($action in $blueprint.actions) {
        if ($action.isStartingAction -and $action.sender) {
            $openSenders += $action.sender
        }
    }
    $openSenders = $openSenders | Sort-Object -Unique

    # Patch wallet addresses in-memory
    foreach ($participant in $blueprint.participants) {
        if ($openSenders -contains $participant.id) {
            Write-WtInfo "  Skipped $($participant.id) (open participant — late-bound at runtime)"
            continue
        }
        if ($WalletMap.ContainsKey($participant.id)) {
            $participant | Add-Member -NotePropertyName "walletAddress" -NotePropertyValue $WalletMap[$participant.id] -Force
            Write-WtInfo "  Patched $($participant.id) -> $($WalletMap[$participant.id])"
        }
    }

    # Generate unique ID. Use Add-Member -Force so this is robust whether
    # the template carries a placeholder `id` or omits it entirely (most
    # templates do — the id is generated here per-publish).
    $timestamp = Get-Date -Format "yyyyMMddHHmmss"
    $blueprint | Add-Member -NotePropertyName "id" -NotePropertyValue "$IdPrefix-$timestamp" -Force

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

    $publishBodyHash = @{}
    if ($RegisterId) { $publishBodyHash.registerId = $RegisterId }
    if ($OverrideRehearsal) {
        $publishBodyHash.override = @{ confirm = $true; reason = $OverrideReason }
    }
    $publishBody = if ($publishBodyHash.Count -gt 0) { $publishBodyHash | ConvertTo-Json -Depth 5 } else { $null }

    $publishRaw = Invoke-SorchaApi -Method POST `
        -Uri "$BlueprintUrl/blueprints/$blueprintId/publish" `
        -Body $publishBody `
        -Headers $Headers `
        -RawResponse

    # Fail-loud guard: Invoke-WebRequest in PS 7+ throws on 4xx/5xx by default,
    # but defense-in-depth — if a status ever slips through (proxy transform,
    # custom 2xx-but-error JSON, future PS changes) make the script exit
    # cleanly instead of silently proceeding to instance creation against a
    # blueprint that was never actually published. This was the root cause of
    # the AssuredIdentity "Action 1 not confirmed" 60s timeout: a 403 from the
    # F142 PublishGate was swallowed and the citizen flow polled for a tx
    # that never existed.
    $publishStatus = [int]$publishRaw.StatusCode
    if ($publishStatus -ne 200 -and $publishStatus -ne 201) {
        $body = ""
        try { $body = $publishRaw.Content } catch { }
        throw "Blueprint publish failed for $blueprintId : HTTP $publishStatus, body=$body"
    }

    $publishResponse = $publishRaw.Content | ConvertFrom-Json

    $warnings = @()
    if ($publishResponse.warnings -and ($publishResponse.warnings | Measure-Object).Count -gt 0) {
        $warnings = @($publishResponse.warnings)
        foreach ($w in $warnings) {
            Write-WtWarn "  $w"
        }
    }

    Write-WtSuccess "Blueprint published: $blueprintId"

    # Wait for the publish tx to seal before returning, when we know the
    # register and have a transaction id on the response. Setup scripts that
    # immediately start run.ps1 would otherwise race the validator — the
    # action engine would look up the blueprint and find a not-yet-sealed
    # record. Setup is single-threaded and not a hot path; a few-seconds
    # wait per publish is harmless.
    if ($RegisterId -and $publishResponse.transactionId) {
        $gatewayUrl = $BlueprintUrl -replace '/api$', ''
        Wait-SorchaActorReady -Mode BlueprintSealed `
            -TxId $publishResponse.transactionId `
            -RegisterId $RegisterId `
            -Headers $Headers `
            -GatewayUrl $gatewayUrl `
            -TimeoutSeconds 90
    }

    return @{
        BlueprintId   = $blueprintId
        Title         = $createResponse.title
        Warnings      = $warnings
        TransactionId = $publishResponse.transactionId
    }
}

# ============================================================================
# T016: Invoke-SorchaAction — Execute or Reject an Action
# ============================================================================

# Feature 145 cadence: action submission is single-async — the previous action's /execute returns
# 202 and the instance only advances once the InstanceProjector folds the sealed docket (a beat
# later). A script that submits the next action immediately races the projector and gets a transient
# 400 "Action N is not a current action for instance". Rather than make every walkthrough gate each
# actor switch on AwaitingInbox, this wraps the submit POST and retries ONLY that transient error
# (bounded), so the cadence self-heals everywhere. Any other error (schema 400, auth, etc.) is
# rethrown immediately.
function Invoke-SorchaActionPostWithCadenceRetry {
    param(
        [string]$Method,
        [string]$Uri,
        [hashtable]$Body,
        [hashtable]$Headers,
        [string]$ActionId,
        [int]$MaxAttempts = 15,
        [int]$DelaySeconds = 1
    )
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            return Invoke-SorchaApi -Method $Method -Uri $Uri -Body $Body -Headers $Headers
        } catch {
            $errBody = $null
            try { $errBody = Get-SorchaErrorBody -Exception $_.Exception } catch {}
            if ($attempt -lt $MaxAttempts -and $errBody -and ($errBody -match 'not a current action')) {
                if ($attempt -eq 1) {
                    Write-WtInfo "  Action $ActionId not yet current (projector folding) — waiting…"
                }
                Start-Sleep -Seconds $DelaySeconds
                continue
            }
            throw
        }
    }
}

function Wait-SorchaRegisterRoster {
    <#
    .SYNOPSIS
        Block until a newly-created register's genesis governance roster has sealed.
    .DESCRIPTION
        The register's genesis control transaction — which records the owner governance roster
        the F142 publish gate reads — seals ASYNCHRONOUSLY after New-SorchaRegister returns. A
        blueprint publish issued immediately reads an EMPTY roster and fail-closes with

            403 "You do not hold a publish-governance role (Owner, Admin, or Designer)
                 on the target register."

        That message reads like a ROLE problem, so it sends you looking at the caller's roles and
        its `wallet_address` claim — both of which are usually fine. The actual cause is timing,
        and it is load-bearing whether a walkthrough happens to have other work between creating
        the register and publishing: ConstructionPermit and Forestry get away with it, while
        TradeFinance and Strathcarron 403 every time.

        Poll until at least one roster member exists before publishing.
    #>
    param(
        [Parameter(Mandatory)][string]$GatewayUrl,
        [Parameter(Mandatory)][string]$RegisterId,
        [Parameter(Mandatory)][hashtable]$Headers,
        [int]$TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $started  = Get-Date
    $polls    = 0

    while ((Get-Date) -lt $deadline) {
        $polls++
        try {
            $roster = Invoke-SorchaApi -Method GET `
                -Uri "$GatewayUrl/api/registers/$RegisterId/governance/roster" `
                -Headers $Headers
            $count = @($roster.members).Count
            if ($roster -and $roster.members -and $count -gt 0) {
                $elapsed = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
                Write-WtInfo "  Governance roster sealed ($count member(s)) in ${elapsed}s ($polls polls)"
                return $true
            }
        } catch {
            # The roster endpoint 404s until the genesis tx lands; keep polling.
        }
        Start-Sleep -Seconds 2
    }

    Write-WtWarn ("  Governance roster for register $RegisterId did not seal within ${TimeoutSeconds}s. " +
                  "A blueprint publish now will 403 with a publish-governance-role message that " +
                  "actually means the roster is still empty.")
    return $false
}

function Resolve-SorchaCollection {
    <#
    .SYNOPSIS
        Return the item collection from a response that may be either a bare JSON array or an
        envelope object carrying a named collection property.
    .DESCRIPTION
        Sorcha list endpoints are not uniform: some return a bare array, others an envelope
        (`{ "credentials": [...] }` / `{ "items": [...] }`). Walkthroughs handled that with

            $items = if ($response.credentials) { $response.credentials } else { $response }

        which is SILENTLY WRONG for a bare array of two or more elements. `$array.credentials`
        is PowerShell **member enumeration**: it projects `credentials` from every element and,
        when no element has it, returns an array of $nulls. That array has Count > 0 and is
        therefore TRUTHY, so the `if` takes the envelope branch and `$items` becomes a
        same-length collection of $nulls. Every downstream `Where-Object { $_.type -eq ... }`
        then matches nothing, and the walkthrough reports "not delivered" for data the platform
        returned correctly.

        The bug is invisible at one element (a single $null is falsy, so the correct branch is
        taken) and appears at two — so it presents as a walkthrough that passes on a clean node
        and fails on the second run. Test for the array FIRST; only then look for the property.
    #>
    param(
        [Parameter(Mandatory)][AllowNull()]$Response,
        [Parameter(Mandatory)][string]$PropertyName
    )

    if ($null -eq $Response) { return @() }

    # A bare array IS the collection — check before ANY member access on $Response.
    if ($Response -is [System.Collections.IList]) { return @($Response) }

    $prop = $Response.PSObject.Properties[$PropertyName]
    if ($prop -and $null -ne $prop.Value) { return @($prop.Value) }

    return @($Response)
}

function Resolve-SorchaAsyncTransactionId {
    <#
    .SYNOPSIS
        Resolve the transaction id for an action whose encryption was offloaded to the
        background service (the 202 "encryption offload" path).
    .DESCRIPTION
        `/actions/{id}/execute` has TWO distinct 202 shapes, and only one of them carries a
        transaction id:

          * normal async  — `isAsync=true`, `transactionId` POPULATED (the tx is already built).
          * encryption offload — `isAsync=true`, `transactionId` is the EMPTY STRING, and only
            `operationId` is set. `ActionExecutionService` returns this whenever the action has
            recipient disclosure groups to encrypt; the tx id is assigned later, by the
            background encryption service ("Will be filled by background service").

        An empty string is FALSY in PowerShell, so a caller doing
        `if ($WaitForSeal -and $response.transactionId)` silently skips the seal wait on the
        second shape — the cadence guard the whole walkthrough framework depends on becomes
        inert with no error and no log line, and the script races the validator exactly as it
        did before `-WaitForSeal` existed. Any assertion on `$response.transactionId` also
        fails on a submission the platform actually accepted and sealed.

        This resolver closes that gap: it polls `GET /api/operations/{operationId}` (the
        documented polling fallback for clients without SignalR) until the operation reports a
        `transactionHash`, which IS the transaction id — the F079 lifecycle endpoint echoes it
        back as `transactionId`, and the instance's `lastAppliedTxId` matches it.
    #>
    param(
        [Parameter(Mandatory)][string]$GatewayUrl,
        [Parameter(Mandatory)][string]$OperationId,
        [Parameter(Mandatory)][hashtable]$Headers,
        [int]$TimeoutSeconds = 90
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $polls = 0
    $started = Get-Date

    while ((Get-Date) -lt $deadline) {
        $polls++
        $op = $null
        try {
            $op = Invoke-SorchaApi -Method GET `
                -Uri "$GatewayUrl/api/operations/$OperationId" `
                -Headers $Headers
        } catch {
            # The operation row can lag the 202 by a beat; keep polling until the deadline.
            $op = $null
        }

        if ($op) {
            if ($op.error) {
                throw ("Encryption operation $OperationId failed: $($op.error)" +
                       $(if ($op.failedRecipient) { " (recipient: $($op.failedRecipient))" } else { "" }))
            }
            if (-not [string]::IsNullOrWhiteSpace($op.transactionHash)) {
                $elapsed = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
                Write-WtInfo ("  Async encryption op $($OperationId.Substring(0, [Math]::Min(10, $OperationId.Length)))… " +
                              "-> tx $($op.transactionHash.Substring(0, 10))… in ${elapsed}s ($polls polls)")
                return $op.transactionHash
            }
        }

        Start-Sleep -Seconds 1
    }

    throw ("Encryption operation $OperationId did not report a transactionHash within " +
           "${TimeoutSeconds}s. The action was accepted (HTTP 202) but its transaction id " +
           "could not be resolved, so the seal could not be verified.")
}

function Resolve-SorchaActionTransactionId {
    <#
    .SYNOPSIS
        Normalise an action-submission response so `.transactionId` is always meaningful.
    .DESCRIPTION
        On the encryption-offload 202 the platform returns an EMPTY `transactionId` plus an
        `operationId`. This fills the real id in (resolved via the operations endpoint) so
        every caller — seal waits, assertions, logging — can rely on `.transactionId`.
        Responses that already carry an id are returned untouched.
    #>
    param(
        [Parameter(Mandatory)][AllowNull()]$Response,
        [Parameter(Mandatory)][string]$BlueprintUrl,
        [Parameter(Mandatory)][hashtable]$Headers,
        [int]$TimeoutSeconds = 90
    )

    if (-not $Response) { return $Response }
    if (-not [string]::IsNullOrWhiteSpace($Response.transactionId)) { return $Response }
    if ([string]::IsNullOrWhiteSpace($Response.operationId)) { return $Response }

    $resolved = Resolve-SorchaAsyncTransactionId `
        -GatewayUrl ($BlueprintUrl -replace '/api$', '') `
        -OperationId $Response.operationId `
        -Headers $Headers `
        -TimeoutSeconds $TimeoutSeconds

    $Response | Add-Member -NotePropertyName transactionId -NotePropertyValue $resolved -Force
    return $Response
}

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
        [array]$CredentialPresentations = @(),
        [switch]$Reject,
        [string]$RejectionReason = "",
        # Wait for the submitted tx to seal before returning. This is the
        # actor-cadence bridge: real participants see SignalR-notified
        # inbound transactions, scripts see immediate HTTP responses; without
        # this gate the next action can submit during the validator's post-
        # seal cleanup and trigger the docket-monitoring race. Default off
        # for back-compat; new scripts should always set it.
        [switch]$WaitForSeal,
        [int]$WaitForSealTimeoutSeconds = 90
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

        $response = Invoke-SorchaActionPostWithCadenceRetry -Method POST `
            -Uri "$BlueprintUrl/instances/$InstanceId/actions/$ActionId/reject" `
            -Body $rejectBody `
            -Headers $executeHeaders `
            -ActionId $ActionId

        Write-WtSuccess "Action $ActionId REJECTED"

        $response = Resolve-SorchaActionTransactionId `
            -Response $response -BlueprintUrl $BlueprintUrl `
            -Headers $executeHeaders -TimeoutSeconds $WaitForSealTimeoutSeconds

        if ($WaitForSeal) {
            if ([string]::IsNullOrWhiteSpace($response.transactionId)) {
                throw ("Action $ActionId rejection was accepted but returned no transaction id " +
                       "(and no operationId to resolve one from), so -WaitForSeal cannot " +
                       "verify the seal. Refusing to continue silently.")
            }
            $gatewayUrl = $BlueprintUrl -replace '/api$', ''
            Wait-SorchaActorReady -Mode AfterSubmit `
                -TxId $response.transactionId `
                -RegisterId $RegisterId `
                -Headers $executeHeaders `
                -GatewayUrl $gatewayUrl `
                -TimeoutSeconds $WaitForSealTimeoutSeconds
        }

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

        if ($CredentialPresentations -and $CredentialPresentations.Count -gt 0) {
            $actionBody.credentialPresentations = @($CredentialPresentations)
        }

        $response = Invoke-SorchaActionPostWithCadenceRetry -Method POST `
            -Uri "$BlueprintUrl/instances/$InstanceId/actions/$ActionId/execute" `
            -Body $actionBody `
            -Headers $executeHeaders `
            -ActionId $ActionId

        $nextAction = if ($response.nextAction) { $response.nextAction } else { "complete" }
        Write-WtSuccess "Action $ActionId executed (next: $nextAction)"

        $response = Resolve-SorchaActionTransactionId `
            -Response $response -BlueprintUrl $BlueprintUrl `
            -Headers $executeHeaders -TimeoutSeconds $WaitForSealTimeoutSeconds

        if ($WaitForSeal) {
            if ([string]::IsNullOrWhiteSpace($response.transactionId)) {
                throw ("Action $ActionId was accepted but returned no transaction id " +
                       "(and no operationId to resolve one from), so -WaitForSeal cannot " +
                       "verify the seal. Refusing to continue silently.")
            }
            $gatewayUrl = $BlueprintUrl -replace '/api$', ''
            Wait-SorchaActorReady -Mode AfterSubmit `
                -TxId $response.transactionId `
                -RegisterId $RegisterId `
                -Headers $executeHeaders `
                -GatewayUrl $gatewayUrl `
                -TimeoutSeconds $WaitForSealTimeoutSeconds
        }

        return $response
    }
}

# ============================================================================
# Get-SorchaCredentialPresentation — Fetch a VC from a wallet and wrap it
# ============================================================================

function Get-SorchaCredentialPresentation {
    <#
    .SYNOPSIS
        Fetch an issued credential of a given type from a wallet and build a
        CredentialPresentation body that the blueprint service will accept.
    .PARAMETER WalletUrl
        Wallet service base URL (e.g., http://127.0.0.1/api).
    .PARAMETER WalletAddress
        The wallet address holding the credential.
    .PARAMETER CredentialType
        The credential type to find (e.g., "PlanningPermissionCredential").
    .PARAMETER Token
        Bearer token authorised for this wallet.
    .RETURNS
        A hashtable with credentialId, disclosedClaims, rawPresentation — or $null
        if no credential of that type was found.
    #>
    param(
        [Parameter(Mandatory)][string]$WalletUrl,
        [Parameter(Mandatory)][string]$WalletAddress,
        [Parameter(Mandatory)][string]$CredentialType,
        [Parameter(Mandatory)][string]$Token,
        # Pin an EXACT credential rather than "first of this type". Required whenever the assertion
        # depends on WHICH credential is presented — e.g. proving a REVOKED credential is refused.
        # A wallet accumulates credentials of the same type across runs, so selecting by type alone
        # silently presents whichever happens to be first, and a test that believes it is presenting
        # a revoked credential can be presenting an active one. That produced a false "revoked
        # credential accepted" reading on n1 (2026-08-17).
        [string]$CredentialId
    )

    $headers = @{ Authorization = "Bearer $Token" }

    # Feature 106 Wave C: register-native credential delivery lands as
    # Status=PendingAcceptance. The default /credentials list filters to Active
    # only, so pending credentials are invisible to the presentation flow until
    # the holder accepts. Walkthroughs are single-actor-per-role and don't have
    # a separate "inbox triage" step, so auto-accept pending credentials of the
    # requested type here before the Active fetch.
    try {
        $pending = Invoke-SorchaApi -Method GET `
            -Uri "$WalletUrl/v1/wallets/$WalletAddress/credentials/?status=PendingAcceptance" `
            -Headers $headers
        foreach ($p in ($pending | Where-Object { $_.type -eq $CredentialType })) {
            $null = Invoke-SorchaApi -Method PATCH `
                -Uri "$WalletUrl/v1/wallets/$WalletAddress/credentials/$($p.id)" `
                -Body @{ status = "Active" } `
                -Headers $headers
        }
    } catch {
        # Non-fatal: pending accept is best-effort. If the helper is being used
        # against a wallet that has no pending credentials, the fetch below will
        # return the already-active ones and the caller sees no difference.
    }

    # The default listing returns ACTIVE credentials only, which is the right holder-side default —
    # a wallet should not casually hand over a revoked credential. But pinning an exact -CredentialId
    # means the caller knows precisely which credential it wants, INCLUDING a revoked one: that is
    # the adversarial case a verifier must refuse (a holder who kept the token and presents it
    # anyway). Widen the query only in that case, so the default stays safe.
    $listUri = if ($CredentialId) {
        "$WalletUrl/v1/wallets/$WalletAddress/credentials/?status=All"
    } else {
        "$WalletUrl/v1/wallets/$WalletAddress/credentials/"
    }

    $credentials = Invoke-SorchaApi -Method GET -Uri $listUri -Headers $headers

    if (-not $credentials) { return $null }

    $match = if ($CredentialId) {
        $credentials | Where-Object { $_.id -eq $CredentialId } | Select-Object -First 1
    } else {
        $credentials | Where-Object { $_.type -eq $CredentialType } | Select-Object -First 1
    }
    if (-not $match) { return $null }

    $exported = Invoke-SorchaApi -Method GET `
        -Uri "$WalletUrl/v1/wallets/$WalletAddress/credentials/$($match.id)/export" `
        -Headers $headers

    $rawToken = $exported.rawToken
    if (-not $rawToken) { $rawToken = "placeholder-raw-token" }

    # Pull claims from the stored credential (used by the verifier's loose match).
    # The verifier only requires: type/vct match, issuer match (if constrained),
    # and any requiredClaims to be present in disclosedClaims.
    $details = Invoke-SorchaApi -Method GET `
        -Uri "$WalletUrl/v1/wallets/$WalletAddress/credentials/$($match.id)" `
        -Headers $headers

    $disclosed = @{
        type = $CredentialType
        vct  = $CredentialType
        iss  = $details.issuerDid
    }

    if ($details.claimsJson) {
        try {
            $parsed = $details.claimsJson | ConvertFrom-Json -AsHashtable
            foreach ($k in $parsed.Keys) { $disclosed[$k] = $parsed[$k] }
        } catch {
            # claimsJson malformed — fall back to the basic fields already set
        }
    }

    return @{
        credentialId    = $match.id
        disclosedClaims = $disclosed
        rawPresentation = $rawToken
    }
}

# ============================================================================
# New-SorchaOrganization — Create org via Platform Admin API
# ============================================================================

function Invoke-SorchaOrgWalletStep {
    <#
    .SYNOPSIS
        Internal: perform the org-wallet step during New-SorchaOrganization, as the org admin.
    .DESCRIPTION
        Returns the wallet address, or $null when the step was not applicable. Throws when the
        caller asked for it (-WalletUrl) and it genuinely failed — a silently-skipped org wallet is
        what #1518 was, surfacing much later as an issuer DID that does not resolve.
    #>
    param(
        [string]$TenantUrl,
        [string]$WalletUrl,
        [string]$OrganizationId,
        [string]$Subdomain,
        [string]$AdminEmail,
        [string]$AdminPassword,
        [bool]$AdminDirectlyAdded
    )

    if (-not $WalletUrl) { return $null }

    if (-not $AdminDirectlyAdded -or -not $AdminPassword) {
        # The admin was invited rather than provisioned, so there is no session to act as. Say so:
        # the org has no wallet and cannot issue until someone signs in and creates one.
        Write-WtWarn "Org $OrganizationId has no admin session available, so its wallet was not created."
        Write-WtWarn "Sign in as an admin of that org and call New-SorchaOrgWallet (#1525)."
        return $null
    }

    $adminSession = Connect-SorchaUser `
        -TenantUrl $TenantUrl -Email $AdminEmail -Password $AdminPassword `
        -OrganizationId $OrganizationId

    $result = New-SorchaOrgWallet `
        -TenantUrl $TenantUrl -WalletUrl $WalletUrl `
        -OrganizationId $OrganizationId -Headers $adminSession.Headers `
        -Name "org-$Subdomain-signing"

    return $result.WalletAddress
}

function New-SorchaOrgWallet {
    <#
    .SYNOPSIS
        Create the ORGANISATION's signing wallet, as its own admin (#1525).
    .DESCRIPTION
        The step that used to be missing everywhere, and the reason walkthroughs quietly depended on
        a background sweep. An organisation's canonical wallet is what its issuer DID anchors on and
        what its governance roster identity is matched against — and its BIP39 recovery phrase is
        shown ONCE and never stored. So it is created deliberately, by an administrator OF THAT
        ORGANISATION, who is then the only person holding the phrase. The platform will not do it:
        a service-to-service create generates a phrase with nobody present to receive it.

        Create-then-link, so the phrase never transits the Tenant Service:
          1. POST {WalletUrl}/v1/wallets with organizationId  -> address + mnemonicWords
          2. POST {TenantUrl}/organizations/{id}/wallet       -> records it as the org's wallet

        Idempotent: if the organisation already has a wallet this returns it and creates nothing,
        because replacing the canonical wallet would orphan every credential issued under the old one.

        Headers MUST be the ORG ADMIN's session — a platform admin is refused by design.
    .RETURNS
        Hashtable with WalletAddress, Mnemonic (empty when the wallet already existed), Created.
    #>
    param(
        [Parameter(Mandatory)][string]$TenantUrl,
        [Parameter(Mandatory)][string]$WalletUrl,
        [Parameter(Mandatory)][string]$OrganizationId,
        [Parameter(Mandatory)][hashtable]$Headers,
        [string]$Algorithm = "ED25519",
        [string]$Name
    )

    if (-not $Name) { $Name = "org-$OrganizationId-signing" }

    # Already done? Ask the org, not our own bookkeeping.
    try {
        $org = Invoke-SorchaApi -Method GET `
            -Uri "$TenantUrl/organizations/$OrganizationId" -Headers $Headers
        if ($org.walletAddress) {
            Write-WtInfo "  Org wallet already exists: $($org.walletAddress)"
            return @{ WalletAddress = $org.walletAddress; Mnemonic = ""; Created = $false }
        }
    } catch {
        # Fall through and attempt creation; the link step reports anything genuinely wrong.
    }

    Write-WtStep "Org admin creates the organisation's wallet"

    $created = Invoke-SorchaApi -Method POST `
        -Uri "$WalletUrl/v1/wallets" -Headers $Headers `
        -Body @{
            name           = $Name
            algorithm      = $Algorithm
            organizationId = $OrganizationId
        }

    $address  = $created.wallet.address
    $mnemonic = ($created.mnemonicWords -join " ")

    if (-not $address) { throw "Wallet creation returned no address for organisation $OrganizationId." }

    # Shown once, never stored. Walkthroughs do not need to keep it, but printing it is what a real
    # org admin sees and is the point of the step existing at all.
    Write-WtWarn "  ORG RECOVERY MNEMONIC (shown once, never stored): $mnemonic"

    $linked = Invoke-SorchaApi -Method POST `
        -Uri "$TenantUrl/organizations/$OrganizationId/wallet" -Headers $Headers `
        -Body @{ walletAddress = $address }

    Write-WtSuccess "  Org wallet created and linked: $address"

    return @{ WalletAddress = $address; Mnemonic = $mnemonic; Created = $true }
}

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
        [string]$Description = "",
        # When set (with a NEW AdminEmail), the admin is provisioned directly as an org-scoped
        # password user (single-org — no public account, no invitation). Spec 136 follow-up.
        [string]$AdminPassword,
        [string]$AdminDisplayName,
        # Mark the provisioned admin verified (bypasses the email loop). Requires the installation
        # to enable Platform:AllowAdminVerifiedUserCreation (dev/n1 do; production does not).
        [switch]$AdminEmailVerified,

        # Supply this and the organisation's OWN wallet is created as part of provisioning, by
        # signing in as its admin (#1525). Required for any org that issues credentials, owns a
        # register or takes part in governance: the org wallet is what its issuer DID anchors on
        # and what its governance roster identity is matched against.
        #
        # It is done here rather than left to the caller because it must happen, and because the
        # platform will not do it for them — the recovery phrase is shown once and belongs to the
        # org admin, so a human (or a script acting as that admin) has to be the one to take it.
        [string]$WalletUrl
    )

    $body = @{
        name        = $Name
        subdomain   = $Subdomain
        adminEmail  = $AdminEmail
        description = $Description
    }
    if ($AdminPassword) {
        $body.adminPassword = $AdminPassword
        $body.adminEmailVerified = [bool]$AdminEmailVerified
        if ($AdminDisplayName) { $body.adminDisplayName = $AdminDisplayName }
    }

    try {
        $response = Invoke-SorchaApi -Method POST `
            -Uri "$TenantUrl/platform/organizations" `
            -Body $body `
            -Headers $Headers

        if ($response.success) {
            Write-WtSuccess "Organization '$Name' created: $($response.organizationId)"

            $orgWalletAddress = Invoke-SorchaOrgWalletStep `
                -TenantUrl $TenantUrl -WalletUrl $WalletUrl `
                -OrganizationId "$($response.organizationId)" -Subdomain $Subdomain `
                -AdminEmail $AdminEmail -AdminPassword $AdminPassword `
                -AdminDirectlyAdded ([bool]$response.adminDirectlyAdded)

            return @{
                OrganizationId     = $response.organizationId
                AdminDirectlyAdded = $response.adminDirectlyAdded
                WalletAddress      = $orgWalletAddress
            }
        } else {
            throw "Organization creation failed: $($response.error)"
        }
    } catch {
        $statusCode = $null
        $responseBody = ""
        try { $statusCode = $_.Exception.Response.StatusCode.value__ } catch {}
        try { $responseBody = $_.ErrorDetails.Message } catch {}

        # The tenant service rejects duplicate-creation attempts two different
        # ways depending on which validator trips first: 409 Conflict if the
        # org name/id collides, or 400 with InvalidSubdomain if the subdomain
        # is already taken. Both mean "the org we wanted already exists" — in
        # either case fall through to the list-and-match recovery path so
        # setup.ps1 stays idempotent.
        $looksLikeDuplicate = $statusCode -eq 409 `
            -or ($statusCode -eq 400 -and $responseBody -match "InvalidSubdomain|already taken|already exists")

        if ($looksLikeDuplicate) {
            Write-WtWarn "Organization '$Name' already exists — fetching"
            # List orgs to find existing. The platform endpoint requires explicit
            # pagination params (`page` and `pageSize` are mandatory), so pass a
            # wide window rather than relying on server-side defaults.
            $orgs = Invoke-SorchaApi -Method GET `
                -Uri "$TenantUrl/platform/organizations?page=1&pageSize=100" `
                -Headers $Headers
            $existing = $orgs.items | Where-Object { $_.subdomain -eq $Subdomain } | Select-Object -First 1
            if ($existing) {
                Write-WtSuccess "Found existing org: $($existing.id)"
                # AdminDirectlyAdded = $false, and that is the whole point of reporting it: this path
                # recovers an org that someone else created, so the -AdminEmail passed to THIS call
                # was never provisioned into it. Saying $true here (as it used to) told callers the
                # admin was ready when they had no membership at all, and the first org-scoped call
                # they made 403'd with nothing to connect it to (#1427). Callers must ensure
                # membership themselves — see New-SorchaOrgUser.
                # The org already existed, so THIS call never provisioned its admin — we cannot
                # sign in as them to create the wallet. If it already has one, report it; otherwise
                # say plainly that the step is outstanding rather than leaving it to be discovered
                # four steps later as an unresolvable issuer DID.
                $existingWallet = $existing.walletAddress
                if ($WalletUrl -and -not $existingWallet) {
                    Write-WtWarn "Organization '$Name' has no wallet and this call did not provision its admin,"
                    Write-WtWarn "so it cannot be created here. Sign in as an admin of that org and call"
                    Write-WtWarn "New-SorchaOrgWallet, or the org cannot issue credentials (#1525)."
                }

                return @{
                    OrganizationId     = "$($existing.id)"
                    AdminDirectlyAdded = $false
                    WalletAddress      = $existingWallet
                }
            }
            throw "Organization '$Name' looked like a duplicate but could not be found by subdomain"
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

# ============================================================================
# Set-SorchaCitizenPendingApplication / Clear-SorchaCitizenPendingApplication
# Feature 124 — drives the wallet PWA's waiting-state UX from the walkthrough.
# ============================================================================

function Set-SorchaCitizenPendingApplication {
    <#
    .SYNOPSIS
        Set the citizen's pending-application notice on the Wallet Service.
    .DESCRIPTION
        Calls PUT /api/v1/wallet/pending-applications with the supplied label.
        Idempotent — calling with a new label replaces the prior one and
        resets the TTL. The PWA reads this notice on every Home render to
        decide whether to show the waiting-card.
    .PARAMETER WalletUrl
        The wallet service base URL (e.g. http://localhost/api).
    .PARAMETER Label
        Human-readable application label (1..80 chars). Plain text — no
        credential content.
    .PARAMETER Headers
        Citizen-scope authorization headers (Bearer JWT carrying
        platform_user_id claim).
    #>
    param(
        [Parameter(Mandatory)][string]$WalletUrl,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][hashtable]$Headers
    )

    $body = @{ label = $Label }
    $response = Invoke-SorchaApi -Method PUT `
        -Uri "$WalletUrl/v1/wallet/pending-applications" `
        -Body $body `
        -Headers $Headers
    Write-WtInfo "Pending-application notice set: $Label"
    return $response.notice
}

function Clear-SorchaCitizenPendingApplication {
    <#
    .SYNOPSIS
        Clear the citizen's pending-application notice.
    .DESCRIPTION
        Calls DELETE /api/v1/wallet/pending-applications. Idempotent —
        returns 204 whether or not a notice was present. The PWA drops
        the waiting-card within one second of next Home render.
    #>
    param(
        [Parameter(Mandatory)][string]$WalletUrl,
        [Parameter(Mandatory)][hashtable]$Headers
    )

    # Use RawResponse so we don't try to parse a 204 No Content body.
    $null = Invoke-SorchaApi -Method DELETE `
        -Uri "$WalletUrl/v1/wallet/pending-applications" `
        -Headers $Headers `
        -RawResponse
    Write-WtInfo "Pending-application notice cleared"
}

# ============================================================================
# Wait-SorchaActorReady — Bridge script cadence and docket-sealing cadence
# ============================================================================

function Wait-SorchaActorReady {
    <#
    .SYNOPSIS
        Pauses a walkthrough script until the platform is in a state where the
        next step can proceed safely.

    .DESCRIPTION
        Walkthroughs that submit actions back-to-back can race ahead of the
        validator's docket-build cycle. In real-world actor flows each
        participant waits for a SignalR notification that an inbound
        transaction has sealed before proceeding, adding several seconds of
        natural latency between consecutive actions. Scripts move in
        milliseconds and exercise concurrency bugs (notably: stuck
        transactions when a credential-issuance tx arrives during the
        validator's post-seal cleanup window).

        Four modes covering the cases where script cadence needs to gate
        on platform state:

          AfterSubmit         my outgoing tx is sealed (status = Active /
                              Revoked / Superseded)
          AwaitingInbox       the named action is ready for the named actor
                              (the script equivalent of an inbox
                              notification — polls instance.currentActionIds)
          ParticipantSealed   a participant publish tx is sealed
          BlueprintSealed     a blueprint publish tx is sealed

        AfterSubmit / ParticipantSealed / BlueprintSealed share the polling
        shape against /api/registers/{registerId}/transactions/{txId}/status
        (Feature 079 lifecycle endpoint, anonymous). AwaitingInbox polls
        /api/instances/{instanceId} for currentActionIds containment
        (authenticated).

        For agents (Sorcha.Agent), the same conceptual surface is provided
        by their SignalR inbox listeners. Agents do not need this helper.

    .PARAMETER Mode
        Which condition to wait for.

    .PARAMETER TxId
        Transaction ID. Required for AfterSubmit / ParticipantSealed /
        BlueprintSealed.

    .PARAMETER RegisterId
        Register where the transaction seals. Required for all sealed-* modes
        and for AwaitingInbox.

    .PARAMETER InstanceId
        Workflow instance ID. Required for AwaitingInbox.

    .PARAMETER ActionId
        Action ID to wait for. Required for AwaitingInbox.

    .PARAMETER Headers
        Auth headers for API calls. Required for all four modes — the F079
        lifecycle status endpoint is gated by the CanReadTransactions policy,
        and the instance GET is also authenticated.

    .PARAMETER GatewayUrl
        Base URL of the API gateway (e.g., http://localhost or
        https://n1.sorcha.dev).

    .PARAMETER TimeoutSeconds
        Maximum seconds to wait. Default 90. Throws on timeout.

    .PARAMETER IntervalSeconds
        Poll interval. Default 1s.

    .EXAMPLE
        $resp = Invoke-SorchaAction ...
        Wait-SorchaActorReady -Mode AfterSubmit -TxId $resp.transactionId `
            -RegisterId $registerId -GatewayUrl $env.GatewayUrl

    .EXAMPLE
        # Between actor switches in a script:
        Wait-SorchaActorReady -Mode AwaitingInbox -InstanceId $inst `
            -ActionId 6 -RegisterId $registerId -Headers $planningHeaders `
            -GatewayUrl $env.GatewayUrl
    #>
    param(
        [Parameter(Mandatory)]
        [ValidateSet('AfterSubmit', 'AwaitingInbox', 'ParticipantSealed', 'BlueprintSealed')]
        [string]$Mode,

        [string]$TxId,
        [string]$RegisterId,
        [string]$InstanceId,
        [int]$ActionId,
        [hashtable]$Headers,

        [Parameter(Mandatory)]
        [string]$GatewayUrl,

        [int]$TimeoutSeconds = 90,
        [int]$IntervalSeconds = 1
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $start = Get-Date
    $attempt = 0

    $sealedModes = @('AfterSubmit', 'ParticipantSealed', 'BlueprintSealed')
    if ($sealedModes -contains $Mode) {
        if (-not $TxId)       { throw "Wait-SorchaActorReady[$Mode]: -TxId is required" }
        if (-not $RegisterId) { throw "Wait-SorchaActorReady[$Mode]: -RegisterId is required" }
        if (-not $Headers)    { throw "Wait-SorchaActorReady[$Mode]: -Headers is required (lifecycle endpoint is auth-gated by CanReadTransactions policy)" }

        $url = "$GatewayUrl/api/registers/$RegisterId/transactions/$TxId/status"
        $shortTx = if ($TxId.Length -ge 10) { $TxId.Substring(0, 10) } else { $TxId }

        while ((Get-Date) -lt $deadline) {
            $attempt++
            try {
                $r = Invoke-WebRequest -Uri $url -Method GET -Headers $Headers -SkipCertificateCheck -ErrorAction Stop
                if ($r.StatusCode -eq 200) {
                    $body = $r.Content | ConvertFrom-Json
                    # TransactionLifecycleStatus serializes as int (Active=0,
                    # Revoked=1, Superseded=2) on n1; accept both numeric and
                    # string forms for resilience.
                    $statusLabel = switch ($body.status) {
                        0 { 'Active' }
                        1 { 'Revoked' }
                        2 { 'Superseded' }
                        default { "$($body.status)" }
                    }
                    if ($statusLabel -in @('Active', 'Revoked', 'Superseded')) {
                        $elapsed = ((Get-Date) - $start).TotalSeconds
                        Write-WtInfo ("  Wait[$Mode] tx=$shortTx... -> $statusLabel in {0:N1}s ($attempt polls)" -f $elapsed)
                        return
                    }
                }
            }
            catch {
                # 404 expected during pre-seal window — keep polling silently.
                $status = $null
                if ($_.Exception.PSObject.Properties.Name -contains 'Response' -and $_.Exception.Response) {
                    $status = $_.Exception.Response.StatusCode.value__
                }
                if ($status -and $status -ne 404) {
                    Write-WtWarn "  Wait[$Mode] poll error ($status): $($_.Exception.Message)"
                }
            }
            Start-Sleep -Seconds $IntervalSeconds
        }
        throw "Wait-SorchaActorReady[$Mode] timed out after ${TimeoutSeconds}s — tx $TxId on register $RegisterId never reached a sealed status"
    }

    if ($Mode -eq 'AwaitingInbox') {
        if (-not $InstanceId) { throw "Wait-SorchaActorReady[AwaitingInbox]: -InstanceId is required" }
        if (-not $PSBoundParameters.ContainsKey('ActionId')) {
            throw "Wait-SorchaActorReady[AwaitingInbox]: -ActionId is required"
        }
        if (-not $Headers) { throw "Wait-SorchaActorReady[AwaitingInbox]: -Headers is required (the instance GET is authenticated)" }

        $url = "$GatewayUrl/api/instances/$InstanceId"

        while ((Get-Date) -lt $deadline) {
            $attempt++
            try {
                $r = Invoke-WebRequest -Uri $url -Method GET -Headers $Headers -SkipCertificateCheck -ErrorAction Stop
                if ($r.StatusCode -eq 200) {
                    $inst = $r.Content | ConvertFrom-Json
                    if ($inst.currentActionIds -contains $ActionId) {
                        $elapsed = ((Get-Date) - $start).TotalSeconds
                        Write-WtInfo ("  Wait[Inbox] action $ActionId ready on instance $InstanceId in {0:N1}s ($attempt polls)" -f $elapsed)
                        return
                    }
                }
            }
            catch {
                Write-WtWarn "  Wait[Inbox] poll error: $($_.Exception.Message)"
            }
            Start-Sleep -Seconds $IntervalSeconds
        }
        throw "Wait-SorchaActorReady[AwaitingInbox] timed out after ${TimeoutSeconds}s — action $ActionId never became ready on instance $InstanceId"
    }
}
