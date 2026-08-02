<#
.SYNOPSIS
    Sorcha CLI - auth profile bootstrap (NOT platform install)

    Issue #1234 - configures a Sorcha CLI auth profile against an ALREADY-RUNNING instance.
    It does NOT install, start or configure the platform. To do that use
    scripts/sorcha-setup.sh. The "bootstrap-" name is the main trap in this directory;
    see scripts/README.md for a map of which script does what.
.DESCRIPTION
    Interactive PowerShell script to configure a fresh Sorcha installation using
    the `sorcha bootstrap` command. This script:
    - Reads service URLs from CLI config (~/.sorcha/config.json) for the selected profile
    - Checks if Sorcha Tenant Service is running before proceeding
    - Creates the initial organization
    - Sets up the administrative user
    - Optionally creates a service principal for automation
.PARAMETER Profile
    Configuration profile name. If not specified, uses activeProfile from ~/.sorcha/config.json,
    or defaults to 'local' if config doesn't exist
.PARAMETER NonInteractive
    Run in non-interactive mode using defaults
.PARAMETER OrgName
    Organization name (non-interactive mode)
.PARAMETER Subdomain
    Organization subdomain (non-interactive mode)
.PARAMETER AdminEmail
    Administrator email (non-interactive mode)
.PARAMETER AdminName
    Administrator display name (non-interactive mode)
.PARAMETER AdminPassword
    Administrator password (non-interactive mode)
.PARAMETER CreateServicePrincipal
    Create service principal for automation
.PARAMETER ServicePrincipalName
    Service principal name (if CreateServicePrincipal is true)
.EXAMPLE
    .\bootstrap-sorcha.ps1
    Interactive setup with prompts
