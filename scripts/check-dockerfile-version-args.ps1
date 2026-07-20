#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Unified-versioning CI gate: every Dockerfile must opt in to the build-args.
#
# The root Directory.Build.props derives ONE version for every component from
# GITHUB_RUN_NUMBER / GITHUB_RUN_ATTEMPT, falling back to <Major>.0.0-dev when
# they are unset. docker-publish.yml passes both as --build-arg to every image.
#
# Docker build-args are OPT-IN. A Dockerfile that does not declare a matching
# `ARG` silently DISCARDS the value — the build then sees no env var, MSBuild
# takes the local -dev branch, and the image ships an assembly stamped
# "2.0.0-dev" while the image itself is tagged "2.<run>.<attempt>". The tag and
# the payload disagree, and nothing fails: not the build, not the tests.
#
# That is exactly what happened — 12 of 14 Dockerfiles were missing the block,
# so /app reported v2.0.7-dev, and every service's OpenAPI info.version and the
# MCP manifest reported a -dev version in production. This gate exists so the
# next Dockerfile added to the repo cannot reintroduce it.
#
# Usage:
#   pwsh scripts/check-dockerfile-version-args.ps1
#
# Exit codes:
#   0 — every Dockerfile declares both ARGs and both ENVs
#   1 — at least one Dockerfile is missing the block

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

$required = @(
    'ARG GITHUB_RUN_NUMBER'
    'ARG GITHUB_RUN_ATTEMPT'
    'ENV GITHUB_RUN_NUMBER='
    'ENV GITHUB_RUN_ATTEMPT='
)

Push-Location $RepoRoot
try {
    $dockerfiles = git ls-files '*Dockerfile*' | Where-Object { $_ }
}
finally {
    Pop-Location
}

if (-not $dockerfiles) {
    Write-Error 'No Dockerfiles found — is this being run outside the repository?'
    exit 1
}

$violations = @()

foreach ($relative in $dockerfiles) {
    $full = Join-Path $RepoRoot $relative
    if (-not (Test-Path $full)) { continue }

    $content = Get-Content -Raw -LiteralPath $full
    $missing = $required | Where-Object { $content -notlike "*$_*" }

    if ($missing) {
        $violations += [pscustomobject]@{
            File    = $relative
            Missing = ($missing -join ', ')
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host ''
    Write-Host 'Unified-versioning gate FAILED.' -ForegroundColor Red
    Write-Host ''
    Write-Host 'These Dockerfiles do not surface the CI build-args to MSBuild, so the' -ForegroundColor Red
    Write-Host 'assemblies they publish will be stamped <Major>.0.0-dev while the image' -ForegroundColor Red
    Write-Host 'is tagged 2.<run>.<attempt>:' -ForegroundColor Red
    Write-Host ''
    foreach ($v in $violations) {
        Write-Host ("  {0}" -f $v.File) -ForegroundColor Yellow
        Write-Host ("      missing: {0}" -f $v.Missing) -ForegroundColor DarkYellow
    }
    Write-Host ''
    Write-Host 'Add this block AFTER the `COPY src/ ...` line (after restore, so the' -ForegroundColor Cyan
    Write-Host 'cacheable restore layer is not invalidated on every run):' -ForegroundColor Cyan
    Write-Host ''
    Write-Host '  ARG GITHUB_RUN_NUMBER'
    Write-Host '  ARG GITHUB_RUN_ATTEMPT'
    Write-Host '  ENV GITHUB_RUN_NUMBER=${GITHUB_RUN_NUMBER}'
    Write-Host '  ENV GITHUB_RUN_ATTEMPT=${GITHUB_RUN_ATTEMPT}'
    Write-Host ''
    exit 1
}

Write-Host ("Unified-versioning gate passed — {0} Dockerfile(s) declare the build-args." -f @($dockerfiles).Count) -ForegroundColor Green
exit 0
