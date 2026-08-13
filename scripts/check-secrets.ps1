#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Secret-scan CI gate.
#
# No secret scanning existed for this repo before this gate. It fails the
# build when a C#/config/script/compose file under the scanned roots
# contains something that looks like a hardcoded credential — a
# client/service secret, API key, password, or a PEM private key block —
# outside the explicit allowlist (.secrets-allowlist).
#
# The allowlist is a ratchet — it may only shrink, never grow. See
# .secrets-allowlist for the two groups it's currently seeded with:
# docker-compose*.yml (8 ServiceAuth__ClientSecret literals in
# docker-compose.yml, grandfathered pending #1397's per-deploy secret model)
# and a set of pre-existing local-dev bootstrap passwords (the
# "Dev_Pass_2025!" / "sorcha_dev_password" family) that predate this gate
# and are not part of #1397's scope. Remove entries as each is cleaned up.
#
# Usage:
#   pwsh scripts/check-secrets.ps1
#
# Exit codes:
#   0 — no violations
#   1 — secret-like pattern found outside the allowlist (or allowlist file missing)

[CmdletBinding()]
param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = $RepoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)
$allowlistPath = Join-Path $repo '.secrets-allowlist'

if (-not (Test-Path -LiteralPath $allowlistPath)) {
    Write-Error "Allowlist file not found: $allowlistPath"
    exit 1
}

# Load the allowlist — strip blank lines and comments. Normalise to forward
# slashes so the comparison works on both Windows and Linux runners.
$allowed = @{}
foreach ($line in (Get-Content -LiteralPath $allowlistPath)) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0) { continue }
    if ($trimmed.StartsWith('#')) { continue }
    $allowed[$trimmed.Replace('\', '/')] = $true
}

# Patterns that indicate a hardcoded secret. Each is a candidate — lines that
# match are then filtered through Test-Placeholder below to drop obvious
# non-secrets (env var interpolation, `${VAR}`, "changeme", etc.) before
# being treated as a real violation.
#
# Two shapes are flagged:
#   - A quoted literal directly assigned to a secret-shaped key (JSON, C#,
#     Razor, JS) — `"ClientSecret": "abc12345"`, `password = "abc12345"`.
#   - An unquoted key: value pair (YAML / docker-compose / .env style) whose
#     value contains a digit, underscore, or hyphen — this is what
#     distinguishes a real secret token ("AKIAABCDEFGH12345678",
#     "blueprint-service-secret", "sorcha_dev_password") from code merely
#     echoing a variable of the same name into a property
#     (`ClientSecret = clientSecret`), which is pure camelCase letters and
#     has none of the three. The trailing negative lookahead makes sure the
#     whole token was captured (not a truncated prefix of a longer
#     identifier or method call).
$patterns = @(
    '(?i)(client_?secret|service[-_]secret|api[-_]?key|password)\s*[:=]\s*["''][A-Za-z0-9_\-\.\!\@\#\$%^&*]{8,}["'']',
    '(?i)(client_?secret|service[-_]secret|api[-_]?key|password)\s*[:=]\s*(?=[A-Za-z0-9_\-]*[0-9_\-])[A-Za-z0-9_\-]{8,}(?![A-Za-z0-9_\-])',
    '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
)

function Test-Placeholder {
    param([string]$Line)

    # ${VAR} / ${VAR:-default} shell/compose interpolation — not a literal secret.
    if ($Line -match '\$\{') { return $true }
    # PowerShell env var reference.
    if ($Line -match '\$env:') { return $true }
    # Obvious filler / placeholder tokens, including the "your-x" / "yourpassword"
    # family used throughout READMEs for example connection strings.
    if ($Line -match '(?i)(change[-_]?me|replace[-_]?me|\byour[-_]?\w*|placeholder|example\.com|<[a-z0-9_-]+>|xxxxxxxx|dummy[-_]?value)') { return $true }
    # A value assigned directly from a C#/Razor identifier that starts with an
    # underscore is a private-field or local-variable reference under this
    # repo's `_camelCase` naming convention (CLAUDE.md), not a literal string
    # — e.g. `password = _password`, `ClientSecret="_newClientSecret"`.
    if ($Line -match '[:=]\s*["'']?_[A-Za-z]') { return $true }

    return $false
}

# Files to scan: everything under src/ and scripts/, plus root-level
# docker-compose*.yml (non-recursive — matches the workflow's path trigger,
# which does not use `**` for that entry). obj/bin build output and vendored
# third-party bundles under wwwroot/lib are excluded — a secret-shaped token
# in a minified bootstrap.js is not this repo's problem to fix.
$scanRoots = @(
    "$repo/src",
    "$repo/scripts"
)

$candidates = @()
foreach ($root in $scanRoots) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    $candidates += Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' -and $_.FullName -notmatch '[\\/]wwwroot[\\/]lib[\\/]' }
}

