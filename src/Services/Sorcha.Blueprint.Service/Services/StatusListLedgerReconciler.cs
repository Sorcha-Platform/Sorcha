// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>How far a status list has been reconciled against the register.</summary>
public enum StatusListReadiness
{
    /// <summary>Folded up to a known docket — answers are authoritative.</summary>
    Ready,

    /// <summary>Still replaying the ledger. "I don't know yet", NOT "nothing is revoked".</summary>
    Warming,

    /// <summary>The register could not be read. Callers must fail closed.</summary>
    Failed
}

/// <summary>Outcome of a reconciliation pass.</summary>
public record StatusListReconciliation(StatusListReadiness Readiness, int EventsApplied, long? ReconciledToDocket, string? Error);

/// <summary>
/// Rebuilds a Bitstring Status List's bits from the <c>CredentialStatusChange</c> transactions on
/// its register.
/// </summary>
/// <remarks>
/// <para>
/// The register is the source of truth for a credential's status: a sealed transaction is what
/// replicates to other nodes and what an auditor reads. The bitstring is a projection of those
/// events, which is what makes a revocation raised on one node visible on another — tiny folds n1's
/// transaction because it is a docket it already syncs, not because any cache was copied.
/// </para>
/// <para>
/// <b>Order matters, and that is not obvious.</b> Suspend and reinstate currently share the
/// revocation bit (<c>AllocateIndexAsync</c> hardcodes purpose <c>revocation</c>), so the bit is
/// NOT monotonic: <c>set → clear → set</c> and <c>set → set → clear</c> end in opposite states.
/// Events are therefore applied strictly in ledger order (docket, then timestamp within a docket),
/// never in arrival order — otherwise two nodes folding the same events can converge on opposite
/// answers and both believe they are correct.
/// </para>
/// </remarks>
/// <remarks>
/// <para>
/// Takes <see cref="IServiceScopeFactory"/> rather than <c>IRegisterServiceClient</c> directly.
/// The register client is registered SCOPED, and this reconciler is consumed by the singleton
/// <c>StatusListManager</c> — holding the client would be a captive dependency, which .NET's DI
/// validation refuses at startup. A scope is created per reconciliation instead.
/// </para>
/// </remarks>
public class StatusListLedgerReconciler(
    IServiceScopeFactory scopeFactory,
    ILogger<StatusListLedgerReconciler> logger)
{
    private const int PageSize = 100;

    /// <summary>
    /// Replays every status-change event for <paramref name="listId"/> onto <paramref name="list"/>.
    /// </summary>
    /// <remarks>
    /// Replays from the beginning rather than from a watermark. A status list is small and this runs
    /// once per list per process; replaying the whole history is simpler than a resumable fold and
    /// cannot drift. The watermark is recorded so callers can see how current the projection is.
    /// </remarks>
    public async Task<StatusListReconciliation> ReconcileAsync(
        BitstringStatusList list,
        string listId,
        CancellationToken ct = default)
    {
        var applied = 0;
        long? highestDocket = null;

        try
        {
            var events = new List<(ulong Docket, DateTime At, CredentialStatusChangePayload Payload)>();

            using var scope = scopeFactory.CreateScope();
            var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();

            var page = 1;
            while (true)
            {
                var batch = await registerClient
                    .GetTransactionsAsync(list.RegisterId, page, PageSize, ct)
                    .ConfigureAwait(false);

                if (batch.Transactions.Count == 0) break;

                foreach (var tx in batch.Transactions)
                {
                    if (tx.MetaData?.TransactionType != TransactionType.CredentialStatusChange) continue;

                    var payload = TryReadPayload(tx);
                    if (payload is null) continue;

                    // Only events that name THIS list can be projected onto it.
                    if (!string.Equals(payload.StatusListId, listId, StringComparison.Ordinal)) continue;
                    if (payload.StatusListIndex is null) continue;

                    events.Add((tx.DocketNumber ?? 0, tx.TimeStamp, payload));
                }

                if (page >= Math.Max(1, batch.TotalPages)) break;
                page++;
            }

            // LEDGER ORDER — see the class remarks. Arrival order is not good enough.
            var isSuspensionList = listId.Contains("-suspension-", StringComparison.Ordinal);

            foreach (var e in events.OrderBy(e => e.Docket).ThenBy(e => e.At))
            {
                var bit = BitForPurpose(e.Payload.NewStatus, isSuspensionList);
                if (bit is null) continue;

                list.SetBit(e.Payload.StatusListIndex!.Value, bit.Value);
                applied++;
                highestDocket = (long)e.Docket;
            }

            logger.LogInformation(
                "Reconciled status list {ListId} from register {RegisterId}: {Applied} status change(s), up to docket {Docket}",
                listId, list.RegisterId, applied, highestDocket?.ToString() ?? "(none)");

            return new StatusListReconciliation(StatusListReadiness.Ready, applied, highestDocket, null);
        }
        catch (Exception ex)
        {
            // Failed, NOT Ready-with-zero-events. Reporting an unread register as "nothing is
            // revoked" is the one answer that must never be given.
            logger.LogError(ex,
                "Could not reconcile status list {ListId} from register {RegisterId} — the list must "
                + "be treated as unavailable rather than empty",
                listId, list.RegisterId);
            return new StatusListReconciliation(StatusListReadiness.Failed, applied, null, ex.Message);
        }
    }

    /// <summary>
    /// Maps a status word to the revocation bit. <c>Active</c> clears it; <c>Revoked</c> and
    /// <c>Suspended</c> set it — they share one bit today, which is why the fold must be ordered.
    /// </summary>
    /// <summary>
    /// The bit a status change implies for THIS list, or null when the event does not concern it.
    /// </summary>
    /// <remarks>
    /// W3C keeps the two purposes distinct — revocation is not reversible, suspension is — so each
    /// list only folds the events that belong to it:
    /// <list type="bullet">
    ///   <item><c>Revoked</c> sets the revocation bit and says nothing about suspension.</item>
    ///   <item><c>Suspended</c> sets the suspension bit and MUST NOT touch revocation.</item>
    ///   <item><c>Active</c> (reinstatement) clears the suspension bit only. Clearing a revocation
    ///         bit would contradict the spec, so a revocation list ignores it entirely rather than
    ///         un-revoking a credential.</item>
    /// </list>
    /// </remarks>
    private static bool? BitForPurpose(string? status, bool isSuspensionList)
    {
        if (status is null) return null;

        if (status.Equals("Revoked", StringComparison.OrdinalIgnoreCase))
            return isSuspensionList ? null : true;

        if (status.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
            return isSuspensionList ? true : null;

        if (status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            return isSuspensionList ? false : null;

        return null;
    }

    private static CredentialStatusChangePayload? TryReadPayload(TransactionModel tx)
    {
        var data = tx.Payloads.FirstOrDefault()?.Data;
        if (string.IsNullOrWhiteSpace(data)) return null;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(data));
            return JsonSerializer.Deserialize<CredentialStatusChangePayload>(json);
        }
        catch (Exception)
        {
            // An encrypted or foreign payload on a shared register is expected, not an error —
            // it simply is not a status change this node can read.
            return null;
        }
    }
}
