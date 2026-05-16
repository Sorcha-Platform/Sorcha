// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Discriminates the intent behind an enrol-session token. Determines the copy
/// + post-redeem destination on the wallet PWA's redeem page, and the
/// <c>mode</c> dimension on pairing telemetry. The cryptographic ceremony is
/// identical across modes.
/// </summary>
/// <remarks>
/// Feature 128 — see <c>specs/128-cold-start-onboarding/data-model.md</c>.
/// <see cref="Gated"/> is the default to preserve F126 council-page semantics
/// for callers that pre-date this discriminator.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<EnrolSessionMode>))]
public enum EnrolSessionMode
{
    /// <summary>
    /// Council-page-flavoured (F126). The PWA's redeem page honours any
    /// <c>?returnTo=</c> query parameter and routes the citizen back to the
    /// gating application after pairing.
    /// </summary>
    Gated = 0,

    /// <summary>
    /// Standalone (F128). The PWA's redeem page ignores any <c>?returnTo=</c>
    /// query parameter and routes the citizen to the wallet Home after pairing.
    /// Used by the four cold-start routes (desktop-handoff, mobileweb-handoff,
    /// pwa-takeover, cold-landing).
    /// </summary>
    Standalone = 1,
}

/// <summary>
/// Wire shape of the <c>POST /api/auth/enrol-session</c> request body.
/// Empty body (or omitted <see cref="Mode"/>) defaults to
/// <see cref="EnrolSessionMode.Gated"/> for back-compat with F126 callers.
/// </summary>
public sealed record MintEnrolSessionRequest(
    EnrolSessionMode? Mode = null);

/// <summary>
/// Wire shape of a successful <c>POST /api/auth/enrol-session</c> response.
/// Feature 126 — extended in Feature 128 with the <see cref="Mode"/> echo.
/// </summary>
public sealed record MintEnrolSessionResponse(
    string SessionToken,
    string QrUrl,
    DateTimeOffset ExpiresAt,
    EnrolSessionMode Mode);

/// <summary>
/// Wire shape of the <c>POST /api/auth/enrol-session/redeem</c> request body.
/// </summary>
public sealed record RedeemEnrolSessionRequest(string SessionToken);

/// <summary>
/// Wire shape of a successful <c>POST /api/auth/enrol-session/redeem</c> response.
/// The <see cref="DisplayName"/> and <see cref="Email"/> are surfaced by the
/// wallet PWA's confirmation dialog before the device-pairing call.
/// </summary>
/// <remarks>
/// Feature 128 — extended with <see cref="Mode"/> echo so the redeem page can
/// choose between gated copy (route back to <c>?returnTo=</c>) and standalone
/// copy (route to PWA Home).
/// </remarks>
public sealed record RedeemEnrolSessionResponse(
    string AccessToken,
    int ExpiresIn,
    string DisplayName,
    string Email,
    EnrolSessionMode Mode);

/// <summary>
/// Discriminated error codes for redeem failures. Mapped 1:1 to HTTP status:
/// <see cref="MalformedToken"/> / <see cref="InvalidSignature"/> /
/// <see cref="ScopeMismatch"/> → 400, <see cref="AlreadyUsed"/> → 409,
/// <see cref="Expired"/> → 410.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RedeemEnrolSessionErrorCode>))]
public enum RedeemEnrolSessionErrorCode
{
    MalformedToken,
    InvalidSignature,
    ScopeMismatch,
    AlreadyUsed,
    Expired,
}

/// <summary>
/// Wire shape of an unsuccessful <c>POST /api/auth/enrol-session/redeem</c> response.
/// </summary>
public sealed record RedeemEnrolSessionErrorBody(RedeemEnrolSessionErrorCode Code, string Message);
