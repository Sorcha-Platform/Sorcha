// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO;
using FluentAssertions;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests;

/// <summary>
/// Bug fix — Settings → Security rendered raw localisation keys (e.g. "settings.accounts.title")
/// on the Wallet PWA. Two causes: (1) the translation JSON wasn't reachable (fixed by moving it into
/// the shared Sorcha.UI.Components.User RCL — see <c>LocalizationServiceTests</c>), and (2) the PWA's
/// <c>Program.cs</c> never called <c>ILocalizationService.LoadDefaultTranslationsAsync()</c> before
/// <c>RunAsync()</c>, unlike the web host (Sorcha.UI.Web.Client/Program.cs), which does. Top-level
/// statements in Program.cs can't be invoked directly from a test, so this asserts on the source —
/// the same pattern <c>LegacyVerifyRetirementTests</c> uses for a retirement guard. This is a static
/// check, not a runtime proof; the runtime fetch/resolve path is covered by
/// <c>LocalizationServiceTests.LoadDefaultTranslationsAsync_FetchesFromComponentsUserContentPath_AndResolvesRealText</c>.
/// </summary>
public sealed class ProgramInitializesLocalizationTests
{
    private static string ProgramCsPath => Path.Combine(
        "..", "..", "..", "..", "..",
        "src", "Apps", "Sorcha.Wallet.Pwa", "Program.cs");

    [Fact]
    public void ProgramCs_CallsLoadDefaultTranslationsAsync_BeforeRunAsync()
    {
        File.Exists(ProgramCsPath).Should().BeTrue(because: "the PWA entry point must exist at the expected path");
        var content = File.ReadAllText(ProgramCsPath);

        var loadIndex = content.IndexOf("LoadDefaultTranslationsAsync()", System.StringComparison.Ordinal);
        var runIndex = content.IndexOf("RunAsync()", System.StringComparison.Ordinal);

        loadIndex.Should().BeGreaterThan(-1,
            because: "the PWA must eagerly load default translations, mirroring the web host, or every " +
                     "Loc.T(key) call renders the raw key (this is exactly what a citizen saw on Settings → Security)");
        runIndex.Should().BeGreaterThan(-1);
        loadIndex.Should().BeLessThan(runIndex,
            because: "translations must be loaded before the app starts rendering, not raced with it");
    }
}
