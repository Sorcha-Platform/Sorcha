# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# PropertyInspection actor launcher — creates blueprint instance and runs agents.
<#
.SYNOPSIS
    Launches autonomous actor agents for the PropertyInspection walkthrough.
.DESCRIPTION
    Creates a blueprint instance and starts 4 independent sorcha-agent processes
    (one per participant) that autonomously execute the PropertyInspection workflow.
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

$ErrorActionPreference = 'Stop'
$walkthroughDir = $PSScriptRoot
$actorsDir = Join-Path $walkthroughDir "actors"

# ── Module ────────────────────────────────────────────────────────
$modulePath = Join-Path $walkthroughDir ".." "modules" "SorchaWalkthrough" "SorchaWalkthrough.psm1"
Import-Module $modulePath -Force

Write-WtBanner "PropertyInspection — Actor Launcher"

# ── Load state ────────────────────────────────────────────────────
if (-not $StatePath) { $StatePath = Join-Path $walkthroughDir "state.json" }
if (-not (Test-Path $StatePath)) {
    Write-Error "State file not found: $StatePath. Run setup.ps1 first."
    exit 1
}
$state = Get-Content $StatePath -Raw | ConvertFrom-Json

# ── Environment ───────────────────────────────────────────────────
$sorchaEnv = Initialize-SorchaEnvironment -Profile $Profile -SkipHealthCheck

# ── Create blueprint instance ─────────────────────────────────────
Write-WtStep "Creating blueprint instance"

$housingRole = $state.roles."housing-officer"
$housingSession = Connect-SorchaUser -TenantUrl $state.tenantUrl `
    -Email $housingRole.email -Password $housingRole.password `
    -OrganizationId $housingRole.organizationId

$instance = Invoke-SorchaApi -Method POST `
    -Uri "$($state.blueprintUrl)/instances/" `
    -Headers $housingSession.Headers `
    -Body @{
        blueprintId = $state.blueprintId
        registerId  = $state.registerId
        tenantId    = $housingRole.organizationId
    }

# ${instance}?.id, NOT $instance?.id — "?" is a legal character in a PowerShell variable name, so
# the unbraced form parses as ${instance?}.id, reads an undefined variable, and yields $null. The
# guard was therefore ALWAYS true: this script reported "Failed to create blueprint instance" and
# exited 1 on every run, including the runs where the instance was created perfectly well. Proven by
# printing the value beside the check — id 36c8bb1a-… present, guard still firing (#1427).
if (-not ${instance}?.id) {
    Write-Error "Failed to create blueprint instance."
    exit 1
}
Write-WtSuccess "Instance created: $($instance.id)"

# ── Resolve agent command ─────────────────────────────────────────
if (-not $AgentBinary) {
    $agentProject = (Resolve-Path (Join-Path $walkthroughDir ".." ".." "src" "Apps" "Sorcha.Agent" "Sorcha.Agent.csproj")).Path

    # Build ONCE, here, then run every agent with --no-build.
    #
    # Four `dotnet run` processes start within milliseconds of each other and each tries to build the
    # same project into the same obj/bin. They collide on the intermediate assemblies and all four die
    # with CS2012 "Cannot open ... for writing ... being used by another process" — a build race that
    # reads like a Sorcha failure and has nothing to do with the workflow. Serialising the build is
    # enough; the agents themselves are independent (#1427).
    Write-WtInfo "Building sorcha-agent once (four concurrent 'dotnet run' builds race on obj/bin)..."
    $buildLog = & dotnet build $agentProject -v quiet --nologo 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildLog | Select-Object -Last 20 | ForEach-Object { Write-Host $_ }
        Write-Error "sorcha-agent failed to build — cannot launch actors."
        exit 1
    }

    $agentCmd = "dotnet"
    $agentBaseArgs = @("run", "--project", $agentProject, "--no-build", "--")
} else {
    $agentCmd = $AgentBinary
    $agentBaseArgs = @()
}

# ── Actor configurations ──────────────────────────────────────────
$actors = @(
    @{ File = "tenant.json"; EnvVar = "TENANT_PASSWORD"; PasswordKey = "tenant" }
    @{ File = "housing-officer.json"; EnvVar = "HOUSING_OFFICER_PASSWORD"; PasswordKey = "housing-officer" }
    @{ File = "contractor.json"; EnvVar = "CONTRACTOR_PASSWORD"; PasswordKey = "contractor" }
    @{ File = "building-inspector.json"; EnvVar = "BUILDING_INSPECTOR_PASSWORD"; PasswordKey = "building-inspector" }
)

# ── Set password env vars ─────────────────────────────────────────
foreach ($actor in $actors) {
    $role = $state.roles.($actor.PasswordKey)
    if ($role -and $role.password) {
        [Environment]::SetEnvironmentVariable($actor.EnvVar, $role.password)
    }
}

# ── Launch actors ─────────────────────────────────────────────────
$logsDir = Join-Path $walkthroughDir "logs"
New-Item -Path $logsDir -ItemType Directory -ErrorAction SilentlyContinue | Out-Null

$processes = @()
foreach ($actor in $actors) {
    $configPath = Join-Path $actorsDir $actor.File
    if (-not (Test-Path $configPath)) {
        Write-WtWarn "Actor config not found: $configPath"
        continue
    }

    $logFile = Join-Path $logsDir ($actor.File -replace '\.json$', '.log')
    $agentArgs = $agentBaseArgs + @("run", "--config", $configPath, "--state", $StatePath)

    Write-WtInfo "Launching $($actor.File)..."
    $proc = Start-Process -FilePath $agentCmd -ArgumentList $agentArgs `
        -RedirectStandardOutput $logFile `
        -RedirectStandardError "$logFile.err" `
        -PassThru -NoNewWindow

    $processes += @{ Process = $proc; Name = $actor.File; LogFile = $logFile }
}

