#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# JSON-options CI gate. Every client-side System.Text.Json HTTP-response deserialization must pass
# EXPLICIT serializer options — the shared source of truth `Sorcha.Serialization.SorchaJson.Options`
# (aliased as `JsonDefaults.Api` in the UI) or a deliberately-kept local options object. A BARE
# `ReadFromJsonAsync`/`GetFromJsonAsync` uses default options that lack the kebab-case string-enum
# converter the services emit (e.g. "self-asserted", WebAuthn "public-key"), so it throws on the enum
# and the caller silently falls back to empty/failed — the "My Profile is empty though it saved" /
# "couldn't load your security settings" class of bug.
#
# Fails if any bare call is found in the UI / PWA / service-client projects.

$ErrorActionPreference = 'Stop'

$roots = @(
    'src/Apps/Sorcha.UI',
    'src/Apps/Sorcha.Wallet.Pwa',
    'src/Common/Sorcha.ServiceClients.Http'
)

# A call passing ANY options object (shared or a kept local field) is fine — the gate only stops
# genuinely bare calls. This marker matches the shared options and the local options field names in use.
$optionsMarker = 'JsonDefaults\.Api|SorchaJson\.Options|JsonOptions|Options|options:|CanonicalOptions|JsonOpts|CaseInsensitive'
$callPattern = '\.(ReadFromJsonAsync|GetFromJsonAsync)<'

$violations = New-Object System.Collections.Generic.List[string]

foreach ($root in $roots) {
    if (-not (Test-Path $root)) { continue }
    $files = Get-ChildItem -Path $root -Recurse -Include *.cs, *.razor -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
    foreach ($file in $files) {
        $lines = Get-Content -LiteralPath $file.FullName
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match $callPattern) {
                $end = [Math]::Min($i + 2, $lines.Count - 1)
                $span = ($lines[$i..$end]) -join "`n"
                if ($span -notmatch $optionsMarker) {
                    $rel = Resolve-Path -Relative -LiteralPath $file.FullName
                    $violations.Add(("{0}:{1}: {2}" -f $rel, ($i + 1), $lines[$i].Trim()))
                }
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "JSON-options gate FAILED - bare client deserialization found." -ForegroundColor Red
    Write-Host "Pass the shared options: SorchaJson.Options (Sorcha.Serialization) / JsonDefaults.Api (UI)." -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  $v" }
    exit 1
}

Write-Host "JSON-options gate OK: no bare client JSON deserialization calls." -ForegroundColor Green
