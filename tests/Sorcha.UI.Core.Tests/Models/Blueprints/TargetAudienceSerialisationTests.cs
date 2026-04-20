// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Sorcha.Blueprint.Models.Credentials;

using Xunit;

namespace Sorcha.UI.Core.Tests.Models.Blueprints;

/// <summary>
/// Feature 106 — round-trips the <see cref="CredentialIssuanceConfig"/> DTO through
/// <see cref="JsonSerializer"/> with the new <see cref="TargetAudience.SorchaLocalWallet"/>
/// value to catch any serialiser drift before blueprints are authored against it.
/// </summary>
public class TargetAudienceSerialisationTests
{
    private static CredentialIssuanceConfig SampleConfig(TargetAudience audience) => new()
    {
        CredentialType = "AssuredIdentityCredential",
        ClaimMappings =
        [
            new ClaimMapping { ClaimName = "givenName", SourceField = "/name/givenName" }
        ],
        RecipientParticipantId = "citizen",
        TargetAudience = audience,
    };

    [Theory]
    [InlineData(TargetAudience.SorchaInternal, "SorchaInternal")]
    [InlineData(TargetAudience.HaipExternalWallet, "HaipExternalWallet")]
    [InlineData(TargetAudience.SorchaLocalWallet, "SorchaLocalWallet")]
    public void Serialize_EmitsEnumName(TargetAudience audience, string expected)
    {
        var config = SampleConfig(audience);

        var json = JsonSerializer.Serialize(config);

        if (audience == TargetAudience.SorchaInternal)
        {
            // Default value is omitted per [JsonIgnore(WhenWritingDefault)] on the property.
            json.Should().NotContain("\"targetAudience\"");
        }
        else
        {
            json.Should().Contain($"\"targetAudience\":\"{expected}\"");
        }
    }

    [Fact]
    public void Deserialize_SorchaLocalWallet_RoundTrips()
    {
        const string Json = /*lang=json,strict*/ """
        {
          "credentialType": "AssuredIdentityCredential",
          "claimMappings": [ { "claimName": "givenName", "sourceField": "/name/givenName" } ],
          "recipientParticipantId": "citizen",
          "targetAudience": "SorchaLocalWallet"
        }
        """;

        var config = JsonSerializer.Deserialize<CredentialIssuanceConfig>(Json);

        config.Should().NotBeNull();
        config!.TargetAudience.Should().Be(TargetAudience.SorchaLocalWallet);
    }

    [Fact]
    public void SorchaLocalWallet_HasDistinctNumericValue()
    {
        ((int)TargetAudience.SorchaInternal).Should().Be(0);
        ((int)TargetAudience.HaipExternalWallet).Should().Be(1);
        ((int)TargetAudience.SorchaLocalWallet).Should().Be(2);
    }
}
