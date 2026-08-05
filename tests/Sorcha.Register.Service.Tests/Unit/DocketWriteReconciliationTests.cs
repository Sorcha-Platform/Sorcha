// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Services;
using Xunit;

namespace Sorcha.Register.Service.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="DocketWriteReconciliation"/> — the decision that turns a
/// duplicate-key collision on the docket-write endpoint into either a benign idempotent retry or
/// a loud integrity divergence (issue #814). Masking a divergence as success silently dropped a
/// docket's transactions, so the matching/divergence boundary is verified here directly.
/// </summary>
public class DocketWriteReconciliationTests
{
    private static DocketHeader DocketWithHash(string hash, ulong number = 7) => new()
    {
        Id = number,
        RegisterId = "reg-1",
        Hash = hash,
    };

    private static TransactionModel TxWithSignature(string signature, string txId = "tx-1") => new()
    {
        TxId = txId,
        RegisterId = "reg-1",
        Signature = signature,
    };

    // ---- DocketHeader reconciliation ----

    [Fact]
    public void ReconcileDocket_SameHash_ReturnsIdempotentMatch()
    {
        var existing = DocketWithHash("abc123");
        var incoming = DocketWithHash("abc123");

        DocketWriteReconciliation.ReconcileDocket(existing, incoming)
            .Should().Be(DocketWriteReconciliation.Verdict.IdempotentMatch);
    }

    [Fact]
    public void ReconcileDocket_DifferentHash_ReturnsDivergence()
    {
        var existing = DocketWithHash("abc123");
        var incoming = DocketWithHash("deadbeef");

        DocketWriteReconciliation.ReconcileDocket(existing, incoming)
            .Should().Be(DocketWriteReconciliation.Verdict.Divergence);
    }

    [Fact]
    public void ReconcileDocket_NoExistingRecord_ReturnsDivergence()
    {
        // A duplicate-key error with no readable existing record is not a verified match —
        // fail loud rather than assume success.
        DocketWriteReconciliation.ReconcileDocket(null, DocketWithHash("abc123"))
            .Should().Be(DocketWriteReconciliation.Verdict.Divergence);
    }

    [Fact]
    public void ReconcileDocket_ExistingHasEmptyHash_ReturnsDivergence()
    {
        var existing = DocketWithHash(string.Empty);
        var incoming = DocketWithHash("abc123");

        DocketWriteReconciliation.ReconcileDocket(existing, incoming)
            .Should().Be(DocketWriteReconciliation.Verdict.Divergence);
    }

    [Fact]
    public void ReconcileDocket_HashComparisonIsCaseSensitive()
    {
        var existing = DocketWithHash("ABC123");
        var incoming = DocketWithHash("abc123");

        DocketWriteReconciliation.ReconcileDocket(existing, incoming)
            .Should().Be(DocketWriteReconciliation.Verdict.Divergence);
    }

    // ---- Transaction reconciliation ----

    [Fact]
    public void ReconcileTransaction_SameSignature_ReturnsIdempotentMatch()
    {
        var existing = TxWithSignature("sig-aaa");
        var incoming = TxWithSignature("sig-aaa");

        DocketWriteReconciliation.ReconcileTransaction(existing, incoming)
            .Should().Be(DocketWriteReconciliation.Verdict.IdempotentMatch);
    }

    [Fact]
    public void ReconcileTransaction_DifferentSignature_ReturnsDivergence()
    {
        // Same TxId, different signature — the exact silent-loss case from #814.
        var existing = TxWithSignature("sig-aaa");
        var incoming = TxWithSignature("sig-bbb");

        DocketWriteReconciliation.ReconcileTransaction(existing, incoming)
            .Should().Be(DocketWriteReconciliation.Verdict.Divergence);
    }

    [Fact]
    public void ReconcileTransaction_NoExistingRecord_ReturnsDivergence()
    {
        DocketWriteReconciliation.ReconcileTransaction(null, TxWithSignature("sig-aaa"))
            .Should().Be(DocketWriteReconciliation.Verdict.Divergence);
    }

    [Fact]
    public void ReconcileTransaction_ExistingHasEmptySignature_ReturnsDivergence()
    {
        var existing = TxWithSignature(string.Empty);
        var incoming = TxWithSignature("sig-aaa");

        DocketWriteReconciliation.ReconcileTransaction(existing, incoming)
            .Should().Be(DocketWriteReconciliation.Verdict.Divergence);
    }
}
