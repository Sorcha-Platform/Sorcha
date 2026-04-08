<#
.SYNOPSIS
    Launches autonomous actor agents for the PayloadTests walkthrough.
.DESCRIPTION
    Starts 2 independent sorcha-agent processes (sender + receiver) that
    autonomously execute the file transfer workflow.
    Requires setup.ps1 to have been run first to create state.json.
.PARAMETER Profile
    Environment profile. Default: gateway
.PARAMETER StatePath
    Path to state.json. Default: auto-detected.
.PARAMETER TimeoutMinutes
    Maximum wait time. Default: 3
#>
param(
    [string]$Profile = "gateway",
    [string]$StatePath,
    [int]$TimeoutMinutes = 3
)

$ErrorActionPreference = "Stop"
$walkthroughDir = $PSScriptRoot
$actorsDir = Join-Path $walkthroughDir "actors"

if (-not $StatePath) {
    $StatePath = Join-Path $walkthroughDir "state.json"
}
if (-not (Test-Path $StatePath)) {
    Write-Error "state.json not found at $StatePath. Run setup.ps1 first."
    exit 1
}

$state = Get-Content $StatePath -Raw | ConvertFrom-Json

$agentProject = Join-Path $walkthroughDir ".." ".." "src" "Apps" "Sorcha.Agent" "Sorcha.Agent.csproj"
if (-not (Test-Path $agentProject)) {
    Write-Error "Cannot find Sorcha.Agent project."
    exit 1
}

$actors = @(
    @{ File = "sender.json";   EnvVar = "SENDER_PASSWORD";   PasswordKey = "sender" }
    @{ File = "receiver.json"; EnvVar = "RECEIVER_PASSWORD";  PasswordKey = "receiver" }
)

Write-Host "`n=== PayloadTests Agent Launcher ===" -ForegroundColor Cyan
Write-Host "State: $StatePath"
Write-Host "Timeout: ${TimeoutMinutes}m`n"

# Set password env vars from state
foreach ($actor in $actors) {
    $role = $state.($actor.PasswordKey)
    if ($role -and $role.password) {
        [Environment]::SetEnvironmentVariable($actor.EnvVar, $role.password)
    } else {
        Write-Warning "No password found for $($actor.PasswordKey) in state.json"
    }
}

# Launch agents
$logsDir = Join-Path $walkthroughDir "logs"
if (-not (Test-Path $logsDir)) { New-Item -Path $logsDir -ItemType Directory | Out-Null }

$processes = @()
foreach ($actor in $actors) {
    $configPath = Join-Path $actorsDir $actor.File
    if (-not (Test-Path $configPath)) {
        Write-Warning "Actor config not found: $configPath — skipping"
        continue
    }

    $logFile = Join-Path $logsDir "$($actor.File -replace '\.json$', '.log')"
    $args = @("run", "--project", (Resolve-Path $agentProject).Path, "--", "run", "--config", $configPath, "--state", $StatePath)

    Write-Host "Starting $($actor.File)..." -ForegroundColor Green
    $proc = Start-Process -FilePath "dotnet" -ArgumentList $args `
        -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" `
        -PassThru -NoNewWindow

    $processes += @{ Process = $proc; Name = $actor.File; LogFile = $logFile }
}

Write-Host "`n$($processes.Count) agents started. Waiting up to ${TimeoutMinutes}m...`n" -ForegroundColor Cyan

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$allExited = $false

while ((Get-Date) -lt $deadline) {
    $running = $processes | Where-Object { -not $_.Process.HasExited }
    if ($running.Count -eq 0) { $allExited = $true; break }
    Start-Sleep -Seconds 5
}

# Shutdown remaining
$running = $processes | Where-Object { -not $_.Process.HasExited }
if ($running.Count -gt 0) {
    Write-Host "`nTimeout. Stopping $($running.Count) remaining agents..." -ForegroundColor Yellow
    foreach ($p in $running) { try { $p.Process.Kill() } catch { } }
}

# Summary
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
    Write-Host "`nTimeout — agents did not complete within ${TimeoutMinutes}m." -ForegroundColor Yellow
}
