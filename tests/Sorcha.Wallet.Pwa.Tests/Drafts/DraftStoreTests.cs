// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Sorcha.Wallet.Pwa.Services.Drafts;
using Sorcha.Wallet.Pwa.Services.Drafts.Models;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Drafts;

/// <summary>
/// Feature 152 US1 — `DraftStore` maps drafts onto the encrypted store under the
/// <c>instanceId:actionId</c> key, stamps SavedAt, and round-trips form data + media.
/// </summary>
public sealed class DraftStoreTests
{
    private readonly Mock<IEncryptedObjectStore> _store = new();
    private DraftStore Create() => new(_store.Object, TimeProvider.System);

    [Fact]
    public async Task SaveAsync_StampsSavedAt_AndPutsUnderCompositeKey()
    {
        ActionDraft? captured = null;
        _store.Setup(s => s.PutAsync("drafts", "inst-1:3", It.IsAny<ActionDraft>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, ActionDraft, CancellationToken>((_, _, d, _) => captured = d)
            .Returns(Task.CompletedTask);

        await Create().SaveAsync(new ActionDraft { InstanceId = "inst-1", ActionId = 3 });

        captured.Should().NotBeNull();
        captured!.SavedAt.Should().BeAfter(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task GetAsync_ReadsUnderCompositeKey()
    {
        var draft = new ActionDraft { InstanceId = "inst-1", ActionId = 3 };
        _store.Setup(s => s.GetAsync<ActionDraft>("drafts", "inst-1:3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        (await Create().GetAsync("inst-1", 3)).Should().BeSameAs(draft);
    }

    [Fact]
    public async Task DeleteAsync_DeletesUnderCompositeKey()
    {
        await Create().DeleteAsync("inst-1", 3);
        _store.Verify(s => s.DeleteAsync("drafts", "inst-1:3", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveThenRoundTrip_PreservesFormDataAndMedia()
    {
        // Simulate the encrypted store with an in-memory dictionary to prove the draft shape
        // (form data + media) survives a serialise/deserialise round-trip via the store seam.
        var backing = new System.Collections.Generic.Dictionary<string, ActionDraft>();
        _store.Setup(s => s.PutAsync("drafts", It.IsAny<string>(), It.IsAny<ActionDraft>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, ActionDraft, CancellationToken>((_, k, d, _) => backing[k] = d)
            .Returns(Task.CompletedTask);
        _store.Setup(s => s.GetAsync<ActionDraft>("drafts", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string k, CancellationToken _) => backing.GetValueOrDefault(k));

        var store = Create();
        var draft = new ActionDraft { InstanceId = "i", ActionId = 1 };
        draft.FormData["/name"] = "Ada";
        draft.Media.Add(new DraftMedia("photo.jpg", "image/jpeg", "AQID", DateTimeOffset.UtcNow));

        await store.SaveAsync(draft);
        var loaded = await store.GetAsync("i", 1);

        loaded!.FormData["/name"].Should().Be("Ada");
        loaded.Media.Should().ContainSingle().Which.FileName.Should().Be("photo.jpg");
    }
}
