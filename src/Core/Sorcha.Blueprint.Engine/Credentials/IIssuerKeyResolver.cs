// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// The issuer key material resolved from a raw SD-JWT VC (feature 135). Carries everything the
/// format handler needs to verify the issuer signature and seed the <see cref="IssuerContext"/>:
/// the public key bytes, the JOSE algorithm, the matched signing key id, and the leaf-first
/// X.509 chain when the credential embeds one.
/// </summary>
public class IssuerKeyResolution
{
    /// <summary>
    /// The issuer public key bytes in the form expected by <c>ISdJwtService.Verify*</c>:
    /// SubjectPublicKeyInfo (DER) for EC/RSA, raw 32-byte public key for Ed25519.
    /// </summary>
    public byte[] PublicKey { get; set; } = [];

    /// <summary>The JOSE signing algorithm (e.g. "ES256", "EdDSA", "RS256").</summary>
    public string Algorithm { get; set; } = string.Empty;

    /// <summary>
    /// The identifier of the key that produced the signature (the JWS <c>kid</c> / matched
    /// verification-method id), when known. Gates the register source's assertionMethod check.
    /// </summary>
    public string? SigningKeyId { get; set; }

    /// <summary>The leaf-first X.509 certificate chain (DER) the credential carried, when present.</summary>
    public IReadOnlyList<byte[]>? X5cChain { get; set; }
}

/// <summary>
/// Engine-local seam resolving the issuer public key for a raw SD-JWT VC (feature 135).
/// Service-layer adapters implement the full x5c → DID → embedded-jwk resolution (a port of the
/// HAIP verifier's key resolution); the engine ships an in-memory pinned variant so verification
/// and offline-pinned re-evaluation run without network access — mirroring the
/// <see cref="IRevocationChecker"/> / <see cref="IIssuerDirectory"/> pattern. Returning null means
/// the issuer key could not be resolved; the format handler then fails closed (signature unverified).
/// </summary>
public interface IIssuerKeyResolver
{
    /// <summary>
    /// Resolves the issuer key for the issuer JWT of <paramref name="rawSdJwt"/> (compact SD-JWT,
    /// disclosures and any KB-JWT tolerated). Returns null when no key can be resolved.
    /// </summary>
    Task<IssuerKeyResolution?> ResolveAsync(string rawSdJwt, CancellationToken cancellationToken = default);
}
