// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Orchestrates SD-JWT VC credential minting for HAIP-path issuance.
/// Calls into:
/// - ISdJwtService for token creation (cnf binding, nested disclosure)
/// - Wallet Service (via service client) for signing key retrieval
/// - Tenant Service (via service client) for x5c chain
/// - Blueprint Service for status list allocation
///
/// The minter holds no signing keys — all cryptographic operations are
/// delegated to the Wallet Service.
/// </summary>
public class HaipCredentialMinter
{
    private readonly ISdJwtService _sdJwtService;
    private readonly ILogger<HaipCredentialMinter> _logger;

    public HaipCredentialMinter(
        ISdJwtService sdJwtService,
        ILogger<HaipCredentialMinter> logger)
    {
        _sdJwtService = sdJwtService ?? throw new ArgumentNullException(nameof(sdJwtService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Mints an SD-JWT VC credential with holder key binding (cnf).
    /// </summary>
    /// <param name="issuerDid">Issuer DID (e.g., "did:sorcha:org:ws1q...").</param>
    /// <param name="holderJwk">Holder's public key in JWK form (from JWT proof header).</param>
    /// <param name="credentialType">Credential type identifier.</param>
    /// <param name="claims">Credential claims.</param>
    /// <param name="disclosablePaths">Claim names/paths that support selective disclosure.</param>
    /// <param name="signingKey">Issuer's private signing key bytes.</param>
    /// <param name="algorithm">Signing algorithm (e.g., "ES256").</param>
    /// <param name="expiresAt">Optional credential expiry.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The serialized SD-JWT VC token string.</returns>
    public async Task<string> MintCredentialAsync(
        string issuerDid,
        JsonElement holderJwk,
        string credentialType,
        Dictionary<string, object> claims,
        IEnumerable<string>? disclosablePaths,
        byte[] signingKey,
        string algorithm,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default,
        string? kid = null,
        IReadOnlyList<byte[]>? x5cChain = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialType);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(signingKey);

        var subject = $"urn:credential:{credentialType}:{Guid.NewGuid():N}";

        _logger.LogInformation(
            "Minting HAIP credential: type={Type}, issuer={Issuer}, disclosables={Count}, x5c={X5c}",
            credentialType, issuerDid, disclosablePaths?.Count() ?? 0, x5cChain is { Count: > 0 });

        var token = await _sdJwtService.CreateTokenAsync(
            claims,
            disclosablePaths,
            issuerDid,
            subject,
            signingKey,
            algorithm,
            holderJwk,
            expiresAt,
            ct,
            x5cChain: x5cChain,
            kid: kid);

        _logger.LogInformation(
            "Minted HAIP credential: {Disclosures} disclosures, token length {Length}",
            token.Disclosures.Count, token.RawToken.Length);

        return token.RawToken;
    }

    /// <summary>
    /// External-signer overload (Feature 120 HAIP kid-swap) — delegates signing to a
    /// caller-supplied callback that produces the signature without HAIP holding the
    /// private key. Used to sign credentials with the org's issuance key via the
    /// wallet service's sign-on-behalf endpoint.
    /// </summary>
    public async Task<string> MintCredentialWithExternalSignerAsync(
        string issuerDid,
        JsonElement holderJwk,
        string credentialType,
        Dictionary<string, object> claims,
        IEnumerable<string>? disclosablePaths,
        Func<byte[], CancellationToken, Task<byte[]>> externalSigner,
        string algorithm,
        string kid,
        DateTimeOffset? expiresAt = null,
        CancellationToken ct = default,
        IReadOnlyList<byte[]>? x5cChain = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialType);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(externalSigner);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);

        var subject = $"urn:credential:{credentialType}:{Guid.NewGuid():N}";

        _logger.LogInformation(
            "Minting HAIP credential via sign-on-behalf: type={Type}, issuer={Issuer}, kid={Kid}, disclosables={Count}, x5c={X5c}",
            credentialType, issuerDid, kid, disclosablePaths?.Count() ?? 0, x5cChain is { Count: > 0 });

        var token = await _sdJwtService.CreateTokenAsync(
            claims,
            disclosablePaths,
            issuerDid,
            subject,
            algorithm,
            externalSigner,
            holderJwk,
            kid,
            expiresAt,
            ct,
            x5cChain: x5cChain);

        _logger.LogInformation(
            "Minted HAIP credential (sign-on-behalf): {Disclosures} disclosures, token length {Length}",
            token.Disclosures.Count, token.RawToken.Length);

        return token.RawToken;
    }
}
