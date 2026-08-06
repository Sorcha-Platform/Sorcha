# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

<#
.SYNOPSIS
    Fails the build if a WASM-consumed project takes a dependency that cannot load in Blazor WebAssembly.

.DESCRIPTION
    Sorcha.Provenance.Engine and Sorcha.Verification.Abstractions (feature 188) are guarded for a
    different reason from the rest: no WASM host consumes them TODAY. They are guarded because the
    provenance engine exists to run where the Register Service cannot — the Phase-3 portable evidence
    export an external auditor checks with their own tools — and that property is invisible until
    someone tries to build the export, by which point the offending dependency is load-bearing.
    Sorcha.Provenance.Engine.Tests asserts the same rule at runtime over its assembly closure; this
    gate is the static half, so a reference is caught before it is ever exercised.

    Three projects are consumed by Blazor WASM hosts (the citizen wallet PWA, and — from feature 185 — the
    offline reader app):

        Sorcha.Mdoc
        Sorcha.Proximity.Abstractions
        Sorcha.Verifier.Engine

    They MUST stay loadable in the browser. The failure mode this gate exists to prevent is the nasty one:
    a bad dependency here compiles cleanly, passes CI, ships — and then fails AT RUNTIME ON A PHONE.

    WHAT IS BANNED, AND WHY EACH:

      1. NATIVE P/Invoke packages  (Sodium.Core, Nethermind.MclBindings)
         Hard blocker: there is no native interop in the browser. This is precisely why Sorcha.Mdoc had to be
         extracted out of Sorcha.Cryptography (feature 185), and why Sorcha.Cryptography.Secp256k1 was split
         out before it (feature 177).

      2. X509Certificate2 / X509CertificateLoader / GetECDsaPublicKey
         Certificate handling under browser-wasm is unreliable. Sorcha.Mdoc parses x5chain leaves with
         BouncyCastle's X509CertificateParser instead (see Cose/X509Leaf.cs).

      3. ECDiffieHellman
         Not dependable under browser-wasm. Use BouncyCastle's ECDHBasicAgreement.

      4. AesGcm
         Avoided for the same reason. Sorcha.Mdoc uses BouncyCastle's GcmBlockCipher — and its correctness is
         pinned by the ISO Annex D ciphertext vectors, so the swap is not taken on faith.

    WHAT IS DELIBERATELY *NOT* BANNED — and this is the non-obvious part:

      System.Security.Cryptography.ECDsa (verify), SHA-256, HMAC-SHA256, HKDF.

      ECDsa verification DOES work under browser-wasm today. That is not a guess: Sorcha.Verifier.Engine
      uses ECDsa.Create + VerifyData for ES256, it is referenced by Sorcha.Wallet.Pwa, and that path is the
      live holder->device delegation check that the citizen wallet performs in the browser on every
      presentation. Banning it would be cargo-cult, would break a shipping feature, and would force a
      pointless rewrite of working code.

      New code in Sorcha.Mdoc nonetheless prefers BouncyCastle throughout (one crypto provider is easier to
      reason about than two), but that is a preference, not a portability requirement, and this gate does not
      pretend otherwise.

.NOTES
    Feature 185. Companion to check-trust-clean-break.ps1 / check-ledger-derived-clean-break.ps1.
#>

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

$guardedProjects = @(
    'src/Common/Sorcha.Mdoc',
    'src/Common/Sorcha.Provenance.Engine',
    'src/Common/Sorcha.Proximity.Abstractions',
    'src/Common/Sorcha.Proximity.Capacitor',
    'src/Common/Sorcha.Reader',
    'src/Common/Sorcha.Verification.Abstractions',
    'src/Common/Sorcha.Verifier.Engine'
)

# Native P/Invoke packages. Matched only on an actual PackageReference — a csproj COMMENT warning people off
# them is exactly what we want to encourage, so it must not trip the gate.
$bannedPackages = @(
    'Sodium.Core',
    'Nethermind.MclBindings'
)

# BCL APIs that are genuinely unreliable under browser-wasm. Matched as CODE, case-sensitively, so that a
# comment or an error-message string mentioning them does not trip the gate.
$bannedApiPatterns = @(
    @{ Pattern = 'X509Certificate2';       Fix = "use BouncyCastle's X509CertificateParser (see Cose/X509Leaf.cs)" },
    @{ Pattern = 'X509CertificateLoader';  Fix = "use BouncyCastle's X509CertificateParser (see Cose/X509Leaf.cs)" },
    @{ Pattern = 'GetECDsaPublicKey';      Fix = "use X509Leaf.TryReadEcPublicKey" },
    @{ Pattern = 'ECDiffieHellman';        Fix = "use BouncyCastle's ECDHBasicAgreement" },
    @{ Pattern = 'new AesGcm\(';           Fix = "use BouncyCastle's GcmBlockCipher (see MdocSessionCrypto)" }
)

# Server-side-only files where the above are permitted. Keep this list SHORT and justified.
$exemptions = @{
    'src/Common/Sorcha.Mdoc/MdocIssuer.cs' =
        'Issuance is server-side only — a browser never holds an issuer private key, so the reader app can never reach this path.'
}

$failures = @()

foreach ($project in $guardedProjects) {
    $projectPath = Join-Path $repoRoot $project
    if (-not (Test-Path $projectPath)) {
        Write-Host "[wasm-safe] SKIP  $project (not present)"
        continue
    }

    # --- 1. Native packages: only real PackageReference lines ---------------------------------
    Get-ChildItem -Path $projectPath -Filter '*.csproj' -Recurse | ForEach-Object {
        $csprojName = $_.Name
        $lineNumber = 0
        foreach ($line in (Get-Content $_.FullName)) {
            $lineNumber++
            if ($line -notmatch '<PackageReference') { continue }
            foreach ($package in $bannedPackages) {
                if ($line -match ('Include\s*=\s*"' + [regex]::Escape($package) + '"')) {
                    $failures += "$project/${csprojName}:${lineNumber}: references NATIVE package '$package'. " +
                                 'There is no P/Invoke in the browser — this project would fail to load.'
                }
            }
        }
    }

    # --- 2. BCL APIs unreliable under browser-wasm: code lines only ---------------------------
    Get-ChildItem -Path $projectPath -Filter '*.cs' -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
        if ($exemptions.ContainsKey($relative)) { return }

        $lineNumber = 0
        foreach ($line in (Get-Content $_.FullName)) {
            $lineNumber++

            # Skip comments — a comment explaining WHY we avoid an API is the most useful line in the file,
            # and it must never be the thing that fails the build.
            $trimmed = $line.TrimStart()
            if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('///')) { continue }

            # Strip string literals, so an exception message naming an API does not trip the gate.
            $code = [regex]::Replace($line, '"(?:[^"\\]|\\.)*"', '""')

            foreach ($banned in $bannedApiPatterns) {
                if ($code -cmatch $banned.Pattern) {
                    $failures += "${relative}:${lineNumber}: uses '$($banned.Pattern)', which is unreliable " +
                                 "under browser-wasm — it compiles, ships, and fails on the device. " +
                                 "Fix: $($banned.Fix). Line: $($line.Trim())"
                }
            }
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host '[wasm-safe] FAILED' -ForegroundColor Red
    Write-Host ''
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'These projects are consumed by Blazor WASM hosts (the citizen wallet PWA and the offline reader).'
    Write-Host 'A banned dependency here fails AT RUNTIME ON A PHONE, not at build time — which is why this gate exists.'
    Write-Host ''
    exit 1
}

Write-Host "[wasm-safe] PASS  $($guardedProjects.Count) project(s) verified WASM-loadable"
exit 0
