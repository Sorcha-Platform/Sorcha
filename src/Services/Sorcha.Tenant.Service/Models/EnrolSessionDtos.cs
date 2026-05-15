// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Wire shape of a successful <c>POST /api/auth/enrol-session</c> response.
/// Feature 126 — Sorcha Wallet enrolment inside a council application wizard.
/// </summary>
public sealed record MintEnrolSessionResponse(
    string SessionToken,
    string QrUrl,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Wire shape of the <c>POST /api/auth/enrol-session/redeem</c> request body.
/// </summary>
public sealed record RedeemEnrolSessionRequest(string SessionToken);

/// <summary>
/// Wire shape of a successful <c>POST /api/auth/enrol-session/redeem</c> response.
/// The <see cref="DisplayName"/> and <see cref="Email"/> are surfaced by the
/// wallet PWA's confirmation dialog before the device-pairing call.
/// </summary>
public sealed record RedeemEnrolSessionResponse(
    string AccessToken,
    int ExpiresIn,
    string DisplayName,
    string Email);

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
