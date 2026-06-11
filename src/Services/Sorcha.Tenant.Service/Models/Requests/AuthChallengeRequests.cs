// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Models.Requests;

/// <summary>
/// Request to begin a re-authentication challenge.
/// </summary>
/// <param name="ScopedOperation">Operation the resulting token will authorise.</param>
/// <param name="PreferredMethod">
/// Optional override of the default ladder selection. Server still validates
/// the choice is enrolled.
/// </param>
/// <param name="TargetMethodKind">
/// The sign-in method the operation targets, when ambiguous. Required for
/// <see cref="ScopedOperation.RemoveAuthMethod"/> (passkey-revoke vs social-unlink)
/// so the floor rule computes the correct required proof tier (Feature 150). A null
/// target fails safe to the strongest tier.
/// </param>
public sealed record ChallengeInitiateRequest(
    ScopedOperation ScopedOperation,
    ChallengeMethod? PreferredMethod,
    AuthMethodKind? TargetMethodKind = null);

/// <summary>
/// Server response after a successful initiate. The dialog uses
/// <see cref="Method"/> to decide which proof input to render and
/// <see cref="Payload"/> for any method-specific data.
/// </summary>
/// <param name="Method">Method the user is expected to satisfy.</param>
/// <param name="Payload">Method-specific payload (e.g. WebAuthn assertion options); null for TOTP/Password.</param>
public sealed record ChallengeInitiateResponse(
    ChallengeMethod Method,
    JsonElement? Payload);

/// <summary>
/// Submit the proof produced by the user.
/// </summary>
/// <param name="Method">Method the proof corresponds to.</param>
/// <param name="ScopedOperation">Operation the resulting token must be bound to.</param>
/// <param name="Proof">
/// Method-specific proof body. <c>{ "code": "123456" }</c> for TOTP,
/// <c>{ "password": "..." }</c> for Password, WebAuthn assertion JSON for
/// Passkey, OAuth code/state object for ReOAuth.
/// </param>
/// <param name="TargetMethodKind">
/// The sign-in method the operation targets, when ambiguous (see
/// <see cref="ChallengeInitiateRequest.TargetMethodKind"/>). The floor rule is
/// re-checked on verify; a proof tier below the required tier yields
/// <c>403 proof_tier_insufficient</c> (Feature 150).
/// </param>
public sealed record ChallengeVerifyRequest(
    ChallengeMethod Method,
    ScopedOperation ScopedOperation,
    JsonElement Proof,
    AuthMethodKind? TargetMethodKind = null);

/// <summary>
/// Server response after a successful verify. The raw token is returned only
/// here; subsequent mutation calls present it in the <c>X-Auth-Challenge</c> header.
/// </summary>
/// <param name="Token">Opaque single-use token. Always begins <c>ch_</c>.</param>
/// <param name="ExpiresIn">Seconds until <c>ExpiresAt</c>. Always 300.</param>
public sealed record ChallengeVerifyResponse(
    string Token,
    int ExpiresIn);
