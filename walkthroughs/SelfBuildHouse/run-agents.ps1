<#
.SYNOPSIS
    Launches autonomous actor agents for the SelfBuildHouse walkthrough.
.DESCRIPTION
    Creates planning and building warrant instances, then starts 7 independent
    sorcha-agent processes. Actors handle actions across both registers — the
    platform's credential validation enforces cross-register ordering.
    Requires setup.ps1 to have been run first.
.PARAMETER Profile
    Environment profile. Default: gateway
.PARAMETER StatePath
    Path to state.json. Default: auto-detected.
.PARAMETER TimeoutMinutes
    Maximum wait time. Default: 10 (14 actions across 2 registers)
#>
param(
    [string]$Profile = "gateway",
    [string]$StatePath,
    [int]$TimeoutMinutes = 10
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
$secrets = Get-SorchaSecrets -WalkthroughName "self-build-house"

# Resolve URLs
$env = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck
$blueprintUrl = $env.BlueprintUrl

Write-Host "`n=== SelfBuildHouse Agent Launcher ===" -ForegroundColor Cyan
Write-Host "Profile: $Profile"
Write-Host "Planning Register: $($state.planningRegisterId)"
Write-Host "Building Register: $($state.buildingRegisterId)"
Write-Host "Timeout: ${TimeoutMinutes}m`n"

# --- Create Blueprint Instances ---
Write-Host "--- Creating Blueprint Instances ---" -ForegroundColor Green

$headers = @{ Authorization = "Bearer $($state.adminToken)" }

# Planning instance
Write-Host "  Creating planning permission instance..."
$planningInstance = Invoke-SorchaApi -Method POST -Url "$blueprintUrl/instances/" `
    -Headers $headers -Body @{
        blueprintId  = $state.planningBlueprintId
        registerId   = $state.planningRegisterId
        tenantId     = $state.organizationId
        metadata     = @{ source = "agent-walkthrough"; scenario = "happy-path" }
    }
Write-Host "  Planning instance: $($planningInstance.id)" -ForegroundColor Cyan

# Building warrant instance
Write-Host "  Creating building warrant instance..."
$warrantInstance = Invoke-SorchaApi -Method POST -Url "$blueprintUrl/instances/" `
    -Headers $headers -Body @{
        blueprintId  = $state.warrantBlueprintId
        registerId   = $state.buildingRegisterId
        tenantId     = $state.organizationId
        metadata     = @{ source = "agent-walkthrough"; scenario = "happy-path" }
    }
Write-Host "  Warrant instance: $($warrantInstance.id)" -ForegroundColor Cyan

# --- Set Environment Variables ---
[Environment]::SetEnvironmentVariable("ADMIN_EMAIL", $secrets.adminEmail)
[Environment]::SetEnvironmentVariable("ADMIN_PASSWORD", $secrets.adminPassword)

# --- Launch Agents ---
$agentProject = Join-Path $walkthroughDir ".." ".." "src" "Apps" "Sorcha.Agent" "Sorcha.Agent.csproj"
if (-not (Test-Path $agentProject)) {
    Write-Error "Cannot find Sorcha.Agent project."
    exit 1
}

$actorFiles = @(
    "self-builder.json",
    "structural-engineer.json",
    "ecologist.json",
    "utilities-officer.json",
    "planning-officer.json",
    "building-standards-officer.json",
    "building-inspector.json"
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
    $args = @("run", "--project", (Resolve-Path $agentProject).Path, "--", "run", "--config", $configPath, "--state", $StatePath)

    Write-Host "  Starting $file..."
    $proc = Start-Process -FilePath "dotnet" -ArgumentList $args `
        -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" `
        -PassThru -NoNewWindow

    $processes += @{ Process = $proc; Name = $file; LogFile = $logFile }
}

Write-Host "`n$($processes.Count) agents started. Waiting up to ${TimeoutMinutes}m for 14 actions across 2 registers...`n" -ForegroundColor Cyan

# --- Wait for Completion ---
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
Write-Host "  Planning instance:  $($planningInstance.id)"
Write-Host "  Warrant instance:   $($warrantInstance.id)"
Write-Host ""
foreach ($p in $processes) {
    $status = if ($p.Process.HasExited) {
        if ($p.Process.ExitCode -eq 0) { "OK" } else { "ERROR (exit $($p.Process.ExitCode))" }
    } else { "KILLED" }
    Write-Host "  $($p.Name): $status"
}

# Clean up
[Environment]::SetEnvironmentVariable("ADMIN_EMAIL", $null)
[Environment]::SetEnvironmentVariable("ADMIN_PASSWORD", $null)

if ($allExited) {
    Write-Host "`nAll agents completed." -ForegroundColor Green
} else {
    Write-Host "`nTimeout — some agents did not complete within ${TimeoutMinutes}m." -ForegroundColor Yellow
}
