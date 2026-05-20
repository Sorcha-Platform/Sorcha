// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// US5 PR2 placeholder for <see cref="IPresentationLogForwarder"/>. Records that a
/// presentation-log entry would be forwarded to the Blueprint Service, but does not
/// write anything to a register.
/// </summary>
/// <remarks>
/// The real Blueprint forward (PR3) is intentionally deferred: the offline
/// <c>IPresentationConsumer</c> contract is being reconciled against F127 (consumers
/// no longer write the register directly), so committing a wire shape now would bake
/// in a contract known to be stale. PR2 ships the full ingest + dedupe + PWA drain
/// path against this stub; PR3 swaps the implementation behind the same interface.
/// </remarks>
public sealed class LoggingPresentationLogForwarder : IPresentationLogForwarder
{
    private readonly ILogger<LoggingPresentationLogForwarder> _logger;

    /// <summary>Initialise a new instance.</summary>
    public LoggingPresentationLogForwarder(ILogger<LoggingPresentationLogForwarder> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public Task ForwardAsync(Guid platformUserId, PresentationLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _logger.LogInformation(
            "Presentation-log entry accepted (forward stub) platformUser={PlatformUserId} entryId={EntryId} " +
            "credentialId={CredentialId} outcome={Outcome} disclosedClaims={ClaimCount} registerId={RegisterId} " +
            "actionTxId={ActionTxId} — Blueprint lifecycle write lands in US5 PR3.",
            platformUserId, entry.Id, entry.CredentialId, entry.Outcome, entry.DisclosedClaims.Count,
            entry.RegisterId, entry.ActionTxId);

        return Task.CompletedTask;
    }
}
