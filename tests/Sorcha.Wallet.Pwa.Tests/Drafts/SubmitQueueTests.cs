// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services.Drafts;
using Sorcha.Wallet.Pwa.Services.Drafts.Models;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Drafts;

/// <summary>
/// Feature 152 US3/US4 — `SubmitQueue` outbox: enqueue, drain (Submitted → removed, Retry → stays
/// with attempts++, stale → NeedsAttention + reason), per-item isolation, idempotency-key reuse.
/// </summary>
public sealed class SubmitQueueTests
{
    private readonly FakeEncryptedStore _store = new();
    private SubmitQueue Create() => new(_store);

    private static QueuedSubmission Item(string idem = "idem-1") => new()
    {
        InstanceId = "inst-1", ActionId = 1, BlueprintId = "bp-1", IdempotencyKey = idem,
    };

    [Fact]
    public async Task EnqueueAsync_AssignsKey_AndQueuedState()
    {
        var stored = await Create().EnqueueAsync(Item());

        stored.QueuedKey.Should().NotBeNullOrEmpty();
        stored.State.Should().Be(QueuedSubmissionState.Queued);
    }

    [Fact]
    public async Task Drain_Submitted_RemovesItem()
    {
        var q = Create();
        await q.EnqueueAsync(Item());

        await q.DrainAsync((_, _) => Task.FromResult(SubmitOutcome.Submitted));

        (await q.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Drain_Retry_LeavesQueued_AndIncrementsAttempts()
    {
        var q = Create();
        await q.EnqueueAsync(Item());

        await q.DrainAsync((_, _) => Task.FromResult(SubmitOutcome.Retry));

        var items = await q.ListAsync();
        items.Should().ContainSingle();
        items[0].State.Should().Be(QueuedSubmissionState.Queued);
        items[0].Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Drain_SubmitThrows_TreatedAsRetry()
    {
        var q = Create();
        await q.EnqueueAsync(Item());

        await q.DrainAsync((_, _) => throw new InvalidOperationException("network"));

        (await q.ListAsync())[0].State.Should().Be(QueuedSubmissionState.Queued);
    }

    [Theory]
    [InlineData(SubmitOutcome.AlreadySubmitted, ConflictReason.AlreadySubmitted)]
    [InlineData(SubmitOutcome.StepMovedOn, ConflictReason.StepMovedOn)]
    [InlineData(SubmitOutcome.InstanceClosed, ConflictReason.InstanceClosed)]
    public async Task Drain_Stale_MarksNeedsAttention_WithReason(SubmitOutcome outcome, ConflictReason reason)
    {
        var q = Create();
        await q.EnqueueAsync(Item());

        await q.DrainAsync((_, _) => Task.FromResult(outcome));

        var item = (await q.ListAsync()).Should().ContainSingle().Subject;
        item.State.Should().Be(QueuedSubmissionState.NeedsAttention);
        item.ConflictReason.Should().Be(reason);
    }

    [Fact]
    public async Task Drain_OneItemStale_OthersStillSubmit()
    {
        var q = Create();
        await q.EnqueueAsync(Item("a"));
        await q.EnqueueAsync(Item("b"));

        await q.DrainAsync((item, _) => Task.FromResult(
            item.IdempotencyKey == "a" ? SubmitOutcome.InstanceClosed : SubmitOutcome.Submitted));

        var remaining = await q.ListAsync();
        remaining.Should().ContainSingle();
        remaining[0].IdempotencyKey.Should().Be("a");
        remaining[0].State.Should().Be(QueuedSubmissionState.NeedsAttention);
    }

    [Fact]
    public async Task Drain_RetryThenList_PreservesIdempotencyKey()
    {
        var q = Create();
        await q.EnqueueAsync(Item("stable-key"));

        await q.DrainAsync((_, _) => Task.FromResult(SubmitOutcome.Retry));

        (await q.ListAsync())[0].IdempotencyKey.Should().Be("stable-key");
    }

    /// <summary>In-memory stand-in for the IndexedDB-backed encrypted store.</summary>
    private sealed class FakeEncryptedStore : IEncryptedObjectStore
    {
        private readonly Dictionary<string, object> _data = new();

        public Task PutAsync<T>(string storeName, string key, T value, CancellationToken ct = default)
        {
            _data[$"{storeName}:{key}"] = value!;
            return Task.CompletedTask;
        }

        public Task<T?> GetAsync<T>(string storeName, string key, CancellationToken ct = default) where T : class =>
            Task.FromResult(_data.TryGetValue($"{storeName}:{key}", out var v) ? (T?)v : null);

        public Task<IReadOnlyList<T>> ListAsync<T>(string storeName, CancellationToken ct = default) where T : class =>
            Task.FromResult((IReadOnlyList<T>)_data
                .Where(kv => kv.Key.StartsWith($"{storeName}:", StringComparison.Ordinal))
                .Select(kv => kv.Value).OfType<T>().ToList());

        public Task DeleteAsync(string storeName, string key, CancellationToken ct = default)
        {
            _data.Remove($"{storeName}:{key}");
            return Task.CompletedTask;
        }
    }
}
