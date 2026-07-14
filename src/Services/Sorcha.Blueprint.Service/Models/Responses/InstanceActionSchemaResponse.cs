// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Service.Models.Responses;

/// <summary>
/// Consumer-readable schema for a single action within a blueprint instance (P0 fix,
/// <c>fix/pwa-p0-claim-and-camera</c>). Backs <c>GET /api/instances/{instanceId}/actions/{actionId}</c> —
/// the read the Wallet PWA uses to render the form for the action a citizen is currently completing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately narrow.</b> This is NOT the full <see cref="Sorcha.Blueprint.Models.Action"/> model
/// and NOT a wrapper around the blueprint. It carries only the fields
/// <c>Sorcha.UI.Components.User</c>'s <c>SorchaFormRenderer</c> actually reads to render and validate a
/// form: the layout (<see cref="Form"/>), the JSON Schemas (<see cref="DataSchemas"/>), calculated-field
/// definitions (<see cref="Calculations"/>), and the credential gate for this one action
/// (<see cref="CredentialRequirements"/> / <see cref="CredentialIssuanceConfig"/>).
/// </para>
/// <para>
/// <b>Excluded on purpose</b> — routing/workflow-internal fields that would leak the blueprint's
/// authoring structure to a citizen filling in one action: <c>Routes</c> and <c>Condition</c> (where the
/// instance goes next and under what logic), <c>Participants</c> / <c>Target</c> /
/// <c>AdditionalRecipients</c> (who else is involved), <c>RequiredPriorActions</c> (state-reconstruction
/// internals), <c>RejectionConfig</c> (reveals the rejection routing target — the PWA does not wire up
/// <c>OnReject</c> today; add it here if/when it does), <c>Disclosures</c> / <c>Sender</c> (only consumed
/// by <c>SorchaFormRenderer</c> when the caller is NOT the action's sender — this endpoint's caller always
/// is, so they are dead weight that would otherwise reveal downstream participant roles), and
/// <c>PreviousTxId</c> / <c>BlueprintId</c> / <c>JsonLdType</c> / <c>Published</c> /
/// <c>AdditionalProperties</c> / <c>Notification</c> (authoring/bookkeeping metadata the renderer never
/// reads).
/// </para>
/// </remarks>
public sealed record InstanceActionSchemaResponse
{
    /// <summary>The action's sequence number within the blueprint.</summary>
    public required int ActionId { get; init; }

    /// <summary>Human-readable title for this action (e.g. "Apply", "Claim credential").</summary>
    public required string Title { get; init; }

    /// <summary>The UI form layout (JSON Forms-style control tree). Null falls back to schema auto-generation.</summary>
    public Control? Form { get; init; }

    /// <summary>JSON Schemas describing the data this action collects.</summary>
    public IEnumerable<JsonDocument>? DataSchemas { get; init; }

    /// <summary>User-defined calculations (JSON Logic) performed on submitted data.</summary>
    public Dictionary<string, JsonNode>? Calculations { get; init; }

    /// <summary>Credential requirements that must be satisfied before this action can be executed.</summary>
    public IEnumerable<CredentialRequirement>? CredentialRequirements { get; init; }

    /// <summary>Configuration for a credential minted when this action executes (drives the review UI).</summary>
    public CredentialIssuanceConfig? CredentialIssuanceConfig { get; init; }
}
