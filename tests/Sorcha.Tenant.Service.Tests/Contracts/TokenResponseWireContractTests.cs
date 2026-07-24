// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using FluentAssertions;

using Sorcha.Tenant.Models.Auth;

namespace Sorcha.Tenant.Service.Tests.Contracts;

/// <summary>
/// Pins the RFC 6749 token-endpoint wire shape now that issuer and clients share one type.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TokenResponse"/> previously existed as five separate declarations — Tenant Service,
/// the Blazor UI, the CLI, the service-principal client, and the demo host. They had already
/// diverged in what they admitted: the UI's copy omitted <c>scope</c>, and the service-principal
/// client's copy omitted <c>refresh_token</c>. Each dropped whatever its author did not need, so a
/// field added at the issuer reached no consumer until somebody noticed.
/// </para>
/// <para>
/// With one type the divergence cannot recur, but the wire names still need pinning: they are
/// OAuth 2.0 registered parameter names, not ours to rename.
/// </para>
/// </remarks>
public sealed class TokenResponseWireContractTests
{
    [Theory]
    [InlineData(nameof(TokenResponse.AccessToken), "access_token")]
    [InlineData(nameof(TokenResponse.TokenType), "token_type")]
    [InlineData(nameof(TokenResponse.ExpiresIn), "expires_in")]
    [InlineData(nameof(TokenResponse.RefreshToken), "refresh_token")]
    [InlineData(nameof(TokenResponse.Scope), "scope")]
    public void Property_SerialisesToItsRfc6749Name(string clrName, string wireName)
    {
        var attr = typeof(TokenResponse)
            .GetProperty(clrName, BindingFlags.Public | BindingFlags.Instance)!
            .GetCustomAttribute<JsonPropertyNameAttribute>();

        attr.Should().NotBeNull($"{clrName} must pin its wire name explicitly");
        attr!.Name.Should().Be(wireName);
    }

    [Fact]
    public void ClientCredentialsShapedPayload_DeserialisesWithoutARefreshToken()
    {
        // RFC 6749 s4.4.3: the client-credentials grant SHOULD NOT return a refresh token, and
        // Sorcha's service-principal flow does not. Modelling RefreshToken as required would throw
        // here — which is exactly why the service-principal client kept its own private copy.
        const string json = """
            {"access_token":"abc","token_type":"Bearer","expires_in":3600,"scope":"wallets:sign"}
            """;

        var parsed = JsonSerializer.Deserialize<TokenResponse>(json);

        parsed.Should().NotBeNull();
        parsed!.AccessToken.Should().Be("abc");
        parsed.RefreshToken.Should().BeNull();
        parsed.Scope.Should().Be("wallets:sign");
        parsed.IsValid().Should().BeTrue();
    }

    [Fact]
    public void UserGrantShapedPayload_RoundTripsEveryField()
    {
        var original = new TokenResponse
        {
            AccessToken = "at",
            RefreshToken = "rt",
            ExpiresIn = 900,
            Scope = "openid",
        };

        var roundTripped = JsonSerializer.Deserialize<TokenResponse>(JsonSerializer.Serialize(original));

        roundTripped.Should().BeEquivalentTo(original);
    }

    [Theory]
    [InlineData("", 3600)]
    [InlineData("   ", 3600)]
    [InlineData("abc", 0)]
    [InlineData("abc", -1)]
    public void IsValid_RejectsBlankTokenOrNonPositiveLifetime(string accessToken, int expiresIn)
    {
        new TokenResponse { AccessToken = accessToken, ExpiresIn = expiresIn }
            .IsValid().Should().BeFalse();
    }

    // The "platform vs OpenID4VCI token response stay distinct types" assertion lives in
    // Sorcha.Haip.Service.Tests, which already sees both. Referencing HAIP from the Tenant test
    // project purely to assert a separation would be the wrong kind of coupling.
}
