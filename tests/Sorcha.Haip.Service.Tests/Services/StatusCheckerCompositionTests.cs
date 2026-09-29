// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Haip.Service.Services;

using Xunit;

using IStatusListChecker = Sorcha.Blueprint.Engine.Credentials.IStatusListChecker;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// #1768 — resolves HAIP's status checker from the REAL <c>Program</c> composition (any type in the entry
/// assembly selects it; <c>Program</c> itself is ambiguous here because Tenant's is referenced too). The checker now
/// needs the engine's <c>StatusListTokenVerifier</c> and a DID-backed issuer key resolver; a
/// registration missed there compiles, passes every unit test (each builds its own checker), and
/// throws only on the first live presentation that carries a status reference.
/// </summary>
public sealed class StatusCheckerCompositionTests
{
    [Fact]
    public void Program_ResolvesTheIssuerPinnedIetfStatusChecker()
    {
        using var factory = new WebApplicationFactory<HaipPresentationVerifier>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.UseSetting("ConnectionStrings:redis", "localhost:6379,abortConnect=false");
        });

        using var scope = factory.Services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IStatusListChecker>();

        checker.Should().BeOfType<IetfTokenStatusListChecker>();
    }
}
