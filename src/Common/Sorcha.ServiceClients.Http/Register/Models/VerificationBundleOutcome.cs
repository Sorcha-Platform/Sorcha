// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.ServiceClients.Register.Models;

/// <summary>
/// Why a verification-bundle export was refused, as the Register Service said it (#1680).
/// </summary>
/// <param name="StatusCode">The HTTP status the Register Service returned.</param>
/// <param name="Reason">The server's own explanation from the response body, or null when it gave none.</param>
/// <remarks>
/// #1680. <c>GetVerificationBundleAsync</c> used to collapse every non-success response —
/// including a genuine <c>404</c> and a <c>409</c> the server explained in its body — into a
/// bare <c>null</c>, which the MCP tool then reported as "NotFound … not yet sealed" regardless
/// of what actually happened. An agent cannot distinguish "try again later" from "this will
/// never succeed" from a fabricated explanation. Carry the real status and reason through instead.
/// </remarks>
public sealed record VerificationBundleRefusal(int StatusCode, string? Reason);

/// <summary>
/// Discriminated outcome of a verification-bundle export (#1680). Either the export succeeded
/// (<see cref="Bundle"/> set) or the Register Service refused it (<see cref="Refusal"/> set,
/// carrying the actual HTTP status and the server's own reason).
/// </summary>
public sealed record VerificationBundleOutcome
{
    /// <summary>The exported bundle, or null when the request was refused.</summary>
    public VerificationBundle? Bundle { get; init; }

    /// <summary>The service's refusal, or null when it succeeded.</summary>
    public VerificationBundleRefusal? Refusal { get; init; }
}
