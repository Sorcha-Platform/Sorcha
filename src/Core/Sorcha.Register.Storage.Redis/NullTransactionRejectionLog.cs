// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

using Sorcha.Register.Models;

namespace Sorcha.Register.Storage.Redis;

/// <summary>
/// Used when no Redis connection is configured: rejections keep going to the log and nowhere else,
/// exactly as before #1669.
/// </summary>
/// <remarks>
/// Deliberately not silent. A deployment running on this cannot tell a submitter why anything was
/// refused, which is the defect #1669 exists to fix — so it says so once per rejection rather than
/// letting the gap look like "nothing was rejected".
/// </remarks>
public sealed class NullTransactionRejectionLog : ITransactionRejectionLog
{
    private readonly ILogger<NullTransactionRejectionLog> _logger;

    /// <summary>Initialises a new instance of <see cref="NullTransactionRejectionLog"/>.</summary>
    public NullTransactionRejectionLog(ILogger<NullTransactionRejectionLog> logger) =>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public Task RecordAsync(TransactionRejection rejection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rejection);

        _logger.LogWarning(
            "No rejection store is configured, so the refusal of transaction {TransactionId} on "
            + "register {RegisterId} ({Code}) is not discoverable by whoever submitted it.",
            rejection.TransactionId, rejection.RegisterId, rejection.Code);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TransactionRejection?> FindAsync(
        string registerId, string transactionId, CancellationToken cancellationToken = default) =>
        Task.FromResult<TransactionRejection?>(null);
}
