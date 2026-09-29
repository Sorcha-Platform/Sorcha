// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

using Sorcha.Blueprint.Engine.Credentials;

using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// #1759 — Sorcha SD-JWTs now carry both status shapes, so the Blueprint Service's trust evaluator
/// must route each reference to the checker that reads it. Resolved from the REAL Program graph: a
/// registration that still wires the W3C checker alone compiles, passes every unit test, and refuses
/// every good credential at the first internal gate.
/// </summary>
public sealed class StatusCheckerCompositionTests(BlueprintServiceWebApplicationFactory factory)
    : IClassFixture<BlueprintServiceWebApplicationFactory>
{
    [Fact]
    public void Program_RoutesStatusReferencesByKind()
    {
        using var scope = factory.Services.CreateScope();

        scope.ServiceProvider.GetRequiredService<IStatusListChecker>()
            .Should().BeOfType<RoutingStatusListChecker>();
        scope.ServiceProvider.GetRequiredService<IetfTokenStatusListChecker>()
            .Should().NotBeNull("the IETF reader and its issuer-resolved verifier must both be resolvable");
    }
}
