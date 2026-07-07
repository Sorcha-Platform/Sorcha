// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// The prior-action data of a workflow instance disclosed to the <b>calling participant</b> under the
/// register's disclosure model (Feature 176). Returned by
/// <c>GET /api/workflows/{instanceId}/actions/{actionId}/disclosures</c>.
/// </summary>
/// <remarks>
/// The wire shape is aligned to the existing <c>IBlueprintServiceClient.GetDisclosedDataAsync</c>
/// contract and its MCP consumer (<c>DisclosedDataTool</c>), which reads the <see cref="Disclosures"/>
/// list. The additional <see cref="DisclosedFields"/> / <see cref="RecipientResolved"/> members serve
/// the autonomous agent, which feeds <see cref="DisclosedFields"/> to its external checks and treats an
/// unresolved recipient / empty view as a fail-closed hold signal. Only fields the caller's participant
/// is entitled to see ever appear here — the disclosure model is never widened (FR-006 / FR-010).
/// </remarks>
public sealed record DisclosedActionData
{
    /// <summary>The workflow instance the disclosed data belongs to.</summary>
    public required string InstanceId { get; init; }

    /// <summary>The action being decided (the action whose prior-action data was requested), or null for an instance-wide query.</summary>
    public int? ActionId { get; init; }

    /// <summary>The register the instance lives on.</summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// True when the caller's wallet was resolved as a disclosure recipient and at least one prior
    /// action disclosed fields to it. False → the caller is not a recipient (or nothing was disclosed):
    /// <see cref="DisclosedFields"/> and <see cref="Disclosures"/> are empty and the consuming agent
    /// must hold rather than decide on a blank view.
    /// </summary>
    public bool RecipientResolved { get; init; }

    /// <summary>
    /// The disclosed prior-action data partitioned by the action it originated from (newest facts win
    /// on merge into <see cref="DisclosedFields"/>). This is the member the MCP <c>DisclosedDataTool</c>
    /// consumes.
    /// </summary>
    public IReadOnlyList<DisclosedActionEntry> Disclosures { get; init; } = [];

    /// <summary>
    /// The merged, disclosed prior-action payload as a single field map — the fields the calling
    /// participant is entitled to see (e.g. <c>name</c>, <c>address</c>, <c>email</c>,
    /// <c>emailVerified</c>, <c>portrait</c>). This is what the agent feeds to its checks as the
    /// previous payload. Empty when <see cref="RecipientResolved"/> is false.
    /// </summary>
    public IReadOnlyDictionary<string, object> DisclosedFields { get; init; } =
        new Dictionary<string, object>();
}

/// <summary>
/// The subset of a single prior action's payload disclosed to the calling participant (Feature 176).
/// </summary>
public sealed record DisclosedActionEntry
{
    /// <summary>The prior action id these fields originated from.</summary>
    public required int ActionId { get; init; }

    /// <summary>The prior action's human-readable title (for provenance / diagnostics).</summary>
    public required string ActionTitle { get; init; }

    /// <summary>When the prior action was disclosed/sealed, when available; otherwise null.</summary>
    public DateTimeOffset? DisclosedAt { get; init; }

    /// <summary>The fields of the prior action disclosed to the calling participant.</summary>
    public IReadOnlyDictionary<string, object> Data { get; init; } =
        new Dictionary<string, object>();
}
