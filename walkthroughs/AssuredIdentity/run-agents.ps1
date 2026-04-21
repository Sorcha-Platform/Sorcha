<#
.SYNOPSIS
    Launches autonomous actor agents for the AssuredIdentity walkthrough.
.DESCRIPTION
    Starts verification-analyst and licensing-officer agents in background, each in rules
    mode, so the citizen-side run scripts can drive the workflow end-to-end
    without a human at the analyst UI. Feature 107 PR 3 (US3).

    verification-analyst auto-approves the identity application (Phase 1 Action 2).
    licensing-officer auto-issues the driving licence (Phase 2 Action 3). The
    verification step (Phase 2 Action 2) remains script-driven because it
    creates the HAIP presentation request the citizen must respond to —
    see README for the scope rationale.
.PARAMETER StatePath
    Path to state.json. Default: auto-detected from walkthrough directory.
.PARAMETER TimeoutMinutes
    Maximum time to wait for both agents to fire their rules. Default: 3.
.PARAMETER AgentBinary
    Optional path to a built sorcha-agent binary. Default: uses `dotnet run`.
#>
param(
    [string]$StatePath,
    [int]$TimeoutMinutes = 3,
    [string]$AgentBinary
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

# ---------- Env vars ----------
# Passwords for each agent's JWT-bearer login. Pull from the state file so
# we don't hard-code anything; state.json is gitignored.
[Environment]::SetEnvironmentVariable("VERIFICATION_ANALYST_PASSWORD", $state.roles.verificationAnalyst.password)
[Environment]::SetEnvironmentVariable("LICENSING_OFFICER_PASSWORD",    $state.roles.licensingOfficer.password)
[Environment]::SetEnvironmentVariable("CITIZEN_PASSWORD",              $state.roles.citizen.password)

# Licence details for the licensing-officer rule. Expiry = issue + 10 years per
# the Feature 107 PR 2 blueprint contract.
$issued = (Get-Date).ToString("yyyy-MM-dd")
$expiry = (Get-Date).AddYears(10).ToString("yyyy-MM-dd")
[Environment]::SetEnvironmentVariable("LICENCE_ISSUED_DATE", $issued)
[Environment]::SetEnvironmentVariable("LICENCE_EXPIRY_DATE", $expiry)
# Zero-padded 5-digit serial so every licence has the same format
# regardless of Get-Random's output magnitude.
[Environment]::SetEnvironmentVariable("LICENCE_NUMBER", ("DL-ACME-{0:D5}" -f (Get-Random -Maximum 100000)))

# ---------- Agents ----------
$actors = @(
    @{ File = "verification-analyst.json"; Name = "verification-analyst" }
    @{ File = "licensing-officer.json";    Name = "licensing-officer"    }
)

Write-Host "`n=== AssuredIdentity Agent Launcher ===" -ForegroundColor Cyan
Write-Host "State:   $StatePath"
Write-Host "Timeout: ${TimeoutMinutes}m`n"

$logsDir = Join-Path $walkthroughDir "logs"
if (-not (Test-Path $logsDir)) { New-Item -Path $logsDir -ItemType Directory | Out-Null }

$processes = @()
foreach ($actor in $actors) {
    $configPath = Join-Path $actorsDir $actor.File
    if (-not (Test-Path $configPath)) {
        Write-Warning "Actor config not found: $configPath — skipping"
        continue
    }
    $logFile = Join-Path $logsDir "$($actor.Name).log"
    $runArgs = $agentBaseArgs + @("run", "--config", $configPath, "--state", $StatePath)

    Write-Host "Starting $($actor.Name)..." -ForegroundColor Green
    $proc = Start-Process -FilePath $agentCmd -ArgumentList $runArgs `
        -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" `
        -PassThru -NoNewWindow
    $processes += @{ Process = $proc; Name = $actor.Name; LogFile = $logFile }
}

if ($processes.Count -eq 0) {
    Write-Error "No agents started — nothing to wait on."
    exit 1
}

# ---------- Wait for completion ----------
# Each agent in rules mode exits after firing its rule. Both agents
# exiting cleanly means both Action 2 (identity) and Action 3 (licence)
# have been approved.
Write-Host "`n$($processes.Count) agents started. Waiting up to ${TimeoutMinutes}m for rule fire...`n" -ForegroundColor Cyan

$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
while ((Get-Date) -lt $deadline) {
    $running = $processes | Where-Object { -not $_.Process.HasExited }
    if ($running.Count -eq 0) { break }
    Start-Sleep -Seconds 2
}

$running = $processes | Where-Object { -not $_.Process.HasExited }
if ($running.Count -gt 0) {
    Write-Host "`nTimeout reached. Stopping $($running.Count) remaining agents..." -ForegroundColor Yellow
    foreach ($p in $running) {
        try { $p.Process.Kill() } catch { }
    }
}

Write-Host "`n=== Summary ===" -ForegroundColor Cyan
$allOk = $true
foreach ($p in $processes) {
    if ($p.Process.HasExited) {
        if ($p.Process.ExitCode -eq 0) {
            $status = "OK"
        } else {
            $status = "ERROR (exit $($p.Process.ExitCode))"
            $allOk = $false
        }
    } else {
        $status = "KILLED (timeout)"
        $allOk = $false
    }
    Write-Host "  $($p.Name): $status"
}

# ---------- Cleanup ----------
foreach ($v in @("VERIFICATION_ANALYST_PASSWORD", "LICENSING_OFFICER_PASSWORD", "CITIZEN_PASSWORD",
                 "LICENCE_ISSUED_DATE", "LICENCE_EXPIRY_DATE", "LICENCE_NUMBER")) {
    [Environment]::SetEnvironmentVariable($v, $null)
}

if ($allOk) {
    Write-Host "`nAll agents completed cleanly." -ForegroundColor Green
    exit 0
} else {
    Write-Host "`nOne or more agents failed — check logs in $logsDir" -ForegroundColor Yellow
    exit 1
}
