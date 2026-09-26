// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using FluentAssertions;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// #1646. <c>GET /api/instances/</c> declared <c>page</c> and <c>pageSize</c> as REQUIRED minimal-API
/// query parameters, so a bare request 400'd in model binding, before the handler ran, with no
/// detail. The MCP <c>sorcha://instances</c> resource makes exactly that bare request, and reported
/// the 400 as "The Blueprint service is currently unavailable" on a healthy node. The issue guessed
/// the cause was a walletless caller; reproducing it live showed the bare request fails for a caller
/// who does hold wallets, and succeeds with ?page=1&amp;pageSize=20.
/// <para>
/// The reflection-invoked endpoint tests cannot see this: they skip the binder. Same class as #1433
/// (Tenant platform org list).
/// </para>
/// </summary>
public class InstanceListBindingTests : IClassFixture<BlueprintServiceWebApplicationFactory>
{
    private readonly HttpClient _client;

    public InstanceListBindingTests(BlueprintServiceWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task ListInstances_NoQueryString_ReturnsOk_NotABindingFailure()
    {
        var response = await _client.GetAsync("/api/instances/");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "page and pageSize must default rather than be required query parameters");
    }

    [Fact]
    public async Task ListInstances_ExplicitPaging_StillBinds()
    {
        var response = await _client.GetAsync("/api/instances/?page=2&pageSize=5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