$candidates += Get-ChildItem -Path $repo -Filter 'docker-compose*.yml' -File -ErrorAction SilentlyContinue

$violations = @()
$matchedAllowed = @{}

foreach ($file in $candidates) {
    $rel = [IO.Path]::GetRelativePath($repo, $file.FullName).Replace('\', '/')
    $lineNum = 0
    $fileHasMatch = $false

    foreach ($line in (Get-Content -LiteralPath $file.FullName -ErrorAction SilentlyContinue)) {
        $lineNum++

        # Skip comment-only lines — narrative mentions of what a secret
        # pattern looks like (including in this very script) aren't usage.
        # Covered shapes: C# // and /* */, script #, HTML/Razor <!-- and @*.
        $trimmedForCommentCheck = $line.TrimStart()
        if ($trimmedForCommentCheck -match '^(//|/\*|\*|#|<!--|@\*)') { continue }

        foreach ($p in $patterns) {
            if ($line -match $p) {
                if (Test-Placeholder -Line $line) { continue }

                $fileHasMatch = $true
                if (-not $allowed.ContainsKey($rel)) {
                    $violations += [pscustomobject]@{
                        File    = $rel
                        Line    = $lineNum
                        Snippet = $line.Trim()
                    }
                }
                break
            }
        }
    }

    if ($fileHasMatch -and $allowed.ContainsKey($rel)) {
        $matchedAllowed[$rel] = $true
    }
}

# Detect stale allowlist entries — files listed but no longer containing any
# flagged pattern. These should be removed from the allowlist as part of the
# same PR that cleaned them up.
$stale = @()
foreach ($entry in $allowed.Keys) {
    if (-not $matchedAllowed.ContainsKey($entry)) {
        $stale += $entry
    }
}

$failed = $false

if ($violations.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: possible hardcoded secret found in files not on the allowlist." -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
        Write-Host ("  {0}:{1}  {2}" -f $v.File, $v.Line, $v.Snippet) -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Do not commit real secrets. Use environment variables, a secret store, or" -ForegroundColor Yellow
    Write-Host "per-deploy configuration for anything a public node ships with. See CLAUDE.md."
}

if ($stale.Count -gt 0) {
    $failed = $true
    Write-Host ""
    Write-Host "FAIL: stale allowlist entries — files no longer contain any flagged pattern:" -ForegroundColor Red
    foreach ($s in $stale) {
        Write-Host "  - $s" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Remove these lines from .secrets-allowlist in the same PR that cleaned them up."
    Write-Host "The allowlist is a ratchet — it may only shrink."
}

if ($failed) {
    exit 1
}

$remaining = $allowed.Count
Write-Host ("OK: secret-scan gate passed. {0} files still on the allowlist." -f $remaining) -ForegroundColor Green
if ($remaining -eq 0) {
    Write-Host "  Allowlist is empty." -ForegroundColor Green
}
exit 0
