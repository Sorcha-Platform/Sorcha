// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;

namespace Sorcha.ServiceClients.Blueprint.Models;

/// <summary>
/// The outcome of a Blueprint read, carrying WHICH failure occurred rather than only that one did.
/// </summary>
/// <param name="Status">The HTTP status the Blueprint Service returned.</param>
/// <param name="Body">The response body on success; null when the read failed.</param>
/// <remarks>
/// Cold-start run #5: every non-success collapsed to null, so a caller refused an instance it is a
/// participant on (403) was told "Workflow not found" — and went off hypothesising that the
/// instance predated its own participant record. Reporting an authorisation failure as an absence
/// is the same defect class as #1659 and #1641.
/// </remarks>
public sealed record BlueprintReadResult(HttpStatusCode Status, string? Body)
{
    /// <summary>True when the read succeeded and a body is present.</summary>
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    /// <summary>True when the caller is authenticated but not permitted to see this.</summary>
    public bool IsForbidden => Status == HttpStatusCode.Forbidden;

    /// <summary>True when the thing genuinely does not exist.</summary>
    public bool IsNotFound => Status == HttpStatusCode.NotFound;
}
