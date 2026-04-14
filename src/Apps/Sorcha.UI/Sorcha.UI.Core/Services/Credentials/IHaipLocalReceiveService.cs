// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// Receives a HAIP-issued credential into a local Sorcha wallet by acting
/// as an OpenID4VCI holder. Feature 103 wave 13.
/// </summary>
/// <remarks>
/// <para>
/// The default HAIP receive flow delivers credentials to an external wallet
/// via QR code scan (the openid-credential-offer:// URI). This service is
/// the alternative "Receive on this device" path: the user's existing
/// Sorcha wallet acts as the holder, the JWT proof is signed by the
/// wallet's signing key via the Wallet Service, and the resulting SD-JWT
/// VC is stored in the wallet's local credential store so it surfaces in
/// the My Credentials page alongside other native credentials.
/// </para>
/// <para>
/// The credential is bound to the user's wallet public key (cnf claim),
/// so it can later be presented from the same wallet via the standard
/// Sorcha presentation flow. No throwaway holder key is created.
/// </para>
/// </remarks>
public interface IHaipLocalReceiveService
{
    /// <summary>
    /// Drives the OpenID4VCI pre-authorized-code flow against the issuer
    /// at <paramref name="credentialOfferUri"/>, signs the JWT proof with
    /// the wallet at <paramref name="walletAddress"/>, and stores the
    /// resulting SD-JWT VC in that wallet's local credential store.
    /// </summary>
    /// <param name="credentialOfferUri">
    /// The openid-credential-offer:// URI containing the pre-authorized
    /// code (the same value normally rendered as a QR code).
    /// </param>
    /// <param name="walletAddress">
    /// The bech32 address of the local Sorcha wallet that will become the
    /// credential holder. Must belong to the signed-in user.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="HaipLocalReceiveResult"/> describing the outcome — on
    /// success it contains the local credential id, type, and a flag
    /// indicating that the My Credentials view should refresh.
    /// </returns>
    Task<HaipLocalReceiveResult> ReceiveLocallyAsync(
        string credentialOfferUri,
        string walletAddress,
        CancellationToken ct = default);
}

/// <summary>
/// Outcome of a <see cref="IHaipLocalReceiveService.ReceiveLocallyAsync"/>
/// call. <see cref="Success"/> indicates whether the credential is now in
/// the local store; failures populate <see cref="ErrorMessage"/> with a
/// human-readable reason and (optionally) <see cref="ErrorCode"/> with
/// the OpenID4VCI error code from the issuer.
/// </summary>
public sealed record HaipLocalReceiveResult
{
    public required bool Success { get; init; }
    public string? CredentialId { get; init; }
    public string? CredentialType { get; init; }
    public string? IssuerUrl { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static HaipLocalReceiveResult Ok(string credentialId, string credentialType, string issuerUrl)
        => new()
        {
            Success = true,
            CredentialId = credentialId,
            CredentialType = credentialType,
            IssuerUrl = issuerUrl
        };

    public static HaipLocalReceiveResult Fail(string message, string? code = null)
        => new()
        {
            Success = false,
            ErrorCode = code,
            ErrorMessage = message
        };
}
