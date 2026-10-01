// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>
/// The outcome of resolving which <c>register-governance-v1</c> definition governs a governance step.
/// </summary>
public abstract record PinResolution
{
    /// <summary>Governed by the definition with this publication id.</summary>
    /// <param name="DefinitionTxId">System Register publication id of the governing definition.</param>
    /// <param name="IsRaise">
    /// True when the transaction carries its own pin and enacts no earlier proposal — a raise (including
    /// an Owner-override propose-and-enact), which must additionally be pinned to the <i>current</i>
    /// definition. False for approvals and enactments, which are never held to "current".
    /// </param>
    public sealed record Pinned(string DefinitionTxId, bool IsRaise) : PinResolution;

    /// <summary>
    /// No pin exists to honour (a proposal raised before pinning existed); the current definition applies
    /// and the occurrence is counted.
    /// </summary>
    /// <param name="Step">Exactly <c>"proposal"</c>, <c>"approval"</c> or <c>"enactment"</c> (a metric tag).</param>
    public sealed record Legacy(string Step) : PinResolution;

    /// <summary>The governing definition cannot be determined; the transaction must be refused.</summary>
    /// <param name="Reason">Operator-facing explanation.</param>
    public sealed record Unresolvable(string Reason) : PinResolution;
}

/// <summary>
/// The single implementation of "which governance definition governs this step" (Feature 197).
/// </summary>
/// <remarks>
/// <para>
/// <b>Own pin first, on a raise only.</b> An enactment (non-null <c>EnactsProposalId</c>) that carries a pin is refused: it is judged by its proposal's pin. An Owner-override propose-and-enact is one transaction that carries its own pin,
/// a roster and a null <c>EnactsProposalId</c>. It references no earlier proposal, so consulting a
/// reference first would either find nothing or find an unrelated one. On a raise, the transaction's own signed pin
/// is the strongest evidence available and wins.
/// </para>
/// <para>
/// <b>Unreadable is never Legacy.</b> "Legacy" means the referenced proposal was read and carries no pin.
/// If the proposal cannot be read we do not know whether it was pinned; falling back to the current
/// definition would let an outage (or an attacker) strip the pin and have a step judged under a newer
/// definition than the one it was raised under. It fails closed instead.
/// </para>
/// <para>Pure: no I/O and no logging; the caller reads the proposal and records any metric.</para>
/// </remarks>
public static class GovernanceDefinitionPin
{
    /// <summary>Resolves the governing definition for one governance step.</summary>
    /// <param name="actionId">The <see cref="GovernanceBlueprint"/> action the transaction targets.</param>
    /// <param name="ownPayload">The transaction's own control payload, or null (approvals carry a different payload).</param>
    /// <param name="referencedProposal">The proposal this step references, when it could be read.</param>
    /// <param name="referencedProposalReadable">False when the referenced proposal could not be read.</param>
    /// <returns>The resolution.</returns>
    public static PinResolution Resolve(
        int actionId,
        ControlTransactionPayload? ownPayload,
        ControlTransactionPayload? referencedProposal,
        bool referencedProposalReadable)
    {
        // A present-but-blank pin is malformed, never absent: only null means "no pin". Treating blank as
        // absent would let a stripped pin degrade to Legacy (the current definition).
        if (ownPayload?.GovernanceDefinitionTxId is { } ownPin)
        {
            // An enactment is judged by the proposal it enacts. A pin of its own would let it choose
            // its own governing definition and escape the one its proposal was raised under.
            if (ownPayload.EnactsProposalId is not null)
            {
                return new PinResolution.Unresolvable("an enactment must not carry its own pin");
            }

            if (string.IsNullOrWhiteSpace(ownPin))
            {
                return new PinResolution.Unresolvable("malformed pin");
            }

            return new PinResolution.Pinned(ownPin, IsRaise: true);
        }

        if (actionId == GovernanceBlueprint.CollectQuorumActionId)
        {
            return FromProposal(referencedProposal, referencedProposalReadable, "approval");
        }

        if (actionId == GovernanceBlueprint.RecordControlTransactionActionId
            || ownPayload?.EnactsProposalId is not null)
        {
            return FromProposal(referencedProposal, referencedProposalReadable, "enactment");
        }

        if (actionId == GovernanceBlueprint.ProposeChangeActionId)
        {
            return new PinResolution.Legacy("proposal");
        }

        return new PinResolution.Unresolvable("not a governance step");
    }

    private static PinResolution FromProposal(
        ControlTransactionPayload? proposal, bool readable, string step)
    {
        if (!readable || proposal is null)
        {
            return new PinResolution.Unresolvable("referenced proposal unreadable");
        }

        if (proposal.GovernanceDefinitionTxId is not { } pin)
        {
            return new PinResolution.Legacy(step);
        }

        return string.IsNullOrWhiteSpace(pin)
            ? new PinResolution.Unresolvable("malformed pin")
            : new PinResolution.Pinned(pin, IsRaise: false);
    }
}
