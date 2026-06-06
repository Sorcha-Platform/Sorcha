// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Services;
using Xunit;
using CardsPage = Sorcha.Wallet.Pwa.Pages.Cards;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// bUnit tests for the Cards listing page — the dedicated "manage all my cards"
/// destination behind the floating-nav Cards tab.
/// </summary>
public sealed class CardsPageTests : ComponentTestFixture
{
    private readonly Mock<ICredentialCache> _cache = new();

    public CardsPageTests() => Services.AddSingleton(_cache.Object);

    private static CachedCredential Cred(string name) => new()
    {
        Id = Guid.NewGuid(),
        Vct = "AssuredIdentityCredential",
        RawSdJwt = "eyJ.body.sig~",
        AvailableClaimNames = new List<string>(),
        DisplayLabel = name,
    };

    [Fact]
    public void NoCredentials_ShowsEmptyStateWithAddCta()
    {
        _cache.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedCredential>());

        var cut = Render<CardsPage>();

        cut.FindAll("[data-testid=cards-empty]").Should().ContainSingle();
        cut.FindAll("[data-testid=cards-add-empty]").Should().ContainSingle();
        cut.FindAll("[data-testid=cards-list]").Should().BeEmpty();
    }

    [Fact]
    public void WithCredentials_RendersFullCardsPlusAddTile_AndCount()
    {
        _cache.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CachedCredential> { Cred("Treasury"), Cred("Operations") });

        var cut = Render<CardsPage>();

        cut.FindAll("[data-testid=cards-list]").Should().ContainSingle();
        cut.FindAll(".credential-wallet-card").Should().HaveCount(2);
        cut.FindAll("[data-testid=cards-add-tile]").Should().ContainSingle();
        cut.Find("[data-testid=cards-count]").TextContent.Trim().Should().Be("2");
    }
}
