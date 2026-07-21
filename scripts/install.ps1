# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
# =============================================================================
# Sorcha one-line installer (Windows / PowerShell).
#
# Downloads Sorcha and runs the interactive setup. Requires git, Docker Desktop,
# and a bash shell — Git for Windows bundles one (Git Bash), or use WSL.
#
#   irm https://raw.githubusercontent.com/Sorcha-Platform/Sorcha/master/scripts/install.ps1 | iex
#
# Prefer to read it first (recommended for any pipe-to-shell):
#   irm https://raw.githubusercontent.com/Sorcha-Platform/Sorcha/master/scripts/install.ps1 -OutFile sorcha-install.ps1
#   notepad sorcha-install.ps1 ; ./sorcha-install.ps1
#
# Environment overrides: $env:SORCHA_DIR (default: sorcha), $env:SORCHA_REF (default: master).
# =============================================================================
$ErrorActionPreference = 'Stop'

function Say($m) { Write-Host "[sorcha-install] $m" -ForegroundColor Cyan }
function Die($m) { Write-Host "[sorcha-install] ERROR: $m" -ForegroundColor Red; exit 1 }

if (-not (Get-Command git    -ErrorAction SilentlyContinue)) { Die "git is required: https://git-scm.com/downloads" }
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Die "Docker Desktop is required: https://docs.docker.com/get-docker/" }
if (-not (Get-Command bash   -ErrorAction SilentlyContinue)) { Die "A bash shell is required (install Git for Windows or enable WSL), then re-run." }

$dir = if ($env:SORCHA_DIR) { $env:SORCHA_DIR } else { 'sorcha' }
$ref = if ($env:SORCHA_REF) { $env:SORCHA_REF } else { 'master' }

if (Test-Path 'scripts/sorcha-setup.sh') {
    Say 'Existing Sorcha checkout detected here — running setup in place.'
    $target = (Get-Location).Path
}
elseif (Test-Path "$dir/scripts/sorcha-setup.sh") {
    Say "Using existing clone at ./$dir."
    $target = (Resolve-Path $dir).Path
}
else {
    if (Test-Path $dir) { Die "Target ./$dir already exists but is not a Sorcha clone. Set `$env:SORCHA_DIR to another path." }
    Say "Cloning Sorcha ($ref) into ./$dir ..."
    git clone --depth 1 --branch $ref https://github.com/Sorcha-Platform/Sorcha.git $dir
    if ($LASTEXITCODE -ne 0) { Die 'git clone failed. Check your network and that the ref is valid.' }
    $target = (Resolve-Path $dir).Path
}

Set-Location $target
Say 'Handing off to the interactive setup (scripts/sorcha-setup.sh) via bash.'
Write-Host ''
& bash scripts/sorcha-setup.sh @args
exit $LASTEXITCODE
