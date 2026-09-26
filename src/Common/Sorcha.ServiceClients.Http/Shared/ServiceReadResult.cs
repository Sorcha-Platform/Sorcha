// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Shared;
using System.Net;

namespace Sorcha.ServiceClients.Shared;

/// <summary>
/// The outcome of a service read, carrying WHICH failure occurred rather than only that one did.
/// </summary>
/// <param name="Status">The HTTP status the service returned.</param>
/// <param name="Body">The response body on success; null when the read failed.</param>
/// <param name="Reason">
/// On failure, the service's own explanation read from its refusal body (#1673), or null when it
/// gave none. Always null on success.
/// </param>
/// <remarks>
/// Shared by every service client that reads on a caller's behalf, because the same defect has now
/// been found four times: a client collapses 403, 404 and 5xx alike into null, and its callers can
/// only say "not found". Cold-start run #5: an agent refused an instance it IS a participant on
/// (403) was told "Workflow not found" and spent the rest of the run hypothesising about a missing
/// instance. Run #6: a 403 from another organisation's user list was reported as "NotFound".
/// Same class as #1659 and #1641.
/// </remarks>
public sealed record ServiceReadResult(HttpStatusCode Status, string? Body, string? Reason = null)
{
    /// <summary>True when the read succeeded and a body is present.</summary>
    public bool IsSuccess => (int)Status is >= 200 and < 300;

    /// <summary>True when the caller is authenticated but not permitted to see this.</summary>
    public bool IsForbidden => Status == HttpStatusCode.Forbidden;

    /// <summary>True when the thing genuinely does not exist.</summary>
    public bool IsNotFound => Status == HttpStatusCode.NotFound;
}
