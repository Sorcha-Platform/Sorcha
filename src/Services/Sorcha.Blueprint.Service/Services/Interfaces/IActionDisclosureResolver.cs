// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Single authority for the DAD disclosure model (Feature 176). Resolves which fields of an action's
/// data are disclosed to which recipient wallet, and reconstructs the prior-action data disclosed to a
/// given caller. Extracted from the previously-private
/// <c>ActionExecutionService.ApplyDisclosuresAsync</c> so the execution path (submit-side) and the
/// disclosed-data query endpoint (read-side) share one implementation and can never diverge.
/// </summary>
public interface IActionDisclosureResolver
{
    /// <summary>
    /// Applies an action's JSON-Pointer disclosure rules to <paramref name="data"/> and resolves each
    /// recipient participant to a wallet address, returning the fields disclosed to each recipient
    /// wallet. This is the submit-side primitive used by the execution pipeline when sealing an action;
    /// behaviour is identical to the former private method (no drift).
    /// </summary>
    /// <param name="action">The action whose <c>Disclosures</c> rules govern the filtering.</param>
    /// <param name="data">The action's payload (already merged with any calculated fields).</param>
    /// <param name="blueprint">The blueprint (used for participant → register-record resolution).</param>
    /// <param name="participantWallets">Instance participant-id → wallet-address bindings (tier-1 resolution).</param>
    /// <param name="registerId">The register the instance lives on (tier-2 participant resolution).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Map of recipient wallet address → the fields disclosed to that wallet.</returns>
    Task<Dictionary<string, Dictionary<string, object>>> ApplyDisclosuresAsync(
        ActionModel action,
        Dictionary<string, object> data,
        BlueprintModel blueprint,
        Dictionary<string, string> participantWallets,
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the prior-action data of a workflow instance disclosed to the caller's wallet(s), for
    /// the action being decided. Reconstructs each required prior action's caller-decryptable view from
    /// the instance's sealed transactions (identical whether the register stores payloads encrypted or
    /// in dev-mode plaintext) and clamps it to exactly the caller participant's disclosure entitlement.
    /// A field the applicant did not disclose to the caller is never returned (FR-006 / FR-010).
    /// </summary>
    /// <param name="instanceId">The workflow instance (the register is resolved from the instance).</param>
    /// <param name="actionId">The action being decided (its required prior actions are reconstructed).</param>
    /// <param name="callerWallets">The wallet address(es) owned by the calling participant.</param>
    /// <param name="delegationToken">
    /// The caller's delegation token, used to unwrap disclosure-group keys on encrypted registers.
    /// May be null/empty for dev-mode (plaintext) registers, where decryption is not required.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The disclosed prior-action view for the caller. <see cref="DisclosedActionData.RecipientResolved"/>
    /// is false (and the field maps empty) when the caller is not a disclosure recipient.
    /// </returns>
    Task<DisclosedActionData> ResolveDisclosedDataAsync(
        string instanceId,
        int actionId,
        IReadOnlyCollection<string> callerWallets,
        string? delegationToken,
        CancellationToken cancellationToken = default);
}
