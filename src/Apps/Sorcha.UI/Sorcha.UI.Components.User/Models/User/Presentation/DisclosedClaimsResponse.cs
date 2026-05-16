// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.User.Presentation;

/// <summary>
/// Client-side mirror of <c>Sorcha.Blueprint.Service.Endpoints.DisclosedClaimsResponse</c>
/// — what <c>GET /api/presentations/{id}/disclosed-claims</c> returns. Lives
/// in this library so consumer pages (in <c>samples/</c> or any third-party
/// council deployment) can deserialise without referencing the platform-side
/// service assembly.
/// </summary>
/// <param name="PresentationRequestId">Echoed back for the council-page state machine.</param>
/// <param name="Status"><c>"success"</c> when <see cref="Claims"/> is populated; <c>"pending"</c> when the outcome hasn't been written yet.</param>
/// <param name="Claims">Disclosed claims in plaintext, filtered to the gate's required claims. Populated only on success.</param>
/// <param name="SubjectDisplayName">Convenience field — <c>"givenName familyName"</c> when both claims are present.</param>
public sealed record DisclosedClaimsResponse
{
    [JsonPropertyName("presentationRequestId")]
    public required Guid PresentationRequestId { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("claims")]
    public IReadOnlyDictionary<string, object?>? Claims { get; init; }

    [JsonPropertyName("subjectDisplayName")]
    public string? SubjectDisplayName { get; init; }
}
