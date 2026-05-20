// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Sorcha.ServiceClients.CitizenWallet;
using Sorcha.Wallet.Pwa.Services;
using Xunit;
using Wire = Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Feature 114 / US5 PR3 — coverage for the Activity merge rule
/// (<see cref="PresentationActivityMerge"/>, design §5) and the per-row
/// server-authoritative delete (<see cref="PresentationActivityActions"/>).
/// </summary>
public class ActivityMergeTests
{
    private static Wire.PresentationLogEntry ServerEntry(
        Guid? id = null,
        DateTimeOffset? at = null,
        string verifier = "Strathcarron Council",
        Wire.PresentationLogOutcome outcome = Wire.PresentationLogOutcome.Presented)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            CredentialId = Guid.NewGuid(),
            VerifierLabel = verifier,
            DisclosedClaims = ["givenName", "familyName"],
            PresentedAt = at ?? DateTimeOffset.UtcNow,
            Outcome = outcome
        };

    private static PresentationLogEntry LocalEntry(
        Guid? id = null,
        DateTimeOffset? at = null,
        bool synced = false,
        string credentialType = "AssuredIdentityCredential/v1",
        string? credentialLabel = "Assured Identity")
        => new(
            Id: id ?? Guid.NewGuid(),
            PresentedAt: at ?? DateTimeOffset.UtcNow,
            CredentialType: credentialType,
            CredentialLabel: credentialLabel,
            VerifierLabel: "Strathcarron Council",
            DisclosedClaims: ["givenName"],
            Outcome: PresentationLogOutcome.Sent,
            CredentialId: Guid.NewGuid(),
            SyncedToServer: synced);

    // ---- T013 / US1: server entries appear ----

    [Fact]
    public void Build_ServerEntries_AppearInDisplay()
    {
        var server = ServerEntry();
        var merged = PresentationActivityMerge.Build([server], []);

        merged.Should().ContainSingle(i => i.Id == server.Id);
        merged[0].Subtitle.Should().Contain("Strathcarron Council").And.Contain("2 claims").And.Contain("Sent");
    }

    [Fact]
    public void Build_NewestFirst()
    {
        var older = ServerEntry(at: DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = ServerEntry(at: DateTimeOffset.UtcNow);

        var merged = PresentationActivityMerge.Build([older, newer], []);

        merged.Select(i => i.Id).Should().ContainInOrder(newer.Id, older.Id);
    }

    // ---- T022 / US3: merge-rule edge cases ----

    [Fact]
    public void Build_JustMadeUnsyncedLocal_ShowsImmediately()
    {
        var local = LocalEntry(synced: false);

        var merged = PresentationActivityMerge.Build([], [local]);

        merged.Should().ContainSingle(i => i.Id == local.Id);
        merged[0].Title.Should().Be("Presented Assured Identity");
    }

    [Fact]
    public void Build_AfterSync_AppearsExactlyOnce_SyncedLocalSuppressed()
    {
        var id = Guid.NewGuid();
        var at = DateTimeOffset.UtcNow;
        var server = ServerEntry(id: id, at: at);
        var syncedLocal = LocalEntry(id: id, at: at, synced: true);

        var merged = PresentationActivityMerge.Build([server], [syncedLocal]);

        merged.Should().ContainSingle(i => i.Id == id);
        // Enriched with the local credential label on the originating device.
        merged.Single(i => i.Id == id).Title.Should().Be("Presented Assured Identity");
    }

    [Fact]
    public void Build_ServerAbsentButSyncedLocalLingers_DoesNotResurrect()
    {
        // The entry was deleted server-side on another device; this device still
        // holds a synced local copy. It must NOT be displayed.
        var lingering = LocalEntry(synced: true);

        var merged = PresentationActivityMerge.Build([], [lingering]);

        merged.Should().BeEmpty();
    }

    [Fact]
    public void Build_GenericTitle_WhenNoLocalMatch()
    {
        // A server entry with no local counterpart (e.g. freshly-paired device B).
        var server = ServerEntry();

        var merged = PresentationActivityMerge.Build([server], []);

        merged[0].Title.Should().Be("Presented a credential");
    }

    // ---- T021 / US2: per-row delete invokes both server + local ----

    [Fact]
    public async Task DeleteEverywhere_InvokesServerThenLocal()
    {
        var id = Guid.NewGuid();
        var client = new Mock<ICitizenWalletClient>();
        var log = new InMemoryPresentationLog();
        await log.AppendAsync(LocalEntry(id: id));

        await PresentationActivityActions.DeleteEverywhereAsync(client.Object, log, id);

        client.Verify(c => c.DeletePresentationAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        (await log.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteEverywhere_ServerFailure_StillRemovesLocal()
    {
        var id = Guid.NewGuid();
        var client = new Mock<ICitizenWalletClient>();
        client.Setup(c => c.DeletePresentationAsync(id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var log = new InMemoryPresentationLog();
        await log.AppendAsync(LocalEntry(id: id));

        await PresentationActivityActions.DeleteEverywhereAsync(client.Object, log, id);

        (await log.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public void RemoveConfirmCopy_IsReframedForCrossDevice()
    {
        PresentationActivityActions.RemoveConfirmBody.Should()
            .Contain("all your devices")
            .And.Contain("verifier's own records");
    }
}
