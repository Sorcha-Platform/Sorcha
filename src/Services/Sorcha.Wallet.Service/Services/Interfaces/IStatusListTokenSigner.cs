// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Signs IETF Token Status List JWTs (<c>typ: statuslist+jwt</c>) with an organisation's VC-issuance
/// key: the same key, <c>kid</c> and <c>iss</c> its credentials carry, so a verifier resolves the
/// list's key through the org's published DID document (TODO(095), #1759).
/// </summary>
/// <remarks>
/// The token is built HERE from typed inputs. The caller never supplies header or payload bytes,
/// because a signer that signed caller bytes with the org's credential key would be a credential
/// forgery oracle. The private key never leaves the Wallet Service.
/// </remarks>
public interface IStatusListTokenSigner
{
    /// <summary>
    /// Builds and signs the status list token, or returns null when the organisation has no active
    /// VC-issuance key. There is no fallback key: an unverifiable list is worse than none.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Bits is not 1, 2, 4 or 8, or TTL is out of range.</exception>
    /// <exception cref="ArgumentException">Subject is not an absolute http(s) URI, or entries are empty.</exception>
    Task<StatusListToken?> SignAsync(StatusListTokenSignRequest request, CancellationToken ct = default);
}

/// <summary>What to put in a status list token. Everything else is decided by the signer.</summary>
/// <param name="OrganizationId">The issuing organisation, whose VC-issuance key signs the list.</param>
/// <param name="Subject">The list's URI. MUST equal the <c>status_list.uri</c> credentials carry (§5.1).</param>
/// <param name="Bits">Bits per entry: 1, 2, 4 or 8 (§4.1).</param>
/// <param name="Entries">The packed, uncompressed entry bytes (IETF LSB-first layout).</param>
/// <param name="TtlSeconds">Cache lifetime: written as <c>ttl</c>, and <c>exp = iat + ttl</c>.</param>
public sealed record StatusListTokenSignRequest(
    Guid OrganizationId,
    string Subject,
    int Bits,
    byte[] Entries,
    int TtlSeconds);

/// <summary>A signed status list token and the identity it was signed under.</summary>
/// <param name="Jwt">Compact-serialised <c>statuslist+jwt</c>.</param>
/// <param name="IssuerDid">The list's <c>iss</c> — the org's canonical issuer DID.</param>
/// <param name="Kid">The JWS <c>kid</c> — the org's current VC-issuance key.</param>
public sealed record StatusListToken(string Jwt, string IssuerDid, string Kid);
