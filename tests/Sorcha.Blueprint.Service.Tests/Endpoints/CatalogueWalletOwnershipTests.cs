// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using FluentAssertions;
using Sorcha.Blueprint.Service.Tests.Integration;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// #1213 — the blueprint catalogue endpoints (<c>GET /api/actions/{wallet}/{register}/blueprints</c>
/// and its <c>/{blueprintId}</c> sibling) took the wallet from the route and never checked the caller
/// owned it. The group's only gate is <c>CanExecuteBlueprints</c> = "any authenticated user", so any
/// authenticated caller could query as any wallet. The caller must now own the wallet in the path.
///
/// The ownership gate runs before the store lookup, so these tests need no published blueprint: an
/// owned wallet passes the gate (200, possibly empty), an unowned wallet is refused (403) before any
/// data is touched — which also means a non-owner cannot use the 200-vs-404 distinction to probe.
/// </summary>
public class CatalogueWalletOwnershipTests : IClassFixture<BlueprintServiceWebApplicationFactory>
{
    private readonly HttpClient _client;

    public CatalogueWalletOwnershipTests(BlueprintServiceWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private const string Owned = BlueprintServiceWebApplicationFactory.TestOwnedWalletAddress;
    private const string NotOwned = "ws1qsomeoneelse0000000000000000000000000000";

    [Fact]
    public async Task ListBlueprints_WalletNotOwnedByCaller_Returns403()
    {
        var response = await _client.GetAsync($"/api/actions/{NotOwned}/registers-x/blueprints");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListBlueprints_WalletOwnedByCaller_IsNotForbidden()
    {
        var response = await _client.GetAsync($"/api/actions/{Owned}/registers-x/blueprints");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BlueprintDetail_WalletNotOwnedByCaller_Returns403()
    {
        var response = await _client.GetAsync($"/api/actions/{NotOwned}/registers-x/blueprints/some-bp");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BlueprintDetail_WalletOwnedByCaller_PassesGate_ThenNotFound()
    {
        // Owned → past the gate; the unknown blueprint then legitimately 404s. The point is that a
        // 404 (not a 403) proves the gate let an owner through — and that a non-owner never reaches it.
        var response = await _client.GetAsync($"/api/actions/{Owned}/registers-x/blueprints/unknown-bp");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
