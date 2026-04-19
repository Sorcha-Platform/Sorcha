<#
.SYNOPSIS
    Launches autonomous actor agents for the TradeFinance walkthrough.
.DESCRIPTION
    Creates procurement-to-pay and invoice finance instances, then starts 6
    independent sorcha-agent processes. Cross-register credential requirements
    (VerifiedInvoiceCredential) enforce ordering between registers.
    Requires setup.ps1 to have been run first.
.PARAMETER Profile
    Environment profile. Default: gateway
.PARAMETER StatePath
    Path to state.json. Default: auto-detected.
.PARAMETER TimeoutMinutes
    Maximum wait time. Default: 10
.PARAMETER AI
    Use AI-powered actors (Claude) instead of rules-based actors.
    Requires ANTHROPIC_API_KEY environment variable.
#>
param(
    [string]$Profile = "gateway",
    [string]$StatePath,
    [int]$TimeoutMinutes = 10,
    [switch]$AI
)

$ErrorActionPreference = "Stop"
$walkthroughDir = $PSScriptRoot
$actorsDir = Join-Path $walkthroughDir "actors"
$modulePath = Join-Path $walkthroughDir ".." "modules" "SorchaWalkthrough" "SorchaWalkthrough.psm1"

Import-Module $modulePath -Force

if (-not $StatePath) {
    $StatePath = Join-Path $walkthroughDir "state.json"
}
if (-not (Test-Path $StatePath)) {
    Write-Error "state.json not found at $StatePath. Run setup.ps1 first."
    exit 1
}

$state = Get-Content $StatePath -Raw | ConvertFrom-Json

# Resolve URLs
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck
$blueprintUrl = $sorchaEnv.BlueprintUrl

$agentMode = if ($AI) { "AI" } else { "Rules" }

Write-Host "`n=== TradeFinance Agent Launcher ===" -ForegroundColor Cyan
Write-Host "Profile: $Profile"
Write-Host "Mode: $agentMode" -ForegroundColor $(if ($AI) { "Magenta" } else { "White" })
Write-Host "Procurement Register: $($state.registers.'sme-trade-register'.id)"
Write-Host "Finance Register: $($state.registers.'trade-finance-register'.id)"
Write-Host "Timeout: ${TimeoutMinutes}m`n"

if ($AI) {
    if (-not $env:ANTHROPIC_API_KEY) {
        Write-Error "ANTHROPIC_API_KEY environment variable is required for AI mode. Set it with: `$env:ANTHROPIC_API_KEY = 'sk-ant-...'"
        exit 1
    }
    Write-Host "  ANTHROPIC_API_KEY: set" -ForegroundColor Green
}

# --- Create Blueprint Instances ---
Write-Host "--- Creating Blueprint Instances ---" -ForegroundColor Green

# Use first org admin for instance creation
$adminRole = $state.roles.'procurement-mgr'
$loginResult = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl `
    -Email $adminRole.email -Password $adminRole.password `
    -OrganizationId $adminRole.organizationId
$headers = @{ Authorization = "Bearer $($loginResult.Token)" }

# Procurement instance
Write-Host "  Creating procurement-to-pay instance..."
$procInstance = Invoke-SorchaApi -Method POST -Url "$blueprintUrl/instances/" `
    -Headers $headers -Body @{
        blueprintId  = $state.blueprints.'procurement-to-pay'.id
        registerId   = $state.registers.'sme-trade-register'.id
        tenantId     = $adminRole.organizationId
        metadata     = @{ source = "agent-walkthrough"; scenario = "golden-path" }
    }
if (-not $procInstance?.id) {
    Write-Error "Failed to create procurement instance."
    exit 1
}
Write-Host "  Procurement instance: $($procInstance.id)" -ForegroundColor Cyan

# Finance instance (use finance register owner's admin)
$funderRole = $state.roles.'finance-director'
$funderLogin = Connect-SorchaUser -TenantUrl $sorchaEnv.TenantUrl `
    -Email $funderRole.email -Password $funderRole.password `
    -OrganizationId $funderRole.organizationId
$funderHeaders = @{ Authorization = "Bearer $($funderLogin.Token)" }

Write-Host "  Creating invoice finance instance..."
$financeInstance = Invoke-SorchaApi -Method POST -Url "$blueprintUrl/instances/" `
    -Headers $funderHeaders -Body @{
        blueprintId  = $state.blueprints.'invoice-finance'.id
        registerId   = $state.registers.'trade-finance-register'.id
        tenantId     = $funderRole.organizationId
        metadata     = @{ source = "agent-walkthrough"; scenario = "golden-path" }
    }
