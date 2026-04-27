// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models.Requests;

/// <summary>
/// Request body for <c>POST /api/auth/password/set</c> and <c>/change</c> (Feature 116 US3).
/// The password is validated by <c>IPasswordPolicyService</c> before hashing — no
/// complexity rules other than the platform default (NIST SP 800-63B: minimum 12
/// characters + breach check).
/// </summary>
/// <param name="Password">The new password in plaintext. Hashed with BCrypt before storage.</param>
public sealed record PasswordRequest(
    [property: JsonPropertyName("password")] string Password);
