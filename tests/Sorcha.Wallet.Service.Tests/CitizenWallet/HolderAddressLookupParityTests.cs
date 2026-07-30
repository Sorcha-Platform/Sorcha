// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Sorcha.Wallet.Service.Tests.Services;

namespace Sorcha.Wallet.Service.Tests.CitizenWallet;

/// <summary>
/// Runs one set of behavioural assertions against BOTH <see cref="IHolderAddressLookup"/>
/// implementations, so the in-memory backend cannot quietly diverge from the EF Core one.
/// </summary>
/// <remarks>
/// <see cref="EfCoreHolderAddressLookup"/> was the interface's only implementation and it was
/// registered unconditionally, so a host without a Postgres connection string could not activate it —
/// every endpoint touching the lookup returned 500 and
/// <c>Sorcha.Wallet.Service.IntegrationTests</c> sat at 5/33. That suite is the service's only
/// HTTP-level authorization coverage, so its absence is a large part of why the wallet-ownership
/// holes fixed in #1340 survived review.
/// <para>
/// Adding <see cref="InMemoryHolderAddressLookup"/> revives the suite, but only if the two agree —
/// an in-memory stand-in that behaves differently turns 33 green integration tests into 33 tests
/// that prove nothing about production. Both sides are individually plausible and nothing else
/// verifies the join, so this suite verifies it directly: each case is asserted against both.
/// </para>
/// <para>
/// <see cref="HolderAddressLookupTests"/> keeps the EF-Core-specific assertions that have no
/// in-memory analogue (row shape, <c>CreatedAt</c> stamping).
/// </para>
/// </remarks>
public class HolderAddressLookupParityTests
{
    /// <summary>Both implementations, each built fresh per case.</summary>
    public static TheoryData<string> Implementations => new() { "efcore", "inmemory" };

    private static IHolderAddressLookup Create(string implementation, string testName)
    {
        if (implementation == "inmemory")
        {
            return new InMemoryHolderAddressLookup(NullLogger<InMemoryHolderAddressLookup>.Instance);
        }

        var options = new DbContextOptionsBuilder<TestCitizenWalletDbContext>()
            .UseInMemoryDatabase($"holder-parity-{testName}-{Guid.NewGuid():N}")
            .Options;
        return new EfCoreHolderAddressLookup(
            new TestCitizenWalletDbContext(options), NullLogger<EfCoreHolderAddressLookup>.Instance);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Resolve_UnknownAddress_ReturnsNull(string implementation)
    {
        var lookup = Create(implementation, nameof(Resolve_UnknownAddress_ReturnsNull));

        (await lookup.ResolvePlatformUserIdAsync("ws1qorg-not-a-citizen")).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Register_ThenResolve_RoundTrips(string implementation)
    {
        var lookup = Create(implementation, nameof(Register_ThenResolve_RoundTrips));
        var pid = Guid.NewGuid();

        await lookup.RegisterAsync("ws1qcitizen", pid);

        (await lookup.ResolvePlatformUserIdAsync("ws1qcitizen")).Should().Be(pid);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Register_SamePlatformUserTwice_IsIdempotent(string implementation)
    {
        var lookup = Create(implementation, nameof(Register_SamePlatformUserTwice_IsIdempotent));
        var pid = Guid.NewGuid();

        await lookup.RegisterAsync("ws1qcitizen", pid);
        await lookup.RegisterAsync("ws1qcitizen", pid);

        (await lookup.ResolvePlatformUserIdAsync("ws1qcitizen")).Should().Be(pid);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Register_ConflictingPlatformUser_KeepsTheFirst(string implementation)
    {
        // An address can never legitimately remap to a new PlatformUser (slot-108 derivation is
        // deterministic per citizen), so a conflicting write is a bug somewhere upstream. Both
        // backends must refuse to overwrite — an in-memory last-write-wins would silently hand one
        // citizen's inbound credentials to another under test.
        var lookup = Create(implementation, nameof(Register_ConflictingPlatformUser_KeepsTheFirst));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await lookup.RegisterAsync("ws1qcitizen", first);
        await lookup.RegisterAsync("ws1qcitizen", second);

        (await lookup.ResolvePlatformUserIdAsync("ws1qcitizen")).Should().Be(first);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Resolve_IsCaseSensitive(string implementation)
    {
        // The EF Core backend keys on a Postgres `text` primary key, which is case-sensitive. An
        // in-memory map using OrdinalIgnoreCase would resolve addresses the real backend misses.
        var lookup = Create(implementation, nameof(Resolve_IsCaseSensitive));
        await lookup.RegisterAsync("ws1qcitizen", Guid.NewGuid());

        (await lookup.ResolvePlatformUserIdAsync("WS1QCITIZEN")).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task DistinctAddresses_DoNotCollide(string implementation)
    {
        var lookup = Create(implementation, nameof(DistinctAddresses_DoNotCollide));
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await lookup.RegisterAsync("ws1qcitizen-a", a);
        await lookup.RegisterAsync("ws1qcitizen-b", b);

        (await lookup.ResolvePlatformUserIdAsync("ws1qcitizen-a")).Should().Be(a);
        (await lookup.ResolvePlatformUserIdAsync("ws1qcitizen-b")).Should().Be(b);
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Resolve_BlankAddress_Throws(string implementation)
    {
        var lookup = Create(implementation, nameof(Resolve_BlankAddress_Throws));

        await FluentActions.Awaiting(() => lookup.ResolvePlatformUserIdAsync("  "))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [MemberData(nameof(Implementations))]
    public async Task Register_BlankAddress_Throws(string implementation)
    {
        var lookup = Create(implementation, nameof(Register_BlankAddress_Throws));

        await FluentActions.Awaiting(() => lookup.RegisterAsync("  ", Guid.NewGuid()))
            .Should().ThrowAsync<ArgumentException>();
    }
}