.EXAMPLE
    .\bootstrap-sorcha.ps1 -Profile local -NonInteractive `
        -OrgName "Stark Industries" `
        -Subdomain "stark" `
        -AdminEmail "tony@stark.com" `
        -AdminName "Tony Stark" `
        -AdminPassword "SecureP@ss123!" `
        -CreateServicePrincipal `
        -ServicePrincipalName "jarvis"
    Non-interactive setup with all parameters
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$Profile,

    [Parameter(Mandatory = $false)]
    [switch]$NonInteractive,

    [Parameter(Mandatory = $false)]
    [string]$OrgName,

    [Parameter(Mandatory = $false)]
    [string]$Subdomain,

    [Parameter(Mandatory = $false)]
    [string]$AdminEmail,

    [Parameter(Mandatory = $false)]
    [string]$AdminName,

    [Parameter(Mandatory = $false)]
    [string]$AdminPassword,

    [Parameter(Mandatory = $false)]
    [switch]$CreateServicePrincipal,

    [Parameter(Mandatory = $false)]
    [string]$ServicePrincipalName = "bootstrap-principal"
)

# Script configuration
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Configuration paths
$configDir = Join-Path $env:USERPROFILE ".sorcha"
$configFile = Join-Path $configDir "config.json"
if (-not (Test-Path $configDir)) {
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
}

# Function to read CLI configuration
function Get-SorchaConfig {
    if (Test-Path $configFile) {
        try {
            $configJson = Get-Content $configFile -Raw | ConvertFrom-Json
            return $configJson
        } catch {
            Write-Host "Warning: Could not read config file: $_" -ForegroundColor Yellow
            return $null
        }
    }
    return $null
}

# Function to get profile from config
function Get-ProfileConfig {
    param([string]$ProfileName)

    $config = Get-SorchaConfig
    if ($null -eq $config -or $null -eq $config.Profiles) {
        return $null
    }

    if ($config.Profiles.PSObject.Properties.Name -contains $ProfileName) {
        return $config.Profiles.$ProfileName
    }

    return $null
}

# Color output helpers
function Write-Step {
    param([string]$Message)
    Write-Host "==> " -NoNewline -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor White
}

function Write-Success {
    param([string]$Message)
    Write-Host "✓ " -NoNewline -ForegroundColor Green
    Write-Host $Message -ForegroundColor White
}

function Write-ErrorMsg {
    param([string]$Message)
    Write-Host "✗ " -NoNewline -ForegroundColor Red
    Write-Host $Message -ForegroundColor White
}

function Write-Info {
    param([string]$Message)
    Write-Host "ℹ " -NoNewline -ForegroundColor Blue
    Write-Host $Message -ForegroundColor White
}

function Test-CommandExists {
    param([string]$Command)
    return $null -ne (Get-Command $Command -ErrorAction SilentlyContinue)
}

# Banner
Write-Host ""
Write-Host "╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                                                                ║" -ForegroundColor Cyan
Write-Host "║              Sorcha Platform Bootstrap Script                 ║" -ForegroundColor Cyan
Write-Host "║                                                                ║" -ForegroundColor Cyan
Write-Host "║         Initial configuration for fresh installation          ║" -ForegroundColor Cyan
Write-Host "║                                                                ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Determine which profile to use
if ([string]::IsNullOrEmpty($Profile)) {
    # No profile specified, try to read from config
    $config = Get-SorchaConfig
    if ($null -ne $config -and -not [string]::IsNullOrEmpty($config.ActiveProfile)) {
        $Profile = $config.ActiveProfile
        Write-Info "Using active profile from config: $Profile"
    } else {
        # Fallback to default
        $Profile = "local"
        Write-Info "No active profile in config, using default: $Profile"
    }
} else {
    Write-Info "Using profile from parameter: $Profile"
}
Write-Host ""

# Check prerequisites
Write-Step "Checking prerequisites..."

if (-not (Test-CommandExists "sorcha")) {
    Write-ErrorMsg "Sorcha CLI not found. Please install it first:"
    Write-Host "  dotnet tool install -g Sorcha.Cli" -ForegroundColor Yellow
    Write-Host "  OR run from source:" -ForegroundColor Yellow
    Write-Host "  dotnet run --project src/Apps/Sorcha.Cli -- [command]" -ForegroundColor Yellow
    exit 1
}

$sorchaVersion = & sorcha --version
Write-Success "Sorcha CLI found: $sorchaVersion"
Write-Host ""

# Check if service is running
Write-Step "Checking Sorcha Tenant Service..."
$maxAttempts = 5
$attempt = 0
$serviceReady = $false

# Get health URL from profile config, with fallback to defaults
$profileConfig = Get-ProfileConfig -ProfileName $Profile
if ($null -ne $profileConfig) {
    # Check for tenantServiceUrl first (older style with separate service URLs)
    if ($null -ne $profileConfig.TenantServiceUrl) {
        $healthUrl = "$($profileConfig.TenantServiceUrl)/health"
        Write-Info "Using Tenant Service URL from config: $($profileConfig.TenantServiceUrl)"
    }
    # Otherwise check for serviceUrl (newer style with single base URL)
    elseif ($null -ne $profileConfig.ServiceUrl) {
        $healthUrl = "$($profileConfig.ServiceUrl)/api/tenant/health"
        Write-Info "Using Service URL from config: $($profileConfig.ServiceUrl)/api/tenant"
    }
    else {
        # Profile exists but no URL configured
        Write-Host "  Profile '$Profile' found but no service URL configured, using defaults" -ForegroundColor Yellow
        $healthUrl = switch ($Profile) {
            "docker" { "http://localhost/api/tenant/health" }
            "local" { "http://localhost:5110/health" }
            "dev" { "https://localhost:7080/health" }
            default { "http://localhost:5110/health" }
        }
    }
} else {
    # Fallback to defaults if config not found
    Write-Host "  Profile '$Profile' not found in config, using default URLs" -ForegroundColor Yellow
    $healthUrl = switch ($Profile) {
        "docker" { "http://localhost/api/tenant/health" }
        "local" { "http://localhost:5110/health" }
        "dev" { "https://localhost:7080/health" }
        default { "http://localhost:5110/health" }
    }
}

while ($attempt -lt $maxAttempts -and -not $serviceReady) {
    try {
        $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2 -ErrorAction SilentlyContinue
        if ($response.StatusCode -eq 200) {
            $serviceReady = $true
            Write-Success "Tenant Service is running at $healthUrl"
            break
        }
    } catch {
        # Service not ready yet
    }

    $attempt++
    if ($attempt -lt $maxAttempts) {
        Write-Host "  Waiting for service... ($attempt/$maxAttempts)" -ForegroundColor Gray
        Start-Sleep -Seconds 2
    }
}

if (-not $serviceReady) {
    Write-ErrorMsg "Tenant Service is not running. Please start it first:"
    Write-Host ""
    Write-Host "For Docker:" -ForegroundColor Yellow
    Write-Host "  docker-compose up -d" -ForegroundColor Gray
    Write-Host ""
    Write-Host "For local development:" -ForegroundColor Yellow
    Write-Host "  dotnet run --project src/Services/Sorcha.Tenant.Service" -ForegroundColor Gray
    Write-Host ""
    Write-Host "For Aspire:" -ForegroundColor Yellow
    Write-Host "  dotnet run --project src/Apps/Sorcha.AppHost" -ForegroundColor Gray
    Write-Host ""
    exit 1
}

Write-Host ""

# Build sorcha bootstrap command arguments
$bootstrapArgs = @(
    "bootstrap",
    "--profile", $Profile
)

if ($NonInteractive) {
    $bootstrapArgs += "--non-interactive"

    # Add required parameters for non-interactive mode
    if ($OrgName) { $bootstrapArgs += "--org-name"; $bootstrapArgs += $OrgName }
    if ($Subdomain) { $bootstrapArgs += "--subdomain"; $bootstrapArgs += $Subdomain }
    if ($AdminEmail) { $bootstrapArgs += "--admin-email"; $bootstrapArgs += $AdminEmail }
    if ($AdminName) { $bootstrapArgs += "--admin-name"; $bootstrapArgs += $AdminName }
    if ($AdminPassword) { $bootstrapArgs += "--admin-password"; $bootstrapArgs += $AdminPassword }

    if ($CreateServicePrincipal) {
        $bootstrapArgs += "--create-sp"
        if ($ServicePrincipalName) {
            $bootstrapArgs += "--sp-name"
            $bootstrapArgs += $ServicePrincipalName
        }
    }

    Write-Info "Running in non-interactive mode with profile: $Profile"
} else {
    Write-Info "Running in interactive mode - you will be prompted for inputs"
    Write-Info "Using profile: $Profile"
}

Write-Host ""

# Run sorcha bootstrap command
Write-Step "Running sorcha bootstrap..."
Write-Host ""

try {
    & sorcha @bootstrapArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-ErrorMsg "Bootstrap command failed with exit code: $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    Write-Host ""
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host "Bootstrap Complete!" -ForegroundColor Green
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor Green
    Write-Host ""

    Write-Success "Sorcha platform has been initialized successfully"
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Cyan
    Write-Host "  1. Login as admin:" -ForegroundColor White
    Write-Host "     sorcha auth login --profile $Profile" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  2. View configuration:" -ForegroundColor White
    Write-Host "     sorcha config list --profiles" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  3. Create additional users:" -ForegroundColor White
    Write-Host "     sorcha user create --profile $Profile" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  4. View API documentation:" -ForegroundColor White

    # Use profile config for API docs URL if available
    if ($null -ne $profileConfig) {
        if ($null -ne $profileConfig.TenantServiceUrl) {
            $apiDocsUrl = "$($profileConfig.TenantServiceUrl)/scalar/"
        } elseif ($null -ne $profileConfig.ServiceUrl) {
            $apiDocsUrl = "$($profileConfig.ServiceUrl)/scalar/"
        } else {
            $apiDocsUrl = switch ($Profile) {
                "docker" { "http://localhost/scalar/" }
                "local" { "http://localhost:5110/scalar/" }
                "dev" { "https://localhost:7080/scalar/" }
                default { "http://localhost:5110/scalar/" }
            }
        }
    } else {
        $apiDocsUrl = switch ($Profile) {
            "docker" { "http://localhost/scalar/" }
            "local" { "http://localhost:5110/scalar/" }
            "dev" { "https://localhost:7080/scalar/" }
            default { "http://localhost:5110/scalar/" }
        }
    }
    Write-Host "     $apiDocsUrl" -ForegroundColor Gray
    Write-Host ""

    Write-Info "Configuration profile: $Profile"
    Write-Info "Configuration saved to: $configFile"
    Write-Host ""

} catch {
    Write-Host ""
    Write-ErrorMsg "Bootstrap failed: $_"
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "  - Ensure the Tenant Service is running" -ForegroundColor Gray
    Write-Host "  - Check the service logs for errors" -ForegroundColor Gray
    Write-Host "  - Verify your network connectivity" -ForegroundColor Gray
    Write-Host "  - Try running with --verbose flag:" -ForegroundColor Gray
    Write-Host "    sorcha bootstrap --profile $Profile --verbose" -ForegroundColor Gray
    Write-Host ""
    exit 1
}
