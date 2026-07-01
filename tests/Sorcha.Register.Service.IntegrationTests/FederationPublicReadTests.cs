// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Register.Core.Managers;
using Sorcha.Register.Service.IntegrationTests.Fixtures;

namespace Sorcha.Register.Service.IntegrationTests;

/// <summary>
/// Feature 175 — anonymous public-register read (US1) and write guard rails (US3). A public
/// (advertised) register is readable with no credential; a private register and all writes are refused.
/// </summary>
[Collection("RegisterService")]
public class FederationPublicReadTests : IAsyncLifetime
{
    private readonly RegisterServiceWebApplicationFactory _factory;

    public FederationPublicReadTests(RegisterServiceWebApplicationFactory factory) => _factory = factory;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<string> SeedRegisterAsync(bool advertise)
    {
        var registerId = Guid.NewGuid().ToString("N");
        using var scope = _factory.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<RegisterManager>();
        await manager.CreateRegisterAsync(
            name: advertise ? "public-fed-register" : "private-fed-register",
            advertise: advertise,
            registerId: registerId);
        return registerId;
    }

    // === US1: anonymous read of a PUBLIC register ===

    [Fact]
    public async Task PublicRegister_ReadAnonymously_ReturnsOk()
    {
        var registerId = await SeedRegisterAsync(advertise: true);
        using var anon = _factory.CreateUnauthenticatedClient();

        var response = await anon.GetAsync($"/api/registers/{registerId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a public register is open data readable with no credential (FR-001)");
    }

    [Fact]
    public async Task PublicRegister_Dockets_ReadAnonymously_ReturnsOk()
    {
        var registerId = await SeedRegisterAsync(advertise: true);
        using var anon = _factory.CreateUnauthenticatedClient();

        var response = await anon.GetAsync($"/api/registers/{registerId}/dockets");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a puller replicates the public register's sealed docket chain anonymously");
    }

    // === US3: private data stays refused; the gate is strictly Advertise ===

    [Fact]
    public async Task PrivateRegister_ReadAnonymously_IsRefused()
    {
        var registerId = await SeedRegisterAsync(advertise: false);
        using var anon = _factory.CreateUnauthenticatedClient();

        var response = await anon.GetAsync($"/api/registers/{registerId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a non-advertised register is never exposed on the anonymous path (FR-004/FR-007, SC-004)");
    }

    [Fact]
    public async Task PrivateRegister_Dockets_ReadAnonymously_IsRefused()
    {
        var registerId = await SeedRegisterAsync(advertise: false);
        using var anon = _factory.CreateUnauthenticatedClient();

        var response = await anon.GetAsync($"/api/registers/{registerId}/dockets");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnknownRegister_ReadAnonymously_ReturnsNotFound()
    {
        using var anon = _factory.CreateUnauthenticatedClient();

        var response = await anon.GetAsync($"/api/registers/{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // === US3: no cross-installation write via this feature ===

    [Fact]
    public async Task WriteDocket_ToPublicRegister_Anonymously_IsRefused()
    {
        var registerId = await SeedRegisterAsync(advertise: true);
        using var anon = _factory.CreateUnauthenticatedClient();

        // A public register is readable but not writable: the docket write still requires the
        // CanWriteDockets service policy, so an anonymous/foreign caller cannot write (FR-004, SC-004).
        var response = await anon.PostAsync(
            $"/api/registers/{registerId}/dockets/", JsonContent());

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    private static System.Net.Http.HttpContent JsonContent() =>
        System.Net.Http.Json.JsonContent.Create(new { docketNumber = 0, docketHash = "x", transactionIds = Array.Empty<string>() });
}
