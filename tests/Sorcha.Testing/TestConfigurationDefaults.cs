// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Testing;

/// <summary>
/// Shared configuration snippets for integration test factories.
/// </summary>
public static class TestConfigurationDefaults
{
    /// <summary>
    /// The symmetric signing key used by <see cref="TestAuthHandler"/> when it
    /// falls through to real JWT parsing. Must be at least 32 chars.
    /// </summary>
    public const string JwtSigningKey =
        "test-signing-key-for-integration-tests-minimum-32-characters-required";

    /// <summary>
    /// JWT settings applied by every Sorcha integration factory. Authentication
    /// is validated loosely so tests can issue tokens without configuring clocks.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Jwt { get; } = new Dictionary<string, string?>
    {
        ["JwtSettings:Issuer"] = "https://test.sorcha.io",
        ["JwtSettings:Audiences:0"] = "https://test-api.sorcha.io",
        ["JwtSettings:SigningKey"] = JwtSigningKey,
        ["JwtSettings:AccessTokenLifetimeMinutes"] = "60",
        ["JwtSettings:RefreshTokenLifetimeHours"] = "24",
        ["JwtSettings:ServiceTokenLifetimeHours"] = "8",
        ["JwtSettings:ClockSkewMinutes"] = "5",
        ["JwtSettings:ValidateIssuer"] = "false",
        ["JwtSettings:ValidateAudience"] = "false",
        ["JwtSettings:ValidateIssuerSigningKey"] = "false",
        ["JwtSettings:ValidateLifetime"] = "false",
    };
}
