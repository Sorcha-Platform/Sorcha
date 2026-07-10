// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.PresentationLifecycle.Abstractions;

/// <summary>
/// Return value of <see cref="IPresentationConsumer.BuildInitiationAsync"/>.
/// Carries the wire artifacts the calling page renders to the citizen — the
/// OID4VP authorization request URI is the primary artifact; alternative
/// shapes (request URI, nonce) are included when the consumer's protocol
/// surfaces them separately.
/// </summary>
/// <param name="AuthorizationRequestUri">
/// Primary OID4VP <c>openid4vp://?…</c> URI. Embedded in the QR code and the
/// same-device tap-link the council page renders.
/// </param>
/// <param name="RequestUri">
/// Optional alternative request URI shape some OID4VP profiles use.
/// </param>
/// <param name="Nonce">
/// Optional nonce echoed in the verifiable presentation. May be null when the
/// nonce is encoded inside <see cref="AuthorizationRequestUri"/>.
/// </param>
/// <remarks>
/// Feature 127 — lands the F111 "non-HAIP initiation contract extension" that
/// F111's <c>IPresentationLifecycleService.InitiateAsync</c> docstring flagged
/// as deferred. See <c>specs/127-credential-gated-service/contracts/presentation-consumer-buildinitiation.md</c>.
/// </remarks>
public sealed record ConsumerInitiationDescriptor(
    string AuthorizationRequestUri,
    string? RequestUri,
    string? Nonce,
    string? RequestObjectJwt = null);
