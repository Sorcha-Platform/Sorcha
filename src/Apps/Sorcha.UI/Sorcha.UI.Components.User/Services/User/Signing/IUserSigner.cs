// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Components.User.Services.Signing;

/// <summary>
/// Single signing seam for user-side actions (Feature 125, T013). One abstraction;
/// only the managed-mode implementation lands in v1 (<c>ManagedUserSigner</c> in
/// <c>Sorcha.Wallet.Pwa</c>). Self-custody and co-signed implementations slot in
/// behind this contract in v2 without rewriting consuming UI.
/// </summary>
/// <remarks>
/// Consumers (ConsentSheet, PresentationSubmitDialog, action-submission flows)
/// MUST NOT switch behaviour on <see cref="CustodyMode"/>. They invoke
/// <see cref="SignAsync"/> and react to <see cref="SigningResult"/>. The
/// user-visible consent UX is the same regardless of custody mode.
/// </remarks>
public interface IUserSigner
{
    /// <summary>The custody mode this signer implements.</summary>
    UserCustodyMode CustodyMode { get; }

    /// <summary>
    /// User-visible label for the active signing identity — e.g.
    /// <c>"Sign as Sarah (Personal)"</c> or
    /// <c>"Sign as Sarah (Caledonian Builders Ltd)"</c>. Surfaced by the
    /// consent moment in calling UI.
    /// </summary>
    string DisplayLabel { get; }

    /// <summary>
    /// Signs a payload under the current user / context identity.
    /// Implementations may require user consent; callers SHOULD invoke this
    /// from a UI surface that has already presented a ConsentSheet or
    /// equivalent confirmation.
    /// </summary>
    Task<SigningResult> SignAsync(SigningRequest request, CancellationToken ct = default);
}

/// <summary>How the user's holder key is custodied.</summary>
public enum UserCustodyMode
{
    /// <summary>v1 default — server-anchored holder key, browser-local device key, delegation in the middle.</summary>
    Managed,
    /// <summary>v2 — BIP39 on device, no server custody. Not implemented in v1.</summary>
    SelfCustody,
    /// <summary>v2 backlog — collector + organisation dual signature. Not implemented in v1.</summary>
    CoSigned
}

/// <summary>Logical operation classes routed through <see cref="IUserSigner"/>.</summary>
public enum SigningOperation
{
    /// <summary>OID4VP presentation response — signing a verifiable presentation.</summary>
    Presentation,
    /// <summary>Submitting an action / form submission as a Sorcha transaction.</summary>
    ActionSubmission,
    /// <summary>Renewing a holder→device delegation credential.</summary>
    DelegationRenewal,
    /// <summary>Anything else that needs a user signature; consumer-defined.</summary>
    Generic
}

/// <summary>Request handed to <see cref="IUserSigner.SignAsync"/>.</summary>
/// <param name="Operation">Logical operation class — informational; managed-mode treats all alike in v1.</param>
/// <param name="PayloadToSign">Raw bytes to sign. Caller is responsible for canonicalisation.</param>
/// <param name="AudienceClientId">Audience binding for OID4VP presentations; null for non-presentation signing.</param>
/// <param name="ActiveContextOrgId">Active context the signature is being made under; null = Personal.</param>
public sealed record SigningRequest(
    SigningOperation Operation,
    byte[] PayloadToSign,
    string? AudienceClientId = null,
    Guid? ActiveContextOrgId = null);

/// <summary>Result of <see cref="IUserSigner.SignAsync"/>.</summary>
/// <param name="Success">True iff <see cref="Signature"/> is non-null and valid for the request.</param>
/// <param name="Signature">Raw signature bytes; null on failure.</param>
/// <param name="Algorithm">JWS algorithm identifier (e.g. <c>ES256</c>); null on failure.</param>
/// <param name="ErrorCode">Machine-readable error code; non-null on failure.</param>
/// <param name="ErrorDetail">Human-readable error detail; non-null on failure.</param>
public sealed record SigningResult(
    bool Success,
    byte[]? Signature,
    string? Algorithm,
    string? ErrorCode,
    string? ErrorDetail)
{
    /// <summary>Factory for a successful result.</summary>
    public static SigningResult Ok(byte[] signature, string algorithm) =>
        new(true, signature, algorithm, null, null);

    /// <summary>Factory for a failed result.</summary>
    public static SigningResult Fail(string code, string detail) =>
        new(false, null, null, code, detail);
}
