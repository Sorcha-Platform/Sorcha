// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Sorcha.Register.Models;

using StackExchange.Redis;

using Xunit;

namespace Sorcha.Register.Storage.Redis.Tests;

/// <summary>
/// #1669 — the store that carries a validator rejection from the validator to the register's
/// transaction-status endpoint, so a caller holding a transaction id can learn it was refused.
/// </summary>
public class TransactionRejectionLogTests
{
    private const string RegisterId = "4145b4e7102246ae997f2510acba2220";
    private const string TxId = "abe4e5f71553a670e3ea01e0caa60c265052686f5c444cb221e56a1f235e0a00";

    private static TransactionRejection Fork() => new(
        TxId, RegisterId, "VAL_CHAIN_FORK",
        "Fork detected: 1 existing transaction(s) already reference previous transaction 'd20cfff7'",
        DateTimeOffset.Parse("2026-09-19T11:11:54Z"));

    [Fact]
    public async Task RecordThenFind_ReturnsTheReasonTheValidatorGave()
    {
        var (log, store) = Build();

        await log.RecordAsync(Fork());
        var found = await log.FindAsync(RegisterId, TxId);

        store.Should().ContainKey(RedisTransactionRejectionLog.KeyFor(RegisterId, TxId));
        found.Should().NotBeNull();
        found!.Code.Should().Be("VAL_CHAIN_FORK");
        found.Message.Should().Contain("Fork detected");
        found.RejectedAt.Should().Be(DateTimeOffset.Parse("2026-09-19T11:11:54Z"));
    }

    [Fact]
    public async Task Record_SetsAnExpiry_SoThisStaysANotificationSurfaceNotASecondLedger()
    {
        var expiries = new List<TimeSpan?>();
        var (log, _) = Build(onSet: (_, _, e) => expiries.Add(e));

        await log.RecordAsync(Fork());

        expiries.Should().ContainSingle().Which.Should().Be(RedisTransactionRejectionLog.Retention);
    }

    [Fact]
    public async Task Find_ForATransactionNobodyRejected_IsNull()
    {
        var (log, _) = Build();

        (await log.FindAsync(RegisterId, TxId)).Should().BeNull();
    }

    [Fact]
    public async Task Record_WhenRedisThrows_DoesNotPropagate()
    {
        // Recording is best-effort. Failing to note a rejection must never become a second failure
        // on the validation path — the log line the validator already writes is the fallback.
        var (log, _) = Build(setThrows: true);

        var act = () => log.RecordAsync(Fork());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Find_WhenRedisThrows_IsNullRatherThanAnError()
    {
        // "Not known" — which the endpoint is careful to report as NOT evidence of acceptance.
        var (log, _) = Build(getThrows: true);

        (await log.FindAsync(RegisterId, TxId)).Should().BeNull();
    }

    [Fact]
    public void KeyFor_ScopesByRegister_SoTwoRegistersCannotCollideOnATxId()
    {
        RedisTransactionRejectionLog.KeyFor("reg-a", TxId)
            .Should().NotBe(RedisTransactionRejectionLog.KeyFor("reg-b", TxId));
    }

    /// <summary>
    /// A minimal in-memory stand-in for the one Redis get/set pair this type uses.
    /// </summary>
    /// <remarks>
    /// The Setup expressions mirror production's call shape exactly. Moq binds a Setup to one
    /// overload, and StackExchange.Redis has several <c>StringSetAsync</c> overloads — a Setup on
    /// the wrong one silently returns null, the await NREs, and the best-effort catch swallows it,
    /// which looks identical to "Redis was never called". That cost a debugging round here.
    /// </remarks>
    private static (RedisTransactionRejectionLog Log, Dictionary<string, string> Store) Build(
        Action<string, string, TimeSpan?>? onSet = null,
        bool setThrows = false,
        bool getThrows = false)
    {
        var store = new Dictionary<string, string>(StringComparer.Ordinal);
        var db = new Mock<IDatabase>();

        var set = db.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<When>(), It.IsAny<CommandFlags>()));

        if (setThrows)
        {
            set.ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));
        }
        else
        {
            set.Returns((RedisKey k, RedisValue v, TimeSpan? e, When _, CommandFlags _) =>
            {
                store[k.ToString()] = v.ToString();
                onSet?.Invoke(k.ToString(), v.ToString(), e);
                return Task.FromResult(true);
            });
        }

        var get = db.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()));

        if (getThrows)
        {
            get.ThrowsAsync(new RedisConnectionException(ConnectionFailureType.SocketFailure, "down"));
        }
        else
        {
            get.Returns((RedisKey k, CommandFlags _) => Task.FromResult(
                store.TryGetValue(k.ToString(), out var v) ? new RedisValue(v) : RedisValue.Null));
        }

        var mux = new Mock<IConnectionMultiplexer>();
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(db.Object);

        return (
            new RedisTransactionRejectionLog(mux.Object, NullLogger<RedisTransactionRejectionLog>.Instance),
            store);
    }
}
