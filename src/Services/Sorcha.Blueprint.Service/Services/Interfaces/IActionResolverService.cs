// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Service for resolving blueprints and actions
/// </summary>
public interface IActionResolverService
{
    /// <summary>
    /// Retrieves the blueprint definition an instance is PINNED to (Feature 195).
    /// </summary>
    /// <param name="blueprintId">The blueprint ID.</param>
    /// <param name="definitionTxId">
    /// The instance's pin — the id of the transaction that published the definition it runs.
    /// <b>Required, deliberately.</b>
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pinned definition, or null when this node cannot produce it.</returns>
    /// <remarks>
    /// <para>
    /// <b>Why the pin is required rather than optional.</b> This method took no pin at all. It
    /// resolved the DRAFT store first, then the latest published definition, and cached under a bare
    /// blueprint id — so the engine validated a payload, evaluated calculations and computed a route
    /// against one definition, then signed a routing decision labelled with the instance's pin. Where
    /// the two disagreed the submission returned 202 and never sealed, permanently, with no error
    /// anywhere.
    /// </para>
    /// <para>
    /// An optional parameter would preserve exactly that for every caller that omitted it — which is
    /// how the defect survived Feature 194, whose own research listed this call site as in scope.
    /// </para>
    /// <para>
    /// The draft store is <b>not</b> consulted here. Drafts are unpublished work-in-progress and must
    /// never influence a running instance; authoring surfaces resolve latest-or-draft elsewhere.
    /// </para>
    /// </remarks>
    Task<Sorcha.Blueprint.Models.Blueprint?> GetBlueprintAsync(
        string blueprintId,
        string definitionTxId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts an action definition from a blueprint
    /// </summary>
    /// <param name="blueprint">The blueprint</param>
    /// <param name="actionId">The action ID</param>
    /// <returns>The action if found, otherwise null</returns>
    Sorcha.Blueprint.Models.Action? GetActionDefinition(Sorcha.Blueprint.Models.Blueprint blueprint, string actionId);

    /// <summary>
    /// Resolves participant IDs to wallet addresses
    /// </summary>
    /// <param name="blueprint">The blueprint</param>
    /// <param name="participantIds">The participant IDs to resolve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping participant IDs to wallet addresses</returns>
    Task<Dictionary<string, string>> ResolveParticipantWalletsAsync(
        Sorcha.Blueprint.Models.Blueprint blueprint,
        IEnumerable<string> participantIds,
        CancellationToken cancellationToken = default);
}
