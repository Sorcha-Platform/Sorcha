// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Citizen.Wallet.Services;
using Sorcha.ServiceClients.CitizenWallet;
using Xunit;

namespace Sorcha.Citizen.Wallet.Tests.Services;

/// <summary>
/// Tests for the PWA <see cref="SyncService"/> (Feature 114, T108). Covers the
/// merge logic, cursor persistence, and 410 → full-snapshot recovery path.
/// IndexedDB is replaced by <see cref="InMemorySyncCursorStore"/>; HttpClient
/// is replaced by a Moq <see cref="ICitizenWalletClient"/>.
/// </summary>
public sealed class SyncServiceTests
{
    private readonly Mock<ICitizenWalletClient> _client = new();
    private readonly InMemoryCredentialCache _cache = new();
    private readonly InMemoryDelegationStore _delegations = new();
    private readonly InMemorySyncCursorStore _cursors = new();
    private readonly SyncService _sut;

    public SyncServiceTests()
    {
        _sut = new SyncService(_client.Object, _cache, _delegations, _cursors,
            NullLogger<SyncService>.Instance);
    }

    [Fact]
    public async Task SyncAsync_FirstSync_AppliesAddedAndPersistsCursor()
    {
        var added = NewPayload();
        _client.Setup(c => c.SyncAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResponse
            {
                SyncToken = "cursor-1",
                Credentials = new SyncCredentialChanges { Added = new[] { added } },
            });

        var outcome = await _sut.SyncAsync();

        outcome.Mode.Should().Be(SyncMode.Delta);
        outcome.Added.Should().Be(1);
        (await _cache.ListAsync()).Should().ContainSingle(c => c.RawSdJwt == added.Jwt);
        (await _cursors.GetAsync()).Should().Be("cursor-1");
    }

    [Fact]
    public async Task SyncAsync_SubsequentSync_SendsStoredCursor()
    {
        await _cursors.SetAsync("cursor-7");
        _client.Setup(c => c.SyncAsync("cursor-7", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResponse { SyncToken = "cursor-8" });

        await _sut.SyncAsync();

        _client.Verify(c => c.SyncAsync("cursor-7", It.IsAny<CancellationToken>()), Times.Once);
        (await _cursors.GetAsync()).Should().Be("cursor-8");
    }

    [Fact]
    public async Task SyncAsync_ServerReturns410_FallsBackToFullSnapshotAndRebootstrapsCursor()
    {
        await _cursors.SetAsync("stale-cursor");
        _client.Setup(c => c.SyncAsync("stale-cursor", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncResponse?)null);

        var snap = NewPayload();
        _client.Setup(c => c.ListCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CredentialListResponse { Credentials = new[] { snap } });
        _client.Setup(c => c.SyncAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResponse { SyncToken = "fresh-cursor" });

        var outcome = await _sut.SyncAsync();

        outcome.Mode.Should().Be(SyncMode.FullSnapshot);
        outcome.Added.Should().Be(1);
        (await _cache.ListAsync()).Should().ContainSingle(c => c.RawSdJwt == snap.Jwt);
        (await _cursors.GetAsync()).Should().Be("fresh-cursor",
            "the recovery path must obtain a fresh cursor so the next /sync uses it");
    }

    [Fact]
    public async Task SyncAsync_DelegationRenewedInResponse_UpdatesDelegationStore()
    {
        _client.Setup(c => c.SyncAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResponse
            {
                SyncToken = "c1",
                Delegation = new SyncDelegationUpdate
                {
                    Renewed = true,
                    Jwt = "eyJ.fresh.delegation",
                    ExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
                },
            });

        await _sut.SyncAsync();

        (await _delegations.GetCurrentAsync()).Should().Be("eyJ.fresh.delegation");
    }

    [Fact]
    public async Task SyncAsync_StatusListsToRefresh_FlowedThroughOutcome()
    {
        _client.Setup(c => c.SyncAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResponse
            {
                SyncToken = "c1",
                StatusListsToRefresh = new[] { "https://verify.test/status/A.statuslist+jwt" },
            });

        var outcome = await _sut.SyncAsync();

        outcome.StatusListsToRefresh.Should().ContainSingle(u => u.EndsWith("A.statuslist+jwt"));
    }

    private static CachedCredentialPayload NewPayload() => new()
    {
        Id = $"urn:credential:test:{Guid.NewGuid():N}",
        Vct = "https://sorcha.dev/vc/test/v1",
        Jwt = "eyJ.demo.sd-jwt~disclosure~",
        IssuerDid = "did:sorcha:org:test",
        IssuedAt = DateTimeOffset.UtcNow,
    };
}