if (-not $financeInstance?.id) {
    Write-Error "Failed to create finance instance."
    exit 1
}
Write-Host "  Finance instance: $($financeInstance.id)" -ForegroundColor Cyan

# --- Set Password Environment Variables ---
$rolePasswords = @{
    "PROCUREMENT_MGR_PASSWORD"   = "procurement-mgr"
    "SITE_MGR_PASSWORD"          = "site-mgr"
    "SALES_MGR_PASSWORD"         = "sales-mgr"
    "FINANCE_DIRECTOR_PASSWORD"  = "finance-director"
    "CREDIT_ANALYST_PASSWORD"    = "credit-analyst"
    "ASSESSMENT_SVC_PASSWORD"    = "assessment-svc"
}

foreach ($entry in $rolePasswords.GetEnumerator()) {
    $role = $state.roles.($entry.Value)
    if ($role -and $role.password) {
        [Environment]::SetEnvironmentVariable($entry.Key, $role.password)
    } else {
        Write-Warning "No password found for $($entry.Value) in state.json"
    }
}

# --- Launch Agents ---
$agentProject = Join-Path $walkthroughDir ".." ".." "src" "Apps" "Sorcha.Agent" "Sorcha.Agent.csproj"
if (-not (Test-Path $agentProject)) {
    Write-Error "Cannot find Sorcha.Agent project."
    exit 1
}

$suffix = if ($AI) { "-ai" } else { "" }
$actorFiles = @(
    "procurement-mgr$suffix.json",
    "sales-mgr$suffix.json",
    "site-mgr$suffix.json",
    "finance-director$suffix.json",
    "assessment-svc$suffix.json",
    "credit-analyst$suffix.json"
)

$logsDir = Join-Path $walkthroughDir "logs"
if (-not (Test-Path $logsDir)) { New-Item -Path $logsDir -ItemType Directory | Out-Null }

$processes = @()
Write-Host "`n--- Starting Agents ---" -ForegroundColor Green

foreach ($file in $actorFiles) {
    $configPath = Join-Path $actorsDir $file
    if (-not (Test-Path $configPath)) {
        Write-Warning "Actor config not found: $configPath — skipping"
        continue
    }

    $logFile = Join-Path $logsDir "$($file -replace '\.json$', '.log')"
    $agentArgs = @("run", "--project", (Resolve-Path $agentProject).Path, "--", "run", "--config", $configPath, "--state", $StatePath)

    Write-Host "  Starting $file..."
    $proc = Start-Process -FilePath "dotnet" -ArgumentList $agentArgs `
        -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" `
        -PassThru -NoNewWindow

    $processes += @{ Process = $proc; Name = $file; LogFile = $logFile }
}

Write-Host "`n$($processes.Count) agents started. Waiting up to ${TimeoutMinutes}m for 10 actions across 2 registers...`n" -ForegroundColor Cyan

# --- Wait ---
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$allExited = $false

while ((Get-Date) -lt $deadline) {
    $running = $processes | Where-Object { -not $_.Process.HasExited }
    if ($running.Count -eq 0) { $allExited = $true; break }
    Start-Sleep -Seconds 5
}

# --- Shutdown ---
$running = $processes | Where-Object { -not $_.Process.HasExited }
if ($running.Count -gt 0) {
    Write-Host "`nTimeout. Stopping $($running.Count) remaining agents..." -ForegroundColor Yellow
    foreach ($p in $running) { try { $p.Process.Kill() } catch { } }
}

# --- Summary ---
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "  Procurement instance: $($procInstance.id)"
Write-Host "  Finance instance:     $($financeInstance.id)"
Write-Host ""
foreach ($p in $processes) {
    $status = if ($p.Process.HasExited) {
        if ($p.Process.ExitCode -eq 0) { "OK" } else { "ERROR (exit $($p.Process.ExitCode))" }
    } else { "KILLED" }
    Write-Host "  $($p.Name): $status"
}

# Clean up env vars
foreach ($entry in $rolePasswords.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($entry.Key, $null)
}

if ($allExited) {
    Write-Host "`nAll agents completed." -ForegroundColor Green
} else {
    Write-Host "`nTimeout — some agents did not complete within ${TimeoutMinutes}m." -ForegroundColor Yellow
}
