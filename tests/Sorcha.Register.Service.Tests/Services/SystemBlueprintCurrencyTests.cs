// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Services;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

public class SystemBlueprintCurrencyTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static TransactionModel Tx(string id, ulong? docket, DateTime ts) =>
        new() { TxId = id, DocketNumber = docket, TimeStamp = ts };

    private static Func<ulong, DocketHeader?> Dockets(params (ulong Id, string[] Txs)[] dockets)
    {
        var map = dockets.ToDictionary(d => d.Id, d => new DocketHeader { Id = d.Id, TransactionIds = d.Txs.ToList() });
        return n => map.GetValueOrDefault(n);
    }

    [Fact]
    public void Current_LaterSealedWithEarlierTimeStamp_IsCurrent()
    {
        var older = Tx("a", 5, T0.AddHours(2));
        var newer = Tx("b", 9, T0); // sealed later, earlier timestamp
        var lookup = Dockets((5, ["a"]), (9, ["b"]));

        var current = SystemBlueprintCurrency.Current([newer, older], lookup, NullLogger.Instance);

        current.Should().BeSameAs(newer);
    }

    [Fact]
    public void OrderByLedger_SameDocket_OrdersByTransactionIdsIndex()
    {
        // Insertion order and timestamps both disagree with the docket's own order.
        var first = Tx("first", 7, T0.AddHours(5));
        var second = Tx("second", 7, T0.AddHours(1));
        var lookup = Dockets((7, ["first", "second"]));

        var ordered = SystemBlueprintCurrency.OrderByLedger([second, first], lookup, NullLogger.Instance);

        ordered.Select(t => t.TxId).Should().Equal("first", "second");
    }

    [Fact]
    public void OrderByLedger_NullDocketNumber_IsExcluded()
    {
        var sealedTx = Tx("sealed", 3, T0);
        var pending = Tx("pending", null, T0.AddDays(1));
        var lookup = Dockets((3, ["sealed"]));

        var ordered = SystemBlueprintCurrency.OrderByLedger([pending, sealedTx], lookup, NullLogger.Instance);

        ordered.Should().ContainSingle().Which.TxId.Should().Be("sealed");
    }

    [Fact]
    public void OrderByLedger_UnknownDocketOrTxMissingFromDocket_IsExcluded()
    {
        var ok = Tx("ok", 1, T0);
        var noDocket = Tx("nodocket", 2, T0);
        var notListed = Tx("notlisted", 1, T0);
        var lookup = Dockets((1, ["ok"]));

        var ordered = SystemBlueprintCurrency.OrderByLedger([noDocket, notListed, ok], lookup, NullLogger.Instance);

        ordered.Select(t => t.TxId).Should().Equal("ok");
    }

    [Fact]
    public void VersionOf_ReturnsOneBasedOrdinalInLedgerOrder()
    {
        var a = Tx("a", 1, T0.AddHours(3));
        var b = Tx("b", 2, T0.AddHours(2));
        var c = Tx("c", 2, T0.AddHours(1));
        var lookup = Dockets((1, ["a"]), (2, ["b", "c"]));
        var input = new[] { c, a, b };

        SystemBlueprintCurrency.VersionOf("a", input, lookup, NullLogger.Instance).Should().Be(1);
        SystemBlueprintCurrency.VersionOf("b", input, lookup, NullLogger.Instance).Should().Be(2);
        SystemBlueprintCurrency.VersionOf("c", input, lookup, NullLogger.Instance).Should().Be(3);
        SystemBlueprintCurrency.VersionOf("zzz", input, lookup, NullLogger.Instance).Should().BeNull();
    }
}
