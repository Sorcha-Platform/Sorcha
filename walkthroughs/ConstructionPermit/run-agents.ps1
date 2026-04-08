<#
.SYNOPSIS
    Launches autonomous actor agents for the ConstructionPermit walkthrough.
.DESCRIPTION
    Starts 5 independent sorcha-agent processes (one per participant) that
    autonomously execute the ConstructionPermit workflow via rules-based decisions.
    Requires setup.ps1 to have been run first to create state.json.
.PARAMETER Profile
    Environment profile (gateway, n1, local). Default: gateway
.PARAMETER StatePath
    Path to state.json. Default: auto-detected from walkthrough directory.
.PARAMETER TimeoutMinutes
    Maximum time to wait for workflow completion. Default: 5
.PARAMETER AgentBinary
    Path to sorcha-agent binary. Default: uses dotnet run.
#>
param(
    [string]$Profile = "gateway",
    [string]$StatePath,
    [int]$TimeoutMinutes = 5,
    [string]$AgentBinary
)

$ErrorActionPreference = "Stop"
$walkthroughDir = $PSScriptRoot
$actorsDir = Join-Path $walkthroughDir "actors"

# Resolve state.json
if (-not $StatePath) {
    $StatePath = Join-Path $walkthroughDir "state.json"
}
if (-not (Test-Path $StatePath)) {
    Write-Error "state.json not found at $StatePath. Run setup.ps1 first."
    exit 1
}

# Load state to extract passwords for env vars
$state = Get-Content $StatePath -Raw | ConvertFrom-Json

# Resolve agent command
if (-not $AgentBinary) {
    $agentProject = Join-Path $walkthroughDir ".." ".." "src" "Apps" "Sorcha.Agent" "Sorcha.Agent.csproj"
    if (-not (Test-Path $agentProject)) {
        Write-Error "Cannot find Sorcha.Agent project. Specify -AgentBinary."
        exit 1
    }
    $agentCmd = "dotnet"
    $agentBaseArgs = @("run", "--project", (Resolve-Path $agentProject).Path, "--")
} else {
    $agentCmd = $AgentBinary
    $agentBaseArgs = @()
}

# Actor configs and their password env var mappings
$actors = @(
    @{ File = "contractor.json";            EnvVar = "CONTRACTOR_PASSWORD";     PasswordKey = "contractor" }
    @{ File = "structural-engineer.json";   EnvVar = "ENGINEER_PASSWORD";       PasswordKey = "structural-engineer" }
    @{ File = "planning-officer.json";      EnvVar = "PLANNING_PASSWORD";       PasswordKey = "planning-officer" }
    @{ File = "environmental-assessor.json"; EnvVar = "ENVIRONMENTAL_PASSWORD"; PasswordKey = "environmental-assessor" }
    @{ File = "building-control.json";      EnvVar = "INSPECTOR_PASSWORD";      PasswordKey = "building-control" }
)

Write-Host "`n=== ConstructionPermit Agent Launcher ===" -ForegroundColor Cyan
Write-Host "State: $StatePath"
Write-Host "Timeout: ${TimeoutMinutes}m`n"

# Set password environment variables from state
foreach ($actor in $actors) {
    $role = $state.roles.($actor.PasswordKey)
    if ($role -and $role.password) {
        [Environment]::SetEnvironmentVariable($actor.EnvVar, $role.password)
    } else {
        Write-Warning "No password found for $($actor.PasswordKey) in state.json"
    }
}

# Launch agents
$processes = @()
$logsDir = Join-Path $walkthroughDir "logs"
if (-not (Test-Path $logsDir)) { New-Item -Path $logsDir -ItemType Directory | Out-Null }

foreach ($actor in $actors) {
    $configPath = Join-Path $actorsDir $actor.File
    if (-not (Test-Path $configPath)) {
        Write-Warning "Actor config not found: $configPath — skipping"
        continue
    }

    $logFile = Join-Path $logsDir "$($actor.File -replace '\.json$', '.log')"
    $args = $agentBaseArgs + @("run", "--config", $configPath, "--state", $StatePath)

    Write-Host "Starting $($actor.File)..." -ForegroundColor Green
    $proc = Start-Process -FilePath $agentCmd -ArgumentList $args `
        -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" `
        -PassThru -NoNewWindow

    $processes += @{ Process = $proc; Name = $actor.File; LogFile = $logFile }
}

Write-Host "`n$($processes.Count) agents started. Waiting up to ${TimeoutMinutes}m for completion...`n" -ForegroundColor Cyan

# Wait for completion or timeout
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$allExited = $false

while ((Get-Date) -lt $deadline) {
    $running = $processes | Where-Object { -not $_.Process.HasExited }
    if ($running.Count -eq 0) {
        $allExited = $true
        break
    }
    Start-Sleep -Seconds 5
}

# Shutdown remaining processes
$running = $processes | Where-Object { -not $_.Process.HasExited }
if ($running.Count -gt 0) {
    Write-Host "`nTimeout reached. Stopping $($running.Count) remaining agents..." -ForegroundColor Yellow
    foreach ($p in $running) {
        try { $p.Process.Kill() } catch { }
    }
}

# Print summary
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
foreach ($p in $processes) {
    $status = if ($p.Process.HasExited) {
        if ($p.Process.ExitCode -eq 0) { "OK" } else { "ERROR (exit $($p.Process.ExitCode))" }
    } else { "KILLED" }
    Write-Host "  $($p.Name): $status"
}

# Clean up env vars
foreach ($actor in $actors) {
    [Environment]::SetEnvironmentVariable($actor.EnvVar, $null)
}

if ($allExited) {
    Write-Host "`nAll agents completed." -ForegroundColor Green
} else {
    Write-Host "`nTimeout — some agents did not complete within ${TimeoutMinutes}m." -ForegroundColor Yellow
}
