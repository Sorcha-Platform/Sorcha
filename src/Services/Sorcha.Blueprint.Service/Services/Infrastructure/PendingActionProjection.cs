// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;

using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Services.Infrastructure;

/// <summary>
/// The single projection of <c>(instance, actionId)</c> onto a <see cref="PendingActionSummary"/>.
/// </summary>
/// <remarks>
/// Two surfaces answer "what work is outstanding here?" — the personal list
/// (<c>GET /api/actions/pending</c>, via <c>IInstanceStore.GetPendingActionsByWalletAsync</c>) and
/// the open-participant list (<c>GET /api/actions/open-starting</c>, issue #1446). They differ only
/// in WHICH actions they select, never in how one is described, so the description lives here once.
/// A second hand-written copy would drift the way the docket projection did (#1370): both correct on
/// the day, silently divergent a field later.
/// </remarks>
internal static class PendingActionProjection
{
    /// <summary>
    /// Describes one current action of <paramref name="instance"/>. Degrades rather than throws when
    /// the blueprint or action cannot be resolved — a summary with a placeholder title is more use
    /// to a caller than a missing row.
    /// </summary>
    internal static PendingActionSummary ToSummary(
        Instance instance,
        int actionId,
        BlueprintModel? blueprint,
        IActionResolverService? actionResolver)
    {
        var actionTitle = $"Action {actionId}";
        JsonElement? dataSchema = null;

        if (blueprint != null && actionResolver != null)
        {
            var actionDef = actionResolver.GetActionDefinition(blueprint, actionId.ToString());
            if (actionDef != null)
            {
                if (!string.IsNullOrEmpty(actionDef.Title))
                {
                    actionTitle = actionDef.Title;
                }

                // Surface the first declared DataSchema so the client-side pending-actions
                // dispatcher can detect Feature 104 wave 14b claim actions via the
                // x-credential-offer schema extension. Without this the CredentialOfferSchemaResolver
                // short-circuits on a null schema and the UI falls through to a generic empty form.
                //
                // Actions may declare multiple schemas, but CredentialOfferSchemaResolver only
                // inspects the first to detect the extension. Multi-schema actions are not a wave 14b
                // use-case; revisit this projection if that assumption ever changes.
                //
                // RootElement.Clone() is required — JsonElement shares memory with its parent
                // JsonDocument and becomes invalid once the document is disposed. Without Clone()
                // this would corrupt at runtime in a hard-to-reproduce way.
                var firstSchemaDoc = actionDef.DataSchemas?.FirstOrDefault();
                if (firstSchemaDoc is not null)
                {
                    dataSchema = firstSchemaDoc.RootElement.Clone();
                }
            }
        }

        // Surface any prepopulated seed payload for this action so the UI can render it without a
        // second round trip. Feature 104 wave 14a (FR-006).
        instance.PendingActionPayloads.TryGetValue(actionId, out var seededPayload);

        return new PendingActionSummary
        {
            InstanceId = instance.Id,
            ActionId = actionId,
            ActionTitle = actionTitle,
            BlueprintId = instance.BlueprintId,
            BlueprintTitle = instance.Metadata.GetValueOrDefault("BlueprintTitle", instance.BlueprintId),
            InstanceReference = instance.Metadata.GetValueOrDefault("instanceReference", string.Empty),
            RegisterId = instance.RegisterId,
            TransactionId = instance.LastTransactionId ?? string.Empty,
            NavigationPath = $"/blueprints/{instance.BlueprintId}/instances/{instance.Id}/actions/{actionId}",
            ReceivedAt = instance.UpdatedAt,
            PrepopulatedPayload = seededPayload,
            DataSchema = dataSchema
        };
    }
}
