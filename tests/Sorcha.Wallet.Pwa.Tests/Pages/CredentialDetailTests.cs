// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Services;
using Xunit;
using DetailPage = Sorcha.Wallet.Pwa.Pages.CredentialDetail;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// bUnit tests for the redesigned credential detail page: decoded claims,
/// progressive disclosure of machine identifiers, and no stale "libsodium" copy.
/// </summary>
public sealed class CredentialDetailTests : ComponentTestFixture
{
    private readonly Mock<ICredentialCache> _cache = new();

    public CredentialDetailTests() => Services.AddSingleton(_cache.Object);

    // The detail page uses MudExpansionPanels, which asserts a MudPopoverProvider
    // in the tree (provided by MainLayout in the app).
    private static RenderFragment PageWithProvider(Guid id) => b =>
    {
        b.OpenComponent<MudPopoverProvider>(0);
        b.CloseComponent();
        b.OpenComponent<DetailPage>(1);
        b.AddAttribute(2, nameof(DetailPage.Id), id);
        b.CloseComponent();
    };

    private static string Disclosure(string name, string value) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new[] { "salt", name, value }));

    /// <summary>The digest of a disclosure as it appears in an <c>_sd</c> array: base64url(SHA256(ascii(disclosure))).</summary>
    private static string Digest(string disclosure) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(disclosure)));

    private static CachedCredential CredWithClaims(Guid id)
    {
        var exp = DateTimeOffset.UtcNow.AddDays(90).ToUnixTimeSeconds();
        var givenName = Disclosure("given_name", "Ada");
        var familyName = Disclosure("family_name", "Lovelace");
        // The digests must appear in the body's _sd array — that is what makes these
        // valid (resolvable) disclosures rather than orphan segments a real SD-JWT
        // reader would ignore.
        var jwt = "eyJhbGciOiJFUzI1NiJ9." +
                  Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
                  {
                      exp,
                      _sd = new[] { Digest(givenName), Digest(familyName) }
                  })) + ".sig";
        var raw = jwt + "~" + givenName + "~" + familyName + "~";
        return new CachedCredential
        {
            Id = id,
            Vct = "AssuredIdentityCredential",
            RawSdJwt = raw,
            AvailableClaimNames = new List<string> { "given_name", "family_name" },
            IssuerDid = "did:sorcha:org:WS11QRNVabcdefghijHGNRK",
            DisplayLabel = "Assured Identity",
        };
    }

    [Fact]
    public void RendersDecodedClaims_AndDetailsExpander_NoLibsodiumCopy()
    {
        var id = Guid.NewGuid();
        _cache.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedCredential> { CredWithClaims(id) });

        var cut = Render(PageWithProvider(id));

        // Decoded claim values are shown (name → value), not just claim names.
        var claims = cut.Find("[data-testid=credential-detail-claims]").TextContent;
        claims.Should().Contain("Ada");
        claims.Should().Contain("Lovelace");

        // Machine identifiers live behind the Details expander.
        cut.FindAll("[data-testid=credential-detail-details]").Should().ContainSingle();

        // Plain-language status leads.
        cut.Find("[data-testid=credential-detail-status]").TextContent.Should().Contain("Valid");

        // The stale "libsodium bridge" hedge is gone.
        cut.Markup.Should().NotContain("libsodium");
    }

    [Fact]
    public void MissingCredential_ShowsNotFound()
    {
        _cache.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedCredential>());

        var cut = Render(PageWithProvider(Guid.NewGuid()));

        cut.Markup.Should().Contain("Credential not found");
    }
}
