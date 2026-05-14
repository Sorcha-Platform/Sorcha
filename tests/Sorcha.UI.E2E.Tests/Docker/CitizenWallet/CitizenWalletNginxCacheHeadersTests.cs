// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

/// <summary>
/// Regression guard for PR #699. The PWA's nginx config had a single
/// regex location block marking every <c>/_framework/*.(dll|wasm|js)</c>
/// as <c>Cache-Control: public, immutable, max-age=31536000</c>. That
/// regex caught the two non-fingerprinted Blazor entry-point JS files
/// (<c>dotnet.js</c> and <c>blazor.webassembly.js</c>) — files whose
/// URLs are stable but whose contents change on every build, because
/// <c>dotnet.js</c> embeds the manifest of which fingerprinted wasm
/// files to load.
/// </summary>
/// <remarks>
/// <para>
/// When the PWA redeployed, browsers holding a year-cached
/// <c>dotnet.js</c> kept fetching the previous build's wasm fingerprints,
/// which 404 on the new server. Manifests as "the wallet won't navigate
/// after a redeploy on a returning device." PR #699 added exact-match
/// nginx locations that send no-cache on the two entry-point files only.
/// This test asserts that contract.
/// </para>
/// <para>
/// Phase 2 of issue #700.
/// </para>
/// </remarks>
public class CitizenWalletNginxCacheHeadersTests : DockerTestBase
{
    private static readonly string[] NonFingerprintedEntryPoints =
    [
        "/wallet/_framework/dotnet.js",
        "/wallet/_framework/blazor.webassembly.js",
    ];

    /// <summary>
    /// Each non-fingerprinted entry-point JS file MUST NOT carry the
    /// <c>immutable</c> directive. Either <c>no-cache</c> / <c>no-store</c>
    /// / <c>must-revalidate</c> is acceptable — any of these tells the
    /// browser to revalidate, which is what we need.
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task EntryPointJs_IsNotMarkedImmutable()
    {
        using var http = new HttpClient { BaseAddress = new Uri(TestConstants.UiWebUrl) };

        foreach (var path in NonFingerprintedEntryPoints)
        {
            using var resp = await http.GetAsync(path);
            resp.EnsureSuccessStatusCode();

            var cacheControl = string.Join(", ", resp.Headers.GetValues("Cache-Control")).ToLowerInvariant();

            Assert.That(cacheControl, Does.Not.Contain("immutable"),
                $"{path} MUST NOT be marked immutable — its URL is stable but its " +
                $"contents change every build (it embeds the wasm fingerprint manifest). " +
                $"A year-cached copy will reference wasm hashes that have been replaced. " +
                $"See PR #699 for the nginx fix.");

            var revalidates =
                cacheControl.Contains("no-cache") ||
                cacheControl.Contains("no-store") ||
                cacheControl.Contains("must-revalidate");
            Assert.That(revalidates, Is.True,
                $"{path} must carry at least one of no-cache / no-store / must-revalidate " +
                $"to ensure browsers revalidate after a redeploy. Got: '{cacheControl}'.");
        }
    }

    /// <summary>
    /// Sanity-check the matching positive case — fingerprinted wasm assets
    /// still get the <c>immutable</c> directive. If this fails, the fix in
    /// PR #699 was over-broadened and Blazor will lose its long-cache on
    /// hashed assets (slower repeat visits, more bandwidth).
    /// </summary>
    [Test]
    [Retry(2)]
    public async Task FingerprintedWasm_IsStillMarkedImmutable()
    {
        using var http = new HttpClient { BaseAddress = new Uri(TestConstants.UiWebUrl) };

        // Pull one fingerprinted wasm name out of the live dotnet.js manifest.
        // Anchoring on System.Private.CoreLib since it ships with every Blazor
        // app and won't be removed by a feature change.
        var dotnetJs = await http.GetStringAsync("/wallet/_framework/dotnet.js");
        var match = System.Text.RegularExpressions.Regex.Match(
            dotnetJs, @"System\.Private\.CoreLib\.[a-z0-9]+\.wasm");
        if (!match.Success)
        {
            Assert.Inconclusive(
                "Could not locate a fingerprinted CoreLib wasm reference in dotnet.js. " +
                "The Blazor manifest format may have changed — update this test's regex.");
            return;
        }

        var fingerprintedPath = $"/wallet/_framework/{match.Value}";
        using var resp = await http.GetAsync(fingerprintedPath);
        resp.EnsureSuccessStatusCode();

        var cacheControl = string.Join(", ", resp.Headers.GetValues("Cache-Control")).ToLowerInvariant();
        Assert.That(cacheControl, Does.Contain("immutable"),
            $"{fingerprintedPath} (fingerprinted wasm) should keep its " +
            $"long-cache 'immutable' directive — content is hash-keyed. " +
            $"Got: '{cacheControl}'.");
    }
}
