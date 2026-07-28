// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.User.Presentation;

/// <summary>
/// Browser-side port of the proven server-custody presentation flow: the same steps
/// <c>demos/AIAS/rehearse.ps1</c>'s <c>Complete-SorchaWalletPresentation</c> (lines 476-625) drives
/// against the API, and the PWA's <c>Present.razor</c> server-custody path drives from a second
/// device. Lets a web citizen satisfy a SorchaWallet credential gate on THIS device — no second
/// device, no QR code — by probing whether the signed-in citizen's own wallet holds a matching
/// credential and, if so, building + signing + submitting the presentation locally (#1330).
/// </summary>
public interface ISorchaWalletLocalPresenter
{
    /// <summary>Null = no local route (no wallet, no match, cross-origin, parse failure). Never throws.</summary>
    Task<LocalPresentationCandidate?> ProbeAsync(string presentationRequestUri, CancellationToken ct = default);

    /// <summary>Builds + signs + direct_posts the presentation. Never throws — failures come back as a result.</summary>
    Task<LocalPresentResult> PresentAsync(
        LocalPresentationCandidate candidate,
        IReadOnlyCollection<string> consentedClaims,
        CancellationToken ct = default);
}

/// <summary>
/// A confirmed local-presentation route: the citizen's own wallet holds a credential matching
/// the request, and the response target is same-origin. Produced by
/// <see cref="ISorchaWalletLocalPresenter.ProbeAsync"/>, consumed by
/// <see cref="ISorchaWalletLocalPresenter.PresentAsync"/>.
/// </summary>
public sealed class LocalPresentationCandidate
{
    /// <summary>The matched credential's identifier in the citizen's wallet.</summary>
    public required string CredentialId { get; init; }

    /// <summary>The citizen's wallet address.</summary>
    public required string WalletAddress { get; init; }

    /// <summary>The requested credential type (vct URI).</summary>
    public required string Vct { get; init; }

    /// <summary>Claim names the request requires — declining any of these means declining the whole ask.</summary>
    public required IReadOnlyList<string> RequiredClaims { get; init; }

    /// <summary>Claim names the request would like but the holder may withhold.</summary>
    public required IReadOnlyList<string> OptionalClaims { get; init; }

    /// <summary>The request object's nonce — bound into the KB-JWT.</summary>
    public required string Nonce { get; init; }

    /// <summary>The verifier's declared client_id.</summary>
    public required string ClientId { get; init; }

    /// <summary>Same-origin RELATIVE path — the presenter refuses cross-origin response targets.</summary>
    public required string ResponseUri { get; init; }

    /// <summary>The DCQL credential query id the response envelope must be keyed by.</summary>
    public required string QueryId { get; init; }

    /// <summary>The request object's <c>state</c> — echoed back on direct_post.</summary>
    public required string RequestState { get; init; }

    /// <summary>JOSE alg for the KB-JWT header: "EdDSA" or "ES256", mapped from the wallet algorithm.</summary>
    public required string JoseAlgorithm { get; init; }

    /// <summary>RFC 7638 thumbprint of the holder JWK — the KB-JWT kid.</summary>
    public required string KidThumbprint { get; init; }

    /// <summary>The matched credential's issuer DID, when known.</summary>
    public string? IssuerDid { get; init; }
}

/// <summary>Outcome of <see cref="ISorchaWalletLocalPresenter.PresentAsync"/>.</summary>
public enum LocalPresentStatus
{
    /// <summary>The presentation was built, signed, and direct_posted successfully.</summary>
    Submitted,

    /// <summary>
    /// The SERVER verified the presentation and refused it (e.g. a KB-JWT signed by the wrong
    /// device key). The presentation request is CONSUMED by this outcome and cannot be retried —
    /// distinct from <see cref="Failed"/>, where nothing ever reached the verifier.
    /// </summary>
    Declined,

    /// <summary>The presentation could not be completed.</summary>
    Failed
}

/// <summary>Result of <see cref="ISorchaWalletLocalPresenter.PresentAsync"/>.</summary>
public sealed class LocalPresentResult
{
    /// <summary>The outcome.</summary>
    public required LocalPresentStatus Status { get; init; }

    /// <summary>Detail for <see cref="LocalPresentStatus.Declined"/> or <see cref="LocalPresentStatus.Failed"/>.</summary>
    public string? Detail { get; init; }

    /// <summary>The presentation was submitted successfully.</summary>
    public static LocalPresentResult Submitted() => new() { Status = LocalPresentStatus.Submitted };

    /// <summary>The server verified and refused the presentation; the request is now consumed.</summary>
    public static LocalPresentResult Declined(string detail) => new() { Status = LocalPresentStatus.Declined, Detail = detail };

    /// <summary>The presentation could not be completed.</summary>
    public static LocalPresentResult Failed(string detail) => new() { Status = LocalPresentStatus.Failed, Detail = detail };
}
