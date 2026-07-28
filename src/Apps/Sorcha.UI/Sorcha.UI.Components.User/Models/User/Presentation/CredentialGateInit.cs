// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.User.Presentation;

/// <summary>
/// Feature 127 — initialisation data the consumer page passes to
/// <c>PresentationRequestCard</c> after submitting the credential-gated
/// starting action. The page owns the action-submission HTTP call (so it can
/// carry the page's own auth, retry policy, and error UX); the gate
/// component owns the subsequent QR + signal + claims-fetch + autofill flow.
/// </summary>
/// <param name="PresentationRequestId">
/// Identifier the F111 lifecycle service minted for the attempt. Used by the
/// card to subscribe to the SignalR group and to address the claims-fetch.
/// </param>
/// <param name="AuthorizationRequestUri">
/// OID4VP <c>openid4vp://…</c> URI the card renders via HybridQrAffordance.
/// </param>
/// <param name="ClaimsFetchToken">
/// Single-use token bound to <paramref name="PresentationRequestId"/>; the
/// card presents this on the F127 claims-fetch endpoint after the outcome
/// arrives. Null only when the consumer doesn't produce council-page-readable
/// claims (e.g. HAIP); the gate treats that case as "no autofill possible"
/// and surfaces the decline state.
/// </param>
public sealed record CredentialGateInit(
    Guid PresentationRequestId,
    string AuthorizationRequestUri,
    string? ClaimsFetchToken);
