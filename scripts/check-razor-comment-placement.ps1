#!/usr/bin/env pwsh
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Razor comment placement CI gate.
#
# A Razor comment (@* ... *@) is stripped at compile time ONLY when it sits where markup is
# expected. Inside an element's attribute list it is not a comment at all — the compiler emits it
# as an attribute NAME, and the browser then throws at render time:
#
#   InvalidCharacterError: Failed to execute 'setAttribute' on 'Element':
#   '@* Issue #1278: a postcode is the canonical machine-checked value ... *@'
#   is not a valid attribute name
#
# That exception aborts the whole render batch, so the offending control AND whatever the renderer
# was going to emit next simply do not appear. On the AIAS "Your address" page this left Postcode
# and Country as bare labels with no input while the four fields above them rendered normally
# (issue: PostalAddressRenderRepro).
#
# Why a static gate rather than a test:
#   - The C# compiler accepts it. There is no build warning.
#   - bUnit does NOT catch it. PostcodeLookupRendererTests passed against the broken file, because
#     bUnit's AngleSharp DOM tolerates an attribute name a real browser rejects. Only a browser
#     reproduces this, and only at the moment the component renders.
#
# Detection: walk each .razor file tracking whether the cursor is inside an element tag (between
# `<tag` and its closing `>`). A `@*` encountered in that state is a violation. Comments in markup
# position, in @code blocks, and at file scope are all unaffected.
#
# Usage:
#   pwsh scripts/check-razor-comment-placement.ps1
#
# Exit codes:
#   0 — no violations
#   1 — a Razor comment sits inside an element's attribute list

[CmdletBinding()]
param(
    [string]$Root = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'

function Test-RazorFile {
    param([string]$Path)

    $text = Get-Content -Raw -LiteralPath $Path
    if ([string]::IsNullOrEmpty($text)) { return @() }

    $violations = @()
    $inTag = $false
    $line = 1
    $i = 0

    while ($i -lt $text.Length) {
        $c = $text[$i]

        if ($c -eq "`n") { $line++ }

        if (-not $inTag -and $c -eq '<' -and ($i + 1) -lt $text.Length -and
            ([char]::IsLetter($text[$i + 1]) -or $text[$i + 1] -eq '/')) {
            $inTag = $true
        }
        elseif ($inTag -and $c -eq '>') {
            $inTag = $false
        }
        elseif ($inTag -and $c -eq '@' -and ($i + 1) -lt $text.Length -and $text[$i + 1] -eq '*') {
            $violations += [pscustomobject]@{ Path = $Path; Line = $line }

            # Skip past the comment so one violation is reported per comment, not per character.
            $close = $text.IndexOf('*@', $i)
            if ($close -lt 0) { break }
            for ($k = $i; $k -lt $close; $k++) { if ($text[$k] -eq "`n") { $line++ } }
            $i = $close + 2
            continue
        }

        $i++
    }

    return $violations
}

$srcRoot = Join-Path $Root 'src'
$files = Get-ChildItem -Path $srcRoot -Recurse -Filter '*.razor' -File |
    Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }

$all = @()
foreach ($file in $files) {
    $all += Test-RazorFile -Path $file.FullName
}

if ($all.Count -gt 0) {
    Write-Host "FAIL: Razor comment inside an element's attribute list." -ForegroundColor Red
    Write-Host ""
    Write-Host "In that position the compiler emits the comment as an ATTRIBUTE NAME. The browser" -ForegroundColor Red
    Write-Host "throws InvalidCharacterError at render time and the render batch aborts, so the" -ForegroundColor Red
    Write-Host "control and whatever renders after it silently disappear. Move the comment ABOVE" -ForegroundColor Red
    Write-Host "the element." -ForegroundColor Red
    Write-Host ""
    foreach ($v in $all) {
        $relative = [IO.Path]::GetRelativePath($Root, $v.Path)
        Write-Host "  $relative`:$($v.Line)" -ForegroundColor Yellow
    }
    exit 1
}

Write-Host "OK: no Razor comments inside attribute lists ($($files.Count) .razor files scanned)." -ForegroundColor Green
exit 0
