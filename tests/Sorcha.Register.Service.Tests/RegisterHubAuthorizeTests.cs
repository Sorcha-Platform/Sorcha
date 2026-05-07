// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Sorcha.Register.Service.Tests;

/// <summary>
/// Feature 118 T080 — guards the RegisterHub <c>[Authorize]</c> cutover.
/// Once T091 lands, unauthenticated SignalR negotiate against
/// <c>/hubs/register</c> must be rejected with HTTP 401.
/// </summary>
/// <remarks>
/// <para>
/// Currently <c>[Fact(Skip = "awaiting cutover")]</c> per the spec — the
/// hub remains permissive while the <c>sorcha_signalr_connections_total{authenticated=...}</c>
/// counter (T090) tracks rollout. Un-skip in T091 alongside adding
/// <c>[Authorize]</c> to <c>RegisterHub</c> and removing the permissive
/// code path.
/// </para>
/// <para>
/// The matching counter, the existing test
/// <c>RegisterHubAuthCounterTests</c>, and the UI's token-passing change
/// (T089 / PR #543) are all already in tree — this scaffolding is the
/// last piece to flip the auth invariant.
/// </para>
/// </remarks>
public class RegisterHubAuthorizeTests
{
    [Fact(Skip = "awaiting cutover (T091 / second-release)")]
    public async Task UnauthenticatedConnection_IsRejectedWith401()
    {
        // Arrange — connect without an access token.
        var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/register")
            .Build();

        // Act
        var act = async () => await connection.StartAsync();

        // Assert — SignalR negotiate returns 401 once [Authorize] is in place.
        var ex = await act.Should().ThrowAsync<Exception>();
        ex.Which.Message.Should().Contain("401");
    }
}
