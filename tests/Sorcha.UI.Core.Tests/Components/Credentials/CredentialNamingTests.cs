// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Components.Wallet;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Credentials;

/// <summary>
/// Guards issue #1278: five credential surfaces rendered the raw <c>vct</c> URI where a human name
/// exists.
/// <para>
/// Post-#1187 the vct is a URI <b>by design</b>, so it must never be used as a label. Three
/// near-copies of the humanising logic existed and only <see cref="CredentialDisplay.Humanize"/> —
/// the one the PWA uses — extracted the URI tail; the two web copies split PascalCase only and let a
/// URI through verbatim. Both now delegate here, so these tests pin the behaviour all three surfaces
/// depend on.
/// </para>
/// </summary>
public sealed class CredentialNamingTests
{
    /// <summary>The exact string a citizen saw as their credential's title, five times over.</summary>
    [Theory]
    [InlineData("https://sorcha.dev/vc/assured-identity/v1", "assured identity")]
    [InlineData("https://sorcha.dev/vc/citizen-device-delegation/v1", "citizen device delegation")]
    public void Humanize_VctUri_ReadsAsItsTypeName_NotTheUri(string vct, string expectedLower)
    {
        var result = CredentialDisplay.Humanize(vct);

        result.Should().NotContain("https", "a URI must never reach a citizen as a label (#1278)");
        result.Should().NotContain("/");
        result.ToLowerInvariant().Should().Be(expectedLower);
    }

    [Fact]
    public void Humanize_PascalCaseType_SplitsAndDropsCredentialSuffix()
        => CredentialDisplay.Humanize("AssuredIdentityCredential").Should().Be("Assured Identity");

    [Fact]
    public void Humanize_NullOrEmpty_FallsBackToAWord_NotAnEmptyLabel()
    {
        CredentialDisplay.Humanize(null).Should().Be("Credential");
        CredentialDisplay.Humanize("").Should().Be("Credential");
        CredentialDisplay.Humanize("   ").Should().Be("Credential");
    }

    /// <summary>
    /// A bare "Credential" type must not collapse to an empty string — an untitled card is worse than
    /// a generically-titled one.
    /// </summary>
    [Fact]
    public void Humanize_BareCredential_StaysNamed()
        => CredentialDisplay.Humanize("Credential").Should().Be("Credential");

    /// <summary>
    /// The issuer line must never be a raw DID. #1278 reported an id-card reading
    /// "ISSUED BY WS11QRXNMCHLCS9LXFFT…" — a truncated, CSS-uppercased wallet address where the org
    /// name belongs. Shortening is the correct fallback ONLY when no org name is known; the fix is that
    /// callers now prefer the org name (see CredentialApiService / CredentialDetailResponse).
    /// </summary>
    [Fact]
    public void ShortenDid_NeverEmitsTheDidPrefix()
    {
        var result = CredentialDisplay.ShortenDid("did:sorcha:org:WS11QRXNMCHLCS9LXFFTABCDEFGHIJK");

        result.Should().NotStartWith("did:");
        result.Should().Contain("…", "a shortened identifier must read as shortened, not as a whole value");
    }
}
