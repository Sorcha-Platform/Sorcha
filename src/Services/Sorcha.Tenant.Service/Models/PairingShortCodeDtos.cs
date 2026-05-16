// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Wire shape of the <c>POST /api/auth/enrol-session/short-code</c> request body.
/// Body is optional — omitted body is treated as default
/// <see cref="PairingShortCodeRoute.DesktopHandoff"/>.
/// </summary>
/// <remarks>
/// Feature 128 — short codes are a human-typeable transport for the same
/// underlying <see cref="EnrolSessionMode.Standalone"/> enrol-session token,
/// used by the PWA pairing-takeover's "Already started on another device?"
/// sub-affordance and the mobile-web install variant's fallback path.
/// </remarks>
public sealed record MintPairingShortCodeRequest(
    PairingShortCodeRoute? Route = null);

/// <summary>
/// Telemetry-only dimension distinguishing which F128 route minted a short
/// code. Used to drive the SC-005 per-route mix dashboard and the SC-006
/// short-code-fallback-rate calculation on the mobile-web variant.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PairingShortCodeRoute>))]
public enum PairingShortCodeRoute
{
    DesktopHandoff = 0,
    MobilewebHandoff = 1,
    PwaTakeover = 2,
    ColdLanding = 3,
}

/// <summary>
/// Wire shape of a successful <c>POST /api/auth/enrol-session/short-code</c>
/// response. The <see cref="Code"/> is a 6-digit numeric string the citizen
/// reads from one surface and types into another.
/// </summary>
public sealed record MintPairingShortCodeResponse(
    string Code,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Wire shape of the
/// <c>POST /api/auth/enrol-session/redeem-short-code</c> request body.
/// </summary>
public sealed record RedeemPairingShortCodeRequest(string Code);

/// <summary>
/// Discriminated error codes for short-code redeem failures. The
/// <see cref="MalformedCode"/> / <see cref="ExpiredCode"/> /
/// <see cref="AlreadyUsedCode"/> / <see cref="RateLimited"/> values mirror
/// the underlying enrol-session redeem semantics with code-specific copy.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RedeemPairingShortCodeErrorCode>))]
public enum RedeemPairingShortCodeErrorCode
{
    MalformedCode,
    ExpiredCode,
    AlreadyUsedCode,
    RateLimited,
}

/// <summary>
/// Wire shape of an unsuccessful redeem-short-code response.
/// </summary>
public sealed record RedeemPairingShortCodeErrorBody(
    RedeemPairingShortCodeErrorCode Code,
    string Message);
