#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Walkthrough-secrets contract CI gate.
#
# Every walkthrough script that calls
#
#     Get-SorchaSecrets -WalkthroughName "<key>"
#
# needs a matching "<key>" block in walkthroughs/initialize-secrets.ps1, which is the ONLY thing
# that generates walkthroughs/.secrets/passwords.json.
#
# Why this is gated rather than left to review:
#
#   The two sides are hand-maintained lists in different files and nothing relates them. Add a
#   walkthrough and forget the generator entry, and there is no compile error, no failing test,
#   and nothing at all until somebody runs that walkthrough on a machine whose passwords.json was
#   generated before it existed — where it dies in setup at
#
#       No secrets found for walkthrough 'cyber-essentials-uac' in .../passwords.json
#
#   That reads like a local-environment problem ("regenerate your secrets"), so the natural
#   response is `initialize-secrets.ps1 -Force`, which does not help because the key was never in
#   the generator. Both cyber-essentials-uac and ping-pong-n1 were missing this way (#1427); only
#   the first was noticed, and only because someone tried to run it.
#
# The gate derives BOTH sides from source — the requested keys by scanning the walkthroughs, the
# provided keys by parsing the generator's own hashtable — so it cannot itself drift out of date.

param([switch]$Quiet)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$walkthroughsDir = Join-Path $repoRoot 'walkthroughs'
$generator = Join-Path $walkthroughsDir 'initialize-secrets.ps1'

if (-not (Test-Path $generator)) {
    Write-Host "[FAIL] Secrets generator is missing: $generator" -ForegroundColor Red
    Write-Host "       It is the single source of walkthrough credentials; this gate reads its keys."
    exit 1
}

# --- Side A: keys the generator provides -------------------------------------------------------
# Match the quoted keys of the $secrets [ordered]@{ ... } literal: lines of the form
#     "key-name" = @{
$generatorText = Get-Content $generator -Raw
$providedKeys = [regex]::Matches($generatorText, '(?m)^\s*"(?<key>[a-z0-9][a-z0-9-]*)"\s*=\s*@\{') |
    ForEach-Object { $_.Groups['key'].Value } |
    Sort-Object -Unique

# Vacuous-guard protection: if the literal is ever reshaped so the regex stops matching, the gate
# would silently "pass" against zero provided keys AND (worse) report every walkthrough as broken.
# Assert a plausible floor instead of trusting an empty parse.
if ($providedKeys.Count -lt 10) {
    Write-Host "[FAIL] Parsed only $($providedKeys.Count) credential blocks from $generator." -ForegroundColor Red
    Write-Host "       Expected at least 10. The generator's hashtable shape probably changed —"
    Write-Host "       fix this gate's parser rather than the generator."
    exit 1
}

# --- Side B: keys the walkthroughs ask for -----------------------------------------------------
$requested = @{}   # key -> list of "relative/path.ps1:line"
$scripts = Get-ChildItem -Path $walkthroughsDir -Filter '*.ps1' -Recurse -File |
    Where-Object { $_.FullName -ne $generator }

foreach ($file in $scripts) {
    $lineNo = 0
    foreach ($line in (Get-Content $file.FullName)) {
        $lineNo++
        foreach ($m in [regex]::Matches($line, 'Get-SorchaSecrets\s+-WalkthroughName\s+"(?<key>[^"]+)"')) {
            $key = $m.Groups['key'].Value
            $rel = $file.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
            if (-not $requested.ContainsKey($key)) { $requested[$key] = @() }
            $requested[$key] += "${rel}:${lineNo}"
        }
    }
}

# Same protection on this side: an empty scan must fail loudly, not pass quietly.
if ($requested.Count -lt 10) {
    Write-Host "[FAIL] Found only $($requested.Count) Get-SorchaSecrets call sites under $walkthroughsDir." -ForegroundColor Red
    Write-Host "       Expected at least 10. The call shape probably changed — fix this gate's scanner."
    exit 1
}

# --- The join ----------------------------------------------------------------------------------
$missing = $requested.Keys | Where-Object { $providedKeys -notcontains $_ } | Sort-Object

if ($missing) {
    Write-Host ""
    Write-Host "[FAIL] Walkthroughs request credential keys that initialize-secrets.ps1 does not generate:" -ForegroundColor Red
    foreach ($key in $missing) {
        Write-Host ""
        Write-Host "  '$key' requested by:" -ForegroundColor Yellow
        foreach ($site in $requested[$key]) { Write-Host "    $site" }
    }
    Write-Host ""
    # NB @(...) — a single missing key is a bare string, and [0] would index its first CHARACTER.
    Write-Host "  Fix: add a `"$(@($missing)[0])`" = @{ ... } block to walkthroughs/initialize-secrets.ps1"
    Write-Host "  with the fields that walkthrough's scripts read (adminEmail / adminPassword /"
    Write-Host "  DefaultPassword / per-role emails). Do NOT work around it by hand-editing a local"
    Write-Host "  .secrets/passwords.json — that fixes one machine and leaves the walkthrough broken"
    Write-Host "  everywhere else."
    Write-Host ""
    exit 1
}

# Unused generator entries are reported but do NOT fail: a walkthrough may legitimately be
# script-less (UI-driven demos) or read its block through a path this scanner cannot see.
$unused = $providedKeys |
    Where-Object { $_ -ne 'platform' -and $requested.Keys -notcontains $_ } |
    Sort-Object

if (-not $Quiet) {
    Write-Host "[OK] All $($requested.Count) requested walkthrough credential keys are generated by initialize-secrets.ps1." -ForegroundColor Green
    if ($unused) {
        Write-Host "     Note — generated but not requested by any script (not a failure): $($unused -join ', ')" -ForegroundColor DarkGray
    }
}

exit 0
