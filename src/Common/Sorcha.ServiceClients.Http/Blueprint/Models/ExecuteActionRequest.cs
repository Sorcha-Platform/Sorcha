// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.ServiceClients.Blueprint.Models;

/// <summary>
/// Body for <c>POST /api/instances/{instanceId}/actions/{actionId}/execute</c>. Mirrors the Blueprint
/// Service's <c>ActionSubmissionRequest</c> on the wire (#1658): the action data rides inside
/// <see cref="PayloadData"/>, beside the ids and the sender wallet the endpoint also requires. Posting the
/// data as the whole body binds none of those and is refused with a 400 before any logic runs.
/// </summary>
/// <remarks>
/// <c>ActionSubmissionRequestWireContractTests</c> in the Blueprint Service tests binds bytes produced by
/// this type into the server's request and runs the endpoint's validation filter over them.
/// </remarks>
public sealed record ExecuteActionRequest
{
    /// <summary>The blueprint the instance runs.</summary>
    public required string BlueprintId { get; init; }

    /// <summary>The action id within the blueprint.</summary>
    public required string ActionId { get; init; }

    /// <summary>The workflow instance id.</summary>
    public string? InstanceId { get; init; }

    /// <summary>The caller's wallet that signs the submission.</summary>
    public required string SenderWallet { get; init; }

    /// <summary>The register the instance lives on.</summary>
    public required string RegisterAddress { get; init; }

    /// <summary>The action data: a JSON object, sent unchanged.</summary>
    public required JsonElement PayloadData { get; init; }
}
