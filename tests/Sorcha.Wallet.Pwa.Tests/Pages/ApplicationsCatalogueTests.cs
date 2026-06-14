// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Services.Catalogue;
using Xunit;
using CataloguePage = Sorcha.Wallet.Pwa.Pages.Applications;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// Feature 154 (B) — the catalogue page lists startable services, handles empty/error, and starts a
/// service (create instance → navigate into the existing fill/submit flow).
/// </summary>
public sealed class ApplicationsCatalogueTests : ComponentTestFixture
{
    private readonly Mock<ICatalogueClient> _catalogue = new();

    public ApplicationsCatalogueTests() => Services.AddSingleton(_catalogue.Object);

    private static CatalogueItem Svc(string id, string title) => new(id, title, "desc", "reg-1");

    [Fact]
    public void ListsServices()
    {
        _catalogue.Setup(c => c.GetServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogueItem> { Svc("a", "Blue Badge"), Svc("b", "Bus Pass") });

        var cut = Render<CataloguePage>();

        cut.FindAll("[data-testid^=catalogue-item-]").Should().HaveCount(2);
    }

    [Fact]
    public void EmptyCatalogue_ShowsEmptyState()
    {
        _catalogue.Setup(c => c.GetServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CatalogueItem>());

        var cut = Render<CataloguePage>();

        cut.FindAll("[data-testid=catalogue-empty]").Should().ContainSingle();
    }

    [Fact]
    public void LoadFailure_ShowsNotice()
    {
        _catalogue.Setup(c => c.GetServicesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("offline"));

        var cut = Render<CataloguePage>();

        cut.FindAll("[data-testid=catalogue-error]").Should().ContainSingle();
    }

    [Fact]
    public void TapService_StartsAndNavigates()
    {
        _catalogue.Setup(c => c.GetServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogueItem> { Svc("a", "Blue Badge") });
        _catalogue.Setup(c => c.StartAsync(It.IsAny<CatalogueItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("11111111-1111-1111-1111-111111111111");
        var nav = Services.GetRequiredService<NavigationManager>();

        var cut = Render<CataloguePage>();
        cut.Find("[data-testid=catalogue-item-a]").Click();

        _catalogue.Verify(c => c.StartAsync(It.IsAny<CatalogueItem>(), It.IsAny<CancellationToken>()), Times.Once);
        nav.Uri.Should().EndWith("applications/11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void StartFailure_ShowsError_NoNavigation()
    {
        _catalogue.Setup(c => c.GetServicesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogueItem> { Svc("a", "Blue Badge") });
        _catalogue.Setup(c => c.StartAsync(It.IsAny<CatalogueItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var cut = Render<CataloguePage>();
        cut.Find("[data-testid=catalogue-item-a]").Click();

        cut.FindAll("[data-testid=catalogue-start-error]").Should().ContainSingle();
    }

    [Theory]
    [InlineData("badge", 1)]
    [InlineData("pass", 1)]
    [InlineData("", 2)]
    [InlineData("zzz", 0)]
    public void FilterServices_NarrowsByNameOrDescription(string query, int expected)
    {
        var all = new List<CatalogueItem> { new("a", "Blue Badge", "mobility", "r"), new("b", "Bus Pass", "travel", "r") };
        CataloguePage.FilterServices(all, query).Count.Should().Be(expected);
    }
}
