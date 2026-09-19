// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using Microsoft.Extensions.Logging;

using Sorcha.Register.Models;

using StackExchange.Redis;

namespace Sorcha.Register.Storage.Redis;

/// <summary>
/// Redis-backed <see cref="ITransactionRejectionLog"/> (#1669). The validator writes; the register's
/// transaction-status endpoint reads. Redis is the seam because it is the one store both already
/// share.
/// </summary>
public sealed class RedisTransactionRejectionLog : ITransactionRejectionLog
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisTransactionRejectionLog> _logger;

    /// <summary>
    /// How long a rejection stays discoverable. Long enough that a caller polling after a 202 — or a
    /// person investigating the next morning — still finds it; short enough that this stays a
    /// notification surface rather than a second, lossy ledger.
    /// </summary>
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Initialises a new instance of <see cref="RedisTransactionRejectionLog"/>.</summary>
    public RedisTransactionRejectionLog(
        IConnectionMultiplexer redis,
        ILogger<RedisTransactionRejectionLog> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal static string KeyFor(string registerId, string transactionId) =>
        $"sorcha:tx-rejection:{registerId}:{transactionId}";

    /// <inheritdoc />
    public async Task RecordAsync(TransactionRejection rejection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rejection);

        try
        {
            var db = _redis.GetDatabase();
            // Every argument spelled out to pin the TimeSpan? overload. The three-argument form
            // binds a newer Expiration overload instead, and which one you get is invisible at the
            // call site — including to a test that mocks the other one and then sees nothing.
            await db.StringSetAsync(
                KeyFor(rejection.RegisterId, rejection.TransactionId),
                JsonSerializer.Serialize(rejection, JsonOptions),
                Retention,
                When.Always,
                CommandFlags.None);
        }
        catch (Exception ex)
        {
            // Recording is best-effort: failing to note a rejection must never turn into a second
            // failure on the validation path. The log line below is the fallback it always had.
            _logger.LogError(ex,
                "Could not record the rejection of transaction {TransactionId} on register {RegisterId} "
                + "({Code}: {Message}); it will not be visible to the submitter.",
                rejection.TransactionId, rejection.RegisterId, rejection.Code, rejection.Message);
        }
    }

    /// <inheritdoc />
    public async Task<TransactionRejection?> FindAsync(
        string registerId, string transactionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registerId) || string.IsNullOrWhiteSpace(transactionId))
        {
            return null;
        }

        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(KeyFor(registerId, transactionId));
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return JsonSerializer.Deserialize<TransactionRejection>((string)value!, JsonOptions);
        }
        catch (Exception ex)
        {
            // A read failure is "not known", the same as an absent entry — and the caller must not
            // read that as acceptance, which is why the endpoint says so explicitly.
            _logger.LogWarning(ex,
                "Could not read the rejection record for transaction {TransactionId} on register {RegisterId}.",
                transactionId, registerId);
            return null;
        }
    }
}
