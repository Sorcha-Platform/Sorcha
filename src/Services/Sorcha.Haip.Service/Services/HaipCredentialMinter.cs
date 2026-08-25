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
    /// <summary>The SD-JWT VC type claim — the credential's sole type identifier (§3.2.2.1).</summary>
    private const string VctClaim = "vct";

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
        var typedClaims = WithVct(claims, credentialType);
        var safeDisclosables = ResolveDisclosables(claims, disclosablePaths);

        _logger.LogInformation(
            "Minting HAIP credential: type={Type}, issuer={Issuer}, disclosables={Count}, x5c={X5c}",
            credentialType, issuerDid, safeDisclosables.Count, x5cChain is { Count: > 0 });

        var token = await _sdJwtService.CreateTokenAsync(
            typedClaims,
            safeDisclosables,
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
        var typedClaims = WithVct(claims, credentialType);
        var safeDisclosables = ResolveDisclosables(claims, disclosablePaths);

        _logger.LogInformation(
            "Minting HAIP credential via sign-on-behalf: type={Type}, issuer={Issuer}, kid={Kid}, disclosables={Count}, x5c={X5c}",
            credentialType, issuerDid, kid, safeDisclosables.Count, x5cChain is { Count: > 0 });

        var token = await _sdJwtService.CreateTokenAsync(
            typedClaims,
            safeDisclosables,
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
    /// <summary>
    /// Returns <paramref name="claims"/> carrying the SD-JWT VC <c>vct</c> type claim (#1540).
    /// </summary>
    /// <remarks>
    /// <para>SD-JWT VC (draft-ietf-oauth-sd-jwt-vc §3.2.2.1) makes <c>vct</c> the credential's SOLE type
    /// claim and REQUIRES it — there is no <c>type</c> claim in the profile. HAIP minted credentials
    /// without one: <c>credentialType</c> reached this class and was spent on the <c>sub</c> string and a
    /// log line, so the issued credential carried no type identifier at all and no conformant verifier
    /// could match it to a requested type. Sorcha's own verifier refused it — correctly — with
    /// <c>vct '(none)' is not among the requested type(s)</c>.</para>
    ///
    /// <para>The offer is authoritative: the token request was already validated against
    /// <c>offer.CredentialType</c>, so a <c>vct</c> inherited from the claim set that disagrees with it
    /// would mean the credential's declared type contradicts the offer that authorised it. That is
    /// overridden, and logged rather than done silently.</para>
    ///
    /// <para>The caller's dictionary is never mutated — <c>offer.Claims</c> belongs to a stored offer that
    /// may be read again.</para>
    /// </remarks>
    private Dictionary<string, object> WithVct(Dictionary<string, object> claims, string credentialType)
    {
        var copy = new Dictionary<string, object>(claims);

        if (copy.TryGetValue(VctClaim, out var existing)
            && existing is string existingVct
            && !string.Equals(existingVct, credentialType, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Claim set declared vct '{ExistingVct}' but the offer authorised type '{CredentialType}' — " +
                "using the offer's type, which is what the token request was validated against",
                existingVct, credentialType);
        }

        copy[VctClaim] = credentialType;
        return copy;
    }

    /// <summary>
    /// Resolves the selective-disclosure set with <c>vct</c> excluded (#1540).
    /// </summary>
    /// <remarks>
    /// <para>A holder who could withhold the type identifier would present a credential indistinguishable
    /// from the untyped ones this fix exists to stop — <c>vct</c> identifies the credential and must always
    /// travel in the clear.</para>
    ///
    /// <para><b>A null disclosure set does not mean "disclose nothing".</b>
    /// <c>SdJwtService.CreateTokenCoreAsync</c> reads
    /// <c>disclosableClaims?.ToList() ?? claims.Keys.ToList()</c> — passing null makes EVERY claim
    /// selectively disclosable, <c>vct</c> among them, which would put the type identifier back in a
    /// disclosure and reproduce the defect for any caller that omits the set. So null is expanded here to
    /// the caller's own claim names before <c>vct</c> is removed, rather than forwarded.</para>
    /// </remarks>
    private static List<string> ResolveDisclosables(
        Dictionary<string, object> claims, IEnumerable<string>? disclosablePaths)
    {
        var names = disclosablePaths?.ToList() ?? [.. claims.Keys];

        return [.. names.Where(p => !string.Equals(p, VctClaim, StringComparison.Ordinal))];
    }
}
