// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.ServiceClients.CitizenWallet;
using Xunit;

// Disambiguate the PWA-local presentation-log types from the wire-contract ones.
using LocalLogEntry = Sorcha.Wallet.Pwa.Services.PresentationLogEntry;
using LocalLogOutcome = Sorcha.Wallet.Pwa.Services.PresentationLogOutcome;
using WireLogOutcome = Sorcha.CitizenWallet.Abstractions.Models.PresentationLogOutcome;

namespace Sorcha.Wallet.Pwa.Tests.Services;

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
    private readonly InMemoryPresentationLog _presentationLog = new();
    private readonly SyncService _sut;

    public SyncServiceTests()
    {
        _sut = new SyncService(_client.Object, _cache, _delegations, _cursors,
            _presentationLog, NullLogger<SyncService>.Instance);
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

    // ---- US5 presentation-log drain -------------------------------------

    private void SetupBareSync() =>
        _client.Setup(c => c.SyncAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncResponse { SyncToken = "c1" });

    private static LocalLogEntry LocalEntry(
        Guid? credentialId = null,
        bool synced = false,
        LocalLogOutcome outcome = LocalLogOutcome.Sent,
        string verifierLabel = "Strathcarron Council") =>
        new(
            Id: Guid.NewGuid(),
            PresentedAt: DateTimeOffset.UtcNow,
            CredentialType: "AssuredIdentityCredential/v1",
            CredentialLabel: "Assured Identity",
            VerifierLabel: verifierLabel,
            DisclosedClaims: ["givenName"],
            Outcome: outcome,
            CredentialId: credentialId ?? Guid.NewGuid(),
            SyncedToServer: synced);

    [Fact]
    public async Task SyncAsync_PendingPresentationLog_PostedAndMarkedSynced()
    {
        SetupBareSync();
        var entry = LocalEntry();
        await _presentationLog.AppendAsync(entry);
        _client.Setup(c => c.ReportPresentationLogAsync(
                It.IsAny<PresentationLogReportRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await _sut.SyncAsync();

        _client.Verify(c => c.ReportPresentationLogAsync(
            It.Is<PresentationLogReportRequest>(r => r.Entries.Count == 1 && r.Entries[0].Id == entry.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        var stored = await _presentationLog.ListAsync();
        stored.Should().ContainSingle();
        stored[0].SyncedToServer.Should().BeTrue();
    }

    [Fact]
    public async Task SyncAsync_AlreadySyncedEntry_NotReposted()
    {
        SetupBareSync();
        await _presentationLog.AppendAsync(LocalEntry(synced: true));

        await _sut.SyncAsync();

        _client.Verify(c => c.ReportPresentationLogAsync(
            It.IsAny<PresentationLogReportRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_EntryWithoutCredentialId_Skipped()
    {
        SetupBareSync();
        await _presentationLog.AppendAsync(LocalEntry(credentialId: Guid.Empty));

        await _sut.SyncAsync();

        _client.Verify(c => c.ReportPresentationLogAsync(
            It.IsAny<PresentationLogReportRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_ReportThrows_LeavesEntryUnsynced()
    {
        SetupBareSync();
        await _presentationLog.AppendAsync(LocalEntry());
        _client.Setup(c => c.ReportPresentationLogAsync(
                It.IsAny<PresentationLogReportRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));

        // Drain failure must not surface from SyncAsync.
        var outcome = await _sut.SyncAsync();

        outcome.Should().NotBeNull();
        (await _presentationLog.ListAsync())[0].SyncedToServer.Should().BeFalse();
    }

    [Fact]
    public async Task SyncAsync_MapsSentToAcknowledgedAndRejectedToVerifierRejected()
    {
        SetupBareSync();
        await _presentationLog.AppendAsync(LocalEntry(outcome: LocalLogOutcome.Sent));
        await _presentationLog.AppendAsync(LocalEntry(outcome: LocalLogOutcome.Rejected));
        PresentationLogReportRequest? captured = null;
        _client.Setup(c => c.ReportPresentationLogAsync(
                It.IsAny<PresentationLogReportRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PresentationLogReportRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync(true);

        await _sut.SyncAsync();

        captured.Should().NotBeNull();
        captured!.Entries.Should().Contain(e => e.Outcome == WireLogOutcome.Acknowledged);
        captured.Entries.Should().Contain(e => e.Outcome == WireLogOutcome.VerifierRejected);
    }

    private static CachedCredentialPayload NewPayload() => new()
    {
        Id = $"urn:credential:test:{Guid.NewGuid():N}",
        Vct = "https://sorcha.dev/vc/test/v1",
        Jwt = "eyJ.demo.sd-jwt~disclosure~",
        IssuerDid = "did:sorcha:org:test",
        IssuedAt = DateTimeOffset.UtcNow,
    };

    // ---- Credential VCT decoupling (Task 4) — display label mapping ------

    [Fact]
    public void ToCachedCredential_PopulatesDisplayLabel_FromDisplayMetaCredentialName()
    {
        var payload = NewPayload() with
        {
            DisplayMeta = new JsonObject { ["credentialName"] = "Assured Identity" },
        };

        var cached = SyncService.ToCachedCredential(payload);

        cached.DisplayLabel.Should().Be("Assured Identity");
    }

    [Fact]
    public void ToCachedCredential_NoDisplayMeta_LeavesDisplayLabelNull()
    {
        var payload = NewPayload() with { DisplayMeta = null };

        var cached = SyncService.ToCachedCredential(payload);

        cached.DisplayLabel.Should().BeNull();
    }

    [Fact]
    public void ToCachedCredential_DisplayMetaWithoutCredentialName_LeavesDisplayLabelNull()
    {
        var payload = NewPayload() with
        {
            DisplayMeta = new JsonObject { ["issuerName"] = "Riverside Council" },
        };

        var cached = SyncService.ToCachedCredential(payload);

        cached.DisplayLabel.Should().BeNull();
    }

    [Fact]
    public void ToCachedCredential_PopulatesAvailableClaimNames_FromSdJwtDisclosures()
    {
        // Regression: ToCachedCredential used to hard-code AvailableClaimNames = [], which made
        // PresentationEngine.MatchCandidates reject every request that requires claims (every real
        // verifier preset), regardless of vct — surfacing as "None of your credentials match".
        var fullName = SdJwtDisclosure("salt1", "fullName", "Ada Lovelace");
        var portrait = SdJwtDisclosure("salt2", "portrait", "data:image/jpeg;base64,AAAA");
        var jwt = SdJwtBody(new
            {
                vct = "https://sorcha.dev/vc/assured-identity/v1",
                _sd = new[] { SdJwtDigest(fullName), SdJwtDigest(portrait) },
            })
            + "~" + fullName + "~" + portrait + "~";

        var cached = SyncService.ToCachedCredential(NewPayload() with { Jwt = jwt });

        cached.AvailableClaimNames.Should().Contain(new[] { "fullName", "portrait" },
            "the DCQL matcher checks required claims against AvailableClaimNames");
    }

    private static string SdJwtBody(object payload) =>
        "eyJhbGciOiJFUzI1NiJ9." +
        System.Buffers.Text.Base64Url.EncodeToString(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload)) +
        ".c2ln";

    private static string SdJwtDisclosure(string salt, string name, object value) =>
        System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(new object[] { salt, name, value })));

    private static string SdJwtDigest(string disclosure) =>
        System.Buffers.Text.Base64Url.EncodeToString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(disclosure)));
}