Write-WtSuccess "All $($processes.Count) actors launched"

# ── Wait for completion ───────────────────────────────────────────
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
$allExited = $false

while ((Get-Date) -lt $deadline) {
    $running = $processes | Where-Object { -not $_.Process.HasExited }
    if ($running.Count -eq 0) { $allExited = $true; break }

    $runningNames = ($running | ForEach-Object { $_.Name }) -join ", "
    Write-Host "`r  Waiting... ($($running.Count) running: $runningNames)" -NoNewline
    Start-Sleep -Seconds 5
}
Write-Host ""

# ── Shutdown & summary ────────────────────────────────────────────
if (-not $allExited) {
    Write-WtWarn "Timeout after $TimeoutMinutes minutes — killing remaining actors"
    $running = $processes | Where-Object { -not $_.Process.HasExited }
    foreach ($p in $running) {
        try { $p.Process.Kill() } catch { }
    }
}

# Clean up env vars
foreach ($actor in $actors) {
    [Environment]::SetEnvironmentVariable($actor.EnvVar, $null)
}

# Summary
Write-WtBanner "Results"
foreach ($p in $processes) {
    $exitCode = $p.Process.ExitCode
    $status = if ($exitCode -eq 0) { "[PASS]" } else { "[FAIL] (exit $exitCode)" }
    Write-Host "  $status $($p.Name)"
    if ($exitCode -ne 0 -and (Test-Path $p.LogFile)) {
        Write-Host "    Log: $($p.LogFile)"
        Get-Content $p.LogFile | Select-Object -Last 5 | ForEach-Object { Write-Host "    $_" }
    }
}

$failed = $processes | Where-Object { $_.Process.ExitCode -ne 0 }
if ($failed.Count -gt 0) {
    Write-WtWarn "$($failed.Count) actor(s) failed"
    exit 1
} else {
    Write-WtSuccess "All actors completed successfully"
}
