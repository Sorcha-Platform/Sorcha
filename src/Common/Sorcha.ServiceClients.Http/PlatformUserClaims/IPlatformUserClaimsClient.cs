// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceClients.PlatformUserClaims;

/// <summary>
/// Reads the <b>current</b> value of named identity claims for a platform user from the Tenant
/// Service, rather than trusting the snapshot carried on an already-minted JWT.
/// </summary>
/// <remarks>
/// <para>
/// Issue #1264: a citizen's token was minted at signup carrying <c>email_verified: false</c>; they
/// verified nine minutes later; their application, submitted five minutes after that, was rejected
/// on the stale value. Verification updates server state but cannot rewrite an issued token, and
/// nothing re-mints it — so any decision that must reflect reality when it is made has to read live.
/// </para>
/// <para>
/// Deliberately a <b>batch</b> read keyed by claim name: one round trip however many bindings a
/// caller has to resolve, and a newly-resolvable attribute is a Tenant-side mapping entry rather than
/// a new endpoint. The vocabulary is the JWT claim vocabulary, so a caller asks for "the live value
/// of the claim I would otherwise have read off the token".
/// </para>
/// </remarks>
public interface IPlatformUserClaimsClient
{
    /// <summary>
    /// Resolves the requested claim names for <paramref name="platformUserId"/> from live server state.
    /// </summary>
    /// <param name="platformUserId">The cross-org platform user to read.</param>
    /// <param name="names">Claim names to resolve, in the JWT claim vocabulary (e.g. <c>email_verified</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The resolved name → value pairs. A name the server does not support is <b>absent</b> from the
    /// result, so the caller can fail closed on it visibly rather than acting on a guess.
    /// </returns>
    /// <exception cref="PlatformUserClaimsUnavailableException">
    /// The live value could not be determined — the user is unknown, or the Tenant Service could not
    /// be reached. Callers must NOT substitute a token value or a default: doing either reintroduces
    /// exactly the staleness this interface exists to remove. Fail the operation instead.
    /// </exception>
    Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        Guid platformUserId,
        IReadOnlyCollection<string> names,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Thrown when live claim values cannot be resolved.
/// </summary>
/// <remarks>
/// This is deliberately an exception rather than a null/empty return. Issue #1264 established that
/// silently signing a defaulted <c>false</c> writes an irreversible wrongful rejection onto the
/// ledger, whereas a failed submission is recoverable — the citizen simply retries. A caller that
/// swallows this and carries on has reintroduced the bug.
/// </remarks>
public sealed class PlatformUserClaimsUnavailableException : Exception
{
    /// <summary>Initialises a new instance with a message.</summary>
    public PlatformUserClaimsUnavailableException(string message) : base(message) { }

    /// <summary>Initialises a new instance with a message and inner cause.</summary>
    public PlatformUserClaimsUnavailableException(string message, Exception innerException)
        : base(message, innerException) { }
}
