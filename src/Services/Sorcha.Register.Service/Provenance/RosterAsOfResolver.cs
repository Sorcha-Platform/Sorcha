// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Provenance.Engine.Evidence;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Service.Provenance;

/// <summary>
/// Walks a register's control transactions to find the validator set that applied when a given
/// docket was sealed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The exclusive boundary is the point of this class.</b> A roster change sealed in docket 12
/// cannot govern docket 12: that docket was proposed and signed under the set in force at the time,
/// and the change only takes effect once it is sealed. So the roster applying at docket N is the one
/// established by the latest control transaction sealed <i>strictly before</i> N.
/// </para>
/// <para>
/// Resolving this inclusively would report the validators who sealed <i>every governance docket</i>
/// as unauthorised — a false accusation landing exactly on the dockets that record the network
/// growing, which is the event this whole feature exists to make legible. <c>RosterAsOfResolverTests</c>
/// pins both sides of the boundary (dockets 12 and 13).
/// </para>
/// <para>
/// The genesis docket is the single exception, and not really an exception at all: it declares the
/// validator set that seals it, so the earliest control record governs docket 0 itself. That falls
/// out of the fallback below rather than needing a special case.
/// </para>
/// <para>
/// <b>Never throws for missing or unreadable evidence.</b> Everything unresolvable becomes null,
/// which the engine renders as
/// <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/> with a reason. A view
/// that cannot establish one link must still render the rest (FR-020).
/// </para>
/// <para>
/// No caching yet. Plan Open Items records roster-as-of resolution cost as unmeasured and defers it
/// to measurement against SC-007's 5,000 dockets rather than pre-optimising. Note that the spine
/// endpoint does not call this at all (D6) — only the per-docket trail does, once.
/// </para>
/// </remarks>
public sealed class RosterAsOfResolver : IRosterAsOfResolver
{
    private readonly IReadOnlyRegisterRepository _repository;
    private readonly ILogger<RosterAsOfResolver> _logger;

    /// <summary>Creates a resolver over the register's read-only store.</summary>
    public RosterAsOfResolver(IReadOnlyRegisterRepository repository, ILogger<RosterAsOfResolver> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<RosterAsOf?> ResolveAsync(
        string registerId,
        ulong docketNumber,
        CancellationToken cancellationToken = default)
    {
        var versions = await ReadRosterVersionsAsync(registerId, cancellationToken);

        if (versions is null)
        {
            return null;
        }

        if (versions.Count == 0)
        {
            _logger.LogDebug(
                "Register {RegisterId} has no control transaction carrying a validator roster; roster-as-of for docket {DocketNumber} is unresolvable",
                registerId, docketNumber);
            return null;
        }

        // Strictly before: the docket that seals a change is still governed by the previous set.
        var applicable = versions.LastOrDefault(v => v.Tx.DocketNumber!.Value < docketNumber);

        // Nothing sealed before this docket — the genesis docket, or a docket at or before the first
        // recorded roster. The earliest recorded set is the one that governed it.
        if (applicable.Tx is null)
        {
            applicable = versions[0];
        }

        var entries = applicable.Roster.Validators
            .Select(v => new RosterEntryAsOf
            {
                ValidatorId = v.ValidatorId,
                PublicKey = v.PublicKey,
                HeldAuthority = HeldAuthority(v.Status),
            })
            .ToList();

        return new RosterAsOf
        {
            RosterVersion = applicable.Roster.Version,
            Entries = entries,
            ResolvedFrom =
                $"control transaction {applicable.Tx.TxId} (sealed in docket {applicable.Tx.DocketNumber!.Value})",
        };
    }

    /// <inheritdoc/>
    public async Task<IReadOnlySet<ulong>> GetRosterChangeDocketsAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        var versions = await ReadRosterVersionsAsync(registerId, cancellationToken);

        if (versions is null || versions.Count <= 1)
        {
            // No history, or only the genesis set — which is the origin, not a change within it.
            return new HashSet<ulong>();
        }

        var changes = new HashSet<ulong>();

        for (var i = 1; i < versions.Count; i++)
        {
            if (DiffersFrom(versions[i].Roster, versions[i - 1].Roster))
            {
                changes.Add(versions[i].Tx.DocketNumber!.Value);
            }
        }

        return changes;
    }

