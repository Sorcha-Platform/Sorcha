// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Models.Requests;

/// <summary>
/// Request body for <c>POST /api/auth/social/link/confirm</c>.
/// The challenge proof is presented via the <c>X-Auth-Challenge</c> header (not in this body).
/// </summary>
/// <param name="LinkPendingToken">Opaque link-pending token returned by the social callback LinkRequired branch.</param>
public record LinkConfirmRequest(string LinkPendingToken);

/// <summary>
/// Request body for <c>POST /api/auth/social/link/challenge/initiate</c>.
/// </summary>
/// <param name="LinkPendingToken">Opaque link-pending token identifying the target account.</param>
/// <param name="PreferredMethod">Optional preferred challenge method. When null the ladder selects the strongest available.</param>
public record SocialLinkChallengeInitiateRequest(
    string LinkPendingToken,
    ChallengeMethod? PreferredMethod);

/// <summary>
/// Request body for <c>POST /api/auth/social/link/challenge/verify</c>.
/// </summary>
/// <param name="LinkPendingToken">Opaque link-pending token identifying the target account.</param>
/// <param name="Method">The challenge method being answered.</param>
/// <param name="Proof">Method-specific proof payload (TOTP code, password, WebAuthn assertion, etc.).</param>
public record SocialLinkChallengeVerifyRequest(
    string LinkPendingToken,
    ChallengeMethod Method,
    JsonElement Proof);
