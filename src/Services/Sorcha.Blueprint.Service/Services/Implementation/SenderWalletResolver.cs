// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Responses;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Chooses which of the caller's wallets submits an action (#1658). This is the one home of that rule, so
/// that a client never has to re-derive participant bindings to learn which wallet to send.
/// </summary>
/// <remarks>
/// <para>
/// It mirrors <b>the validator's</b> sender-authorisation rule (<c>VAL_BP_002</c>,
/// <c>ValidationEngine</c>), in the same order, because the validator is what decides whether a
/// submission ever seals:
/// </para>
/// <list type="number">
/// <item><b>Tier 1</b> — a wallet hard-coded on the sender participant in the published blueprint.</item>
/// <item><b>Tier 2</b> — a participant record published to the register for that role: the signer must be
/// one of its published addresses. Checked BEFORE the in-instance binding, as the validator does.</item>
/// <item><b>Tier 3</b> — a wallet already bound to that role earlier in this instance.</item>
/// <item>Otherwise the sender is unbound. Only a <b>starting</b> action late-binds (Feature 103), so the
/// caller's own wallet may be offered only there; for any later action nothing can submit until a
/// participant record is published, and that is reported as
/// <see cref="SenderWalletStatus.AwaitingParticipantRecord"/>.</item>
/// </list>
/// <para>
/// ⚠ #1664, found live in cold-start run #4. This resolver originally mirrored the EXECUTE path, which
/// accepts any wallet the caller owns for an unbound sender. The validator does not: the submission was
/// accepted with a 202 and then refused with <c>VAL_BP_002</c>, which reaches no audit log, so the caller
/// saw success followed by silence. Mirroring execute is not enough — execute admits transactions the
/// ledger then refuses.
/// </para>
/// <para>
/// Wallet comparisons are ordinal-ignore-case, as in the validator. It only ever returns wallets the
/// caller holds: it advises, and the execute path, the validator and the Wallet Service remain authority.
/// </para>
/// </remarks>
public static class SenderWalletResolver
{
    /// <summary>Resolves the submission context for <paramref name="action"/> on <paramref name="instance"/>.</summary>
    /// <param name="blueprint">The instance's pinned published definition.</param>
    /// <param name="action">The action being submitted.</param>
    /// <param name="instance">The workflow instance.</param>
    /// <param name="callerWallets">Every wallet resolved for the caller.</param>
    /// <param name="publishedAddresses">
    /// The addresses on the participant record published to the register for this action's sender, or null
    /// when no record is published (or the lookup could not be made). An empty list means the same as null:
    /// nothing on the register binds the role.
    /// </param>
    public static ActionSubmissionContext Resolve(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        Sorcha.Blueprint.Models.Action action,
        Instance instance,
        IReadOnlyCollection<string> callerWallets,
        IReadOnlyCollection<string>? publishedAddresses)
    {
        var senderParticipantId = string.IsNullOrWhiteSpace(action.Sender) ? null : action.Sender;

        // Tier 1 — a wallet written into the published blueprint.
        var hardcoded = senderParticipantId is null
            ? null
            : blueprint.Participants?
                .FirstOrDefault(p => string.Equals(p.Id, senderParticipantId, StringComparison.OrdinalIgnoreCase))?
                .WalletAddress;

        if (!string.IsNullOrWhiteSpace(hardcoded))
        {
            return Bound(instance, hardcoded, callerWallets, senderParticipantId);
        }

        // Tier 2 — the participant record published to the register. Any of its addresses is acceptable to
        // the validator, so offer the one the caller holds.
        var published = publishedAddresses?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (published is { Count: > 0 })
        {
            var held = published.FirstOrDefault(a =>
                callerWallets.Any(w => string.Equals(w, a, StringComparison.OrdinalIgnoreCase)));

            return held is not null
                ? Context(instance, SenderWalletStatus.Resolved, held, senderParticipantId)
                : Context(instance, SenderWalletStatus.NotYours, null, senderParticipantId);
        }

        // Tier 3 — a wallet bound to this role earlier in this instance.
        if (senderParticipantId is not null
            && instance.ParticipantWallets.TryGetValue(senderParticipantId, out var boundWallet)
            && !string.IsNullOrWhiteSpace(boundWallet))
        {
            return Bound(instance, boundWallet, callerWallets, senderParticipantId);
        }

        // Unbound. Only a starting action may late-bind the submitter (Feature 103).
        if (!action.IsStartingAction)
        {
            return Context(instance, SenderWalletStatus.AwaitingParticipantRecord, null, senderParticipantId);
        }

        var distinct = callerWallets
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return distinct.Count switch
        {
            0 => Context(instance, SenderWalletStatus.NoWallet, null, senderParticipantId),
            1 => Context(instance, SenderWalletStatus.Resolved, distinct[0], senderParticipantId),
            _ => Context(instance, SenderWalletStatus.Ambiguous, null, senderParticipantId) with
            {
                CandidateWallets = distinct,
            },
        };
    }

    /// <summary>A wallet the ledger already binds to the role: usable only if the caller holds it.</summary>
    private static ActionSubmissionContext Bound(
        Instance instance, string bound, IReadOnlyCollection<string> callerWallets, string? participantId) =>
        callerWallets.Any(w => string.Equals(w, bound, StringComparison.OrdinalIgnoreCase))
            ? Context(instance, SenderWalletStatus.Resolved, bound, participantId)
            : Context(instance, SenderWalletStatus.NotYours, null, participantId);

    private static ActionSubmissionContext Context(
        Instance instance, SenderWalletStatus status, string? wallet, string? participantId) => new()
    {
        BlueprintId = instance.BlueprintId,
        RegisterId = instance.RegisterId,
        SenderWalletStatus = status,
        SenderWallet = wallet,
        UnboundParticipantId = status == SenderWalletStatus.AwaitingParticipantRecord ? participantId : null,
    };
}
