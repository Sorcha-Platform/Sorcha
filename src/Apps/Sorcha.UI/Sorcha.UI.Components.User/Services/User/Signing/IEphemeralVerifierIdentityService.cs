// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Components.User.Services.Signing;

/// <summary>
/// Mints a per-verification-session ephemeral verifier identity used to bind
/// the audience of an OID4VP presentation request (Feature 125, T014 / R-006).
/// </summary>
/// <remarks>
/// <para>
/// The citizen verifier (Margaret at her door, in the doorstep beat) isn't a
/// registered platform consumer — no centralised identity makes sense at the
/// protocol level. OID4VP still requires a verifier <c>client_id</c> for
/// presentation request audience binding; this service mints a fresh EC P-256
/// key per verification session, exposes its RFC 7638 thumbprint as the
/// <c>client_id</c>, and zeroises the private key when the session disposes.
/// </para>
/// <para>
/// The implementation (<c>EphemeralVerifierIdentityService</c> in
/// <c>Sorcha.Wallet.Pwa</c>, T035) bridges to WebCrypto via the existing
/// <c>webcrypto-bridge.js</c>. v1 ships the interface; PR-C (US1 / doorstep
/// verification) fills out the implementation.
/// </para>
/// </remarks>
public interface IEphemeralVerifierIdentityService
{
    /// <summary>
    /// Begin a new verification session. Generates a fresh EC P-256 key and
    /// returns the verifier identity. The caller MUST dispose the returned
    /// identity at the end of the session to zeroise the private key.
    /// </summary>
    Task<EphemeralVerifierIdentity> BeginSessionAsync(CancellationToken ct = default);
}

/// <summary>
/// One verification session's ephemeral verifier identity. Carries the public
/// JWK and its RFC 7638 thumbprint (which becomes the OID4VP
/// <c>client_id</c>). Disposing zeroises the private key material.
/// </summary>
public abstract class EphemeralVerifierIdentity : IAsyncDisposable, IDisposable
{
    /// <summary>RFC 7638 thumbprint of <see cref="PublicJwk"/>; the OID4VP <c>client_id</c>.</summary>
    public string ClientId { get; }

    /// <summary>Serialised public JWK for the session — safe to embed in presentation requests.</summary>
    public string PublicJwk { get; }

    /// <summary>Initialise a new identity.</summary>
    protected EphemeralVerifierIdentity(string clientId, string publicJwk)
    {
        ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        PublicJwk = publicJwk ?? throw new ArgumentNullException(nameof(publicJwk));
    }

    /// <inheritdoc />
    public abstract ValueTask DisposeAsync();

    /// <inheritdoc />
    public void Dispose()
    {
        // Default synchronous Dispose drains the async disposal.
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}