    /// <summary>
    /// Did the validator set actually change between two consecutive control records?
    /// </summary>
    /// <remarks>
    /// Compares membership and authority rather than the declared version number. A governance
    /// commit that touches only the admin attestations bumps the record without changing who may
    /// sign dockets; marking that on the spine would tell an administrator the validator set changed
    /// when it did not.
    /// </remarks>
    private static bool DiffersFrom(ValidatorRoster current, ValidatorRoster previous)
    {
        static HashSet<string> Signature(ValidatorRoster roster) =>
            roster.Validators
                .Select(v => $"{v.ValidatorId}|{v.PublicKey}|{v.Status}")
                .ToHashSet(StringComparer.Ordinal);

        return !Signature(current).SetEquals(Signature(previous));
    }

    /// <summary>
    /// Every sealed control transaction carrying a validator roster, oldest first, or null when the
    /// store could not be read.
    /// </summary>
    private async Task<List<(TransactionModel Tx, ValidatorRoster Roster)>?> ReadRosterVersionsAsync(
        string registerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var controlTransactions = await _repository.GetTransactionsByTypeAsync(
                registerId,
                TransactionType.Control,
                TransactionSort.DocketNumberAscending,
                cancellationToken: cancellationToken);

            return controlTransactions
                // An unsealed control transaction has no position in history, so it cannot be said
                // to apply from any point. Including it would let a proposal govern the register.
                .Where(tx => tx.DocketNumber.HasValue)
                .Select(tx => (Tx: tx, Roster: ReadValidatorRoster(tx)))
                .Where(pair => pair.Roster is not null)
                .Select(pair => (pair.Tx, Roster: pair.Roster!))
                .OrderBy(pair => pair.Tx.DocketNumber!.Value)
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The resolver must never be the thing that breaks the view.
            _logger.LogWarning(ex,
                "Could not read control transactions for register {RegisterId}; the validator roster history is unavailable",
                registerId);
            return null;
        }
    }

    /// <summary>
    /// Could this key legitimately have signed a docket governed by this roster version?
    /// </summary>
    /// <remarks>
    /// <c>Rotated</c> counts, deliberately. The register's own <see cref="ValidatorKeyStatus"/>
    /// documentation says a rotated key "can still verify historical dockets" — a rotated key is
    /// exactly what a historical docket was signed with, and the platform's existing
    /// <c>ValidatorRoster.VerifiableValidators</c> applies the same rule for the same reason.
    /// Excluding it would produce a false failure on every register that has ever rotated a key,
    /// which is the quieter sibling of the roster-as-of trap. <c>Revoked</c> and <c>Ejected</c> are
    /// rejected for all verification purposes.
    /// </remarks>
    private static bool HeldAuthority(ValidatorKeyStatus status) =>
        status is ValidatorKeyStatus.Active or ValidatorKeyStatus.Rotated;

    /// <summary>
    /// Reads the validator roster out of a control transaction, or null when it carries none.
    /// </summary>
    /// <remarks>
    /// A control transaction may carry governance attestations without touching the validator set.
    /// Treating such a record as the applicable roster would empty the set and turn every signature
    /// into a failure, so it is skipped and the walk continues to the previous version.
    /// </remarks>
    private static ValidatorRoster? ReadValidatorRoster(TransactionModel transaction)
    {
        var data = transaction.Payloads?.FirstOrDefault()?.Data;
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            // Smart decode, mirroring GovernanceRosterService: legacy Base64 (+, /, =) or Base64url.
            bytes = data.Contains('+') || data.Contains('/') || data.Contains('=')
                ? Convert.FromBase64String(data)
                : System.Buffers.Text.Base64Url.DecodeFromChars(data);
        }
        catch (FormatException)
        {
            return null;
        }

        var record = RegisterControlPayloadReader.TryRead(bytes);
        var roster = record?.Validators;

        return roster is { Validators.Count: > 0 } ? roster : null;
    }
}
