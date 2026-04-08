<#
.SYNOPSIS
    Launches a distributed ConstructionPermit walkthrough with actors split across machines.
.DESCRIPTION
    Runs contractor and structural-engineer locally, while planning-officer,
    environmental-assessor, and building-control run on a remote machine (n1.sorcha.dev).
    Requires setup.ps1 to have been run first.
.PARAMETER StatePath
    Path to state.json. Default: auto-detected.
.PARAMETER TimeoutMinutes
    Maximum wait time. Default: 5
#>
param(
    [string]$StatePath,
    [int]$TimeoutMinutes = 5
)

$ErrorActionPreference = "Stop"
$walkthroughDir = $PSScriptRoot
$actorsDir = Join-Path $walkthroughDir "actors"

if (-not $StatePath) {
    $StatePath = Join-Path $walkthroughDir "state.json"
}
if (-not (Test-Path $StatePath)) {
    Write-Error "state.json not found. Run setup.ps1 first."
    exit 1
}

$state = Get-Content $StatePath -Raw | ConvertFrom-Json
$agentProject = Join-Path $walkthroughDir ".." ".." "src" "Apps" "Sorcha.Agent" "Sorcha.Agent.csproj"

Write-Host "`n=== Distributed ConstructionPermit ===" -ForegroundColor Cyan
Write-Host "LOCAL:  contractor, structural-engineer"
Write-Host "REMOTE: planning-officer, environmental-assessor, building-control"
Write-Host ""

# --- LOCAL ACTORS ---
Write-Host "--- Starting LOCAL actors ---" -ForegroundColor Green

# Set local env vars
[Environment]::SetEnvironmentVariable("CONTRACTOR_PASSWORD", $state.roles.contractor.password)
[Environment]::SetEnvironmentVariable("ENGINEER_PASSWORD", $state.roles.'structural-engineer'.password)

$logsDir = Join-Path $walkthroughDir "logs"
if (-not (Test-Path $logsDir)) { New-Item -Path $logsDir -ItemType Directory | Out-Null }

$localActors = @("contractor.json", "structural-engineer.json")
$processes = @()

foreach ($file in $localActors) {
    $configPath = Join-Path $actorsDir $file
    $logFile = Join-Path $logsDir "$($file -replace '\.json$', '.log')"
    $args = @("run", "--project", (Resolve-Path $agentProject).Path, "--", "run", "--config", $configPath, "--state", $StatePath)

    Write-Host "  Starting $file..."
    $proc = Start-Process -FilePath "dotnet" -ArgumentList $args `
        -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" `
        -PassThru -NoNewWindow
    $processes += @{ Process = $proc; Name = $file }
}

# --- REMOTE ACTORS ---
Write-Host "`n--- Remote actors (manual setup) ---" -ForegroundColor Yellow
Write-Host ""
Write-Host "Copy these files to the remote machine:"
Write-Host "  - walkthroughs/ConstructionPermit/actors/planning-officer-remote.json"
Write-Host "  - walkthroughs/ConstructionPermit/actors/building-control-remote.json"
Write-Host "  - walkthroughs/ConstructionPermit/actors/environmental-assessor-remote.json"
Write-Host "  - walkthroughs/ConstructionPermit/state.json"
Write-Host ""
Write-Host "Then run on the remote machine:"
Write-Host "  export PLANNING_PASSWORD='$($state.roles.'planning-officer'.password)'"
Write-Host "  export INSPECTOR_PASSWORD='$($state.roles.'building-control'.password)'"
Write-Host "  export ENVIRONMENTAL_PASSWORD='$($state.roles.'environmental-assessor'.password)'"
Write-Host ""
Write-Host "  sorcha-agent run --config planning-officer-remote.json --state state.json &"
Write-Host "  sorcha-agent run --config building-control-remote.json --state state.json &"
Write-Host "  sorcha-agent run --config environmental-assessor-remote.json --state state.json &"
Write-Host ""

# Wait
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
Write-Host "Waiting up to ${TimeoutMinutes}m for local agents..." -ForegroundColor Cyan

while ((Get-Date) -lt $deadline) {
    $running = $processes | Where-Object { -not $_.Process.HasExited }
    if ($running.Count -eq 0) { break }
    Start-Sleep -Seconds 5
}

# Cleanup
$running = $processes | Where-Object { -not $_.Process.HasExited }
foreach ($p in $running) { try { $p.Process.Kill() } catch { } }

Write-Host "`n=== Local Summary ===" -ForegroundColor Cyan
foreach ($p in $processes) {
    $status = if ($p.Process.HasExited -and $p.Process.ExitCode -eq 0) { "OK" }
              elseif ($p.Process.HasExited) { "ERROR (exit $($p.Process.ExitCode))" }
              else { "KILLED" }
    Write-Host "  $($p.Name): $status"
}

# Clean env vars
[Environment]::SetEnvironmentVariable("CONTRACTOR_PASSWORD", $null)
[Environment]::SetEnvironmentVariable("ENGINEER_PASSWORD", $null)
