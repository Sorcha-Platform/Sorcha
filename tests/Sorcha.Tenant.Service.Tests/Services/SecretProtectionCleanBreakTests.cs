// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Feature 146 clean-break guard (SC-006): the legacy reversible / weak secret routines must not
/// reappear. The TOTP secret was reversible Base64 (<c>v1:</c>), the OIDC client secret was a
/// one-way SHA-256 hash, and the login-token key was a per-process random — all replaced by the
/// AES-256-GCM <c>ISecretProtectionProvider</c> + the JWT-derived login-token key. This asserts the
/// deleted methods stay gone (reflection over the production types).
/// </summary>
public class SecretProtectionCleanBreakTests
{
    private const BindingFlags AnyMethod =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    [Theory]
    [InlineData(typeof(TotpService), "EncryptSecret")]        // was: v1: reversible Base64
    [InlineData(typeof(TotpService), "DecryptSecret")]
    [InlineData(typeof(TotpService), "GenerateStableKey")]    // was: per-process random login-token key
    [InlineData(typeof(IdpConfigurationService), "EncryptSecret")]  // was: SHA-256 hash
    [InlineData(typeof(IdpConfigurationService), "DecryptSecret")]  // was: hex-of-hash (broken exchange)
    public void LegacySecretMethods_AreRemoved(Type type, string methodName)
    {
        type.GetMethod(methodName, AnyMethod)
            .Should().BeNull(
                $"{type.Name}.{methodName} was the pre-Feature-146 reversible/weak path and must stay deleted");
    }
}
