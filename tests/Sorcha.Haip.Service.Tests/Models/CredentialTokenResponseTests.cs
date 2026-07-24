// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json.Serialization;

using FluentAssertions;
using Xunit;

using Sorcha.Haip.Service.Models;

namespace Sorcha.Haip.Service.Tests.Models;

/// <summary>
/// Guards the OpenID4VCI token-endpoint response as a surface distinct from the platform auth
/// token response.
/// </summary>
/// <remarks>
/// This type was called <c>TokenResponse</c> until DRIFT-004 — the same name as the Tenant
/// Service's RFC 6749 platform token response, which is how a grep for "TokenResponse" lands on
/// the wrong one. They are genuinely different protocol surfaces: this one carries
/// <c>c_nonce</c> / <c>c_nonce_expires_in</c> for credential-request proof binding, and never a
/// refresh token. Renaming disambiguates without conflating; merging them would have been worse
/// than the collision.
/// </remarks>
public sealed class CredentialTokenResponseTests
{
    [Fact]
    public void CarriesTheCredentialNonceThatDefinesThisSurface()
    {
        typeof(CredentialTokenResponse).GetProperty(nameof(CredentialTokenResponse.CNonce))
            .Should().NotBeNull("c_nonce is what makes this an OpenID4VCI token response");
    }

    [Fact]
    public void DoesNotCarryARefreshToken()
    {
        typeof(CredentialTokenResponse).GetProperty("RefreshToken")
            .Should().BeNull("a credential-issuance token grant does not issue refresh tokens");
    }

    [Fact]
    public void IsNotThePlatformAuthTokenResponse()
    {
        typeof(CredentialTokenResponse).Should().NotBe(typeof(Sorcha.Tenant.Models.Auth.TokenResponse));
    }

    [Theory]
    [InlineData(nameof(CredentialTokenResponse.AccessToken), "access_token")]
    [InlineData(nameof(CredentialTokenResponse.TokenType), "token_type")]
    [InlineData(nameof(CredentialTokenResponse.ExpiresIn), "expires_in")]
    [InlineData(nameof(CredentialTokenResponse.CNonce), "c_nonce")]
    [InlineData(nameof(CredentialTokenResponse.CNonceExpiresIn), "c_nonce_expires_in")]
    public void Property_SerialisesToItsSpecName(string clrName, string wireName)
    {
        // The rename is a C# identifier change only — the wire shape is fixed by OpenID4VCI.
        typeof(CredentialTokenResponse)
            .GetProperty(clrName, BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttribute<JsonPropertyNameAttribute>()!
            .Name.Should().Be(wireName);
    }
}
