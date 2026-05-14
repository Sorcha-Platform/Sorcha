// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Tenant.Models.Persona;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Tests for <see cref="InMemoryPerContextPersonaCache"/> (Feature 125, T029).
/// Verifies per-context isolation, replacement semantics, and the
/// flush-on-sign-out hook (<see cref="IPerContextPersonaCache.ClearAllAsync"/>).
/// </summary>
public sealed class PerContextPersonaCacheTests
{
    private static PersonaReadModelV1 Persona(string givenName) =>
        new()
        {
            GivenName = new PersonaAttribute<string>(givenName, PersonaAttributeSource.SelfAsserted, null, DateTimeOffset.UtcNow)
        };

    [Fact]
    public async Task GetAsync_UnknownContext_ReturnsNull()
    {
        var cache = new InMemoryPerContextPersonaCache();
        var read = await cache.GetAsync(Guid.NewGuid());
        read.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsPersonal()
    {
        var cache = new InMemoryPerContextPersonaCache();
        var sarah = Persona("Sarah");

        await cache.SetAsync(contextOrgId: null, sarah);

        var read = await cache.GetAsync(null);
        read.Should().NotBeNull();
        read!.GivenName!.Value.Should().Be("Sarah");
    }

    [Fact]
    public async Task DifferentContexts_AreIsolated()
    {
        var cache = new InMemoryPerContextPersonaCache();
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        await cache.SetAsync(null, Persona("Sarah-personal"));
        await cache.SetAsync(orgA, Persona("Sarah-orgA"));
        await cache.SetAsync(orgB, Persona("Sarah-orgB"));

        (await cache.GetAsync(null))!.GivenName!.Value.Should().Be("Sarah-personal");
        (await cache.GetAsync(orgA))!.GivenName!.Value.Should().Be("Sarah-orgA");
        (await cache.GetAsync(orgB))!.GivenName!.Value.Should().Be("Sarah-orgB");
    }

    [Fact]
    public async Task SetAsync_Twice_SameContext_OverwritesValue()
    {
        var cache = new InMemoryPerContextPersonaCache();
        var orgId = Guid.NewGuid();

        await cache.SetAsync(orgId, Persona("first"));
        await cache.SetAsync(orgId, Persona("second"));

        (await cache.GetAsync(orgId))!.GivenName!.Value.Should().Be("second");
    }

    [Fact]
    public async Task RemoveAsync_OnlyDropsTargetContext()
    {
        var cache = new InMemoryPerContextPersonaCache();
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        await cache.SetAsync(orgA, Persona("Sarah-orgA"));
        await cache.SetAsync(orgB, Persona("Sarah-orgB"));

        await cache.RemoveAsync(orgA);

        (await cache.GetAsync(orgA)).Should().BeNull();
        (await cache.GetAsync(orgB)).Should().NotBeNull("RemoveAsync must not affect other contexts.");
    }

    [Fact]
    public async Task ClearAllAsync_WipesEveryContext()
    {
        var cache = new InMemoryPerContextPersonaCache();
        await cache.SetAsync(null, Persona("personal"));
        await cache.SetAsync(Guid.NewGuid(), Persona("orgA"));
        await cache.SetAsync(Guid.NewGuid(), Persona("orgB"));

        await cache.ClearAllAsync();

        (await cache.GetAsync(null)).Should().BeNull("sign-out flush wipes every cached persona.");
    }

    [Fact]
    public async Task SetAsync_NullPersona_Throws()
    {
        var cache = new InMemoryPerContextPersonaCache();
        var act = async () => await cache.SetAsync(null, null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
