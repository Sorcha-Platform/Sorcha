// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for <see cref="CredentialOfferSchemaResolver"/>. Feature 104 wave 14b.
/// </summary>
public class CredentialOfferSchemaResolverTests
{
    private static JsonElement ClaimActionSchema() =>
        JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "credentialOffer": {
              "type": "object",
              "x-credential-offer": true,
              "properties": {
                "credential_offer_uri": { "type": "string" },
                "credential_type":      { "type": "string" },
                "expires_at":           { "type": "string", "format": "date-time" },
                "offer_id":             { "type": "string" }
              },
              "required": ["credential_offer_uri"]
            },
            "claimed_at": { "type": "string", "format": "date-time" }
          },
          "required": ["credentialOffer"]
        }
        """);

    private static JsonObject FullSeededPayload() => new()
    {
        ["credentialOffer"] = new JsonObject
        {
            ["credential_offer_uri"] = "openid-credential-offer://?credential_offer=abc",
            ["credential_type"] = "AssuredIdentityCredential",
            ["expires_at"] = "2026-04-15T12:00:00Z",
            ["offer_id"] = "11111111-1111-1111-1111-111111111111"
        }
    };

    [Fact]
    public void TryResolve_FullyPopulatedSeed_ReturnsInfo()
    {
        var info = CredentialOfferSchemaResolver.TryResolve(ClaimActionSchema(), FullSeededPayload());

        info.Should().NotBeNull();
        info!.FieldName.Should().Be("credentialOffer");
        info.CredentialOfferUri.Should().Be("openid-credential-offer://?credential_offer=abc");
        info.CredentialType.Should().Be("AssuredIdentityCredential");
        info.OfferId.Should().Be("11111111-1111-1111-1111-111111111111");
        info.ExpiresAt.Should().NotBeNull();
        info.ExpiresAt!.Value.Year.Should().Be(2026);
        info.RawCredentialOffer.Should().NotBeNull();
        info.RawCredentialOffer["credential_offer_uri"]!.GetValue<string>()
            .Should().Be("openid-credential-offer://?credential_offer=abc");
    }

    [Fact]
    public void TryResolve_NullSchema_ReturnsNull()
    {
        CredentialOfferSchemaResolver.TryResolve(null, FullSeededPayload()).Should().BeNull();
    }

    [Fact]
    public void TryResolve_NullPrepopulatedPayload_ReturnsNull()
    {
        CredentialOfferSchemaResolver.TryResolve(ClaimActionSchema(), null).Should().BeNull();
    }

    [Fact]
    public void TryResolve_EmptyPrepopulatedPayload_ReturnsNull()
    {
        CredentialOfferSchemaResolver.TryResolve(ClaimActionSchema(), new JsonObject()).Should().BeNull();
    }

    [Fact]
    public void TryResolve_SchemaWithoutCredentialOfferExtension_ReturnsNull()
    {
        var plainSchema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "note": { "type": "string" }
          }
        }
        """);
        var seed = new JsonObject { ["note"] = "hello" };

        CredentialOfferSchemaResolver.TryResolve(plainSchema, seed).Should().BeNull();
    }

    [Fact]
    public void TryResolve_CredentialOfferMarkerOnNonObjectField_ReturnsNull()
    {
        // VAL_BP_012 would block this at publish time but the resolver is also
        // defensive — if a malformed schema reaches the client, we bail rather
        // than attempt to render a claim card on a scalar field.
        var schema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "credentialOffer": {
              "type": "string",
              "x-credential-offer": true
            }
          }
        }
        """);
        var seed = new JsonObject { ["credentialOffer"] = "not an object" };

        CredentialOfferSchemaResolver.TryResolve(schema, seed).Should().BeNull();
    }

    [Fact]
    public void TryResolve_SeedMissingOfferUri_ReturnsNull()
    {
        var seed = new JsonObject
        {
            ["credentialOffer"] = new JsonObject
            {
                ["credential_type"] = "AssuredIdentityCredential"
                // no credential_offer_uri
            }
        };

        CredentialOfferSchemaResolver.TryResolve(ClaimActionSchema(), seed).Should().BeNull();
    }

    [Fact]
    public void TryResolve_MinimalSeedWithOnlyUri_ReturnsInfoWithNullOptionals()
    {
        var seed = new JsonObject
        {
            ["credentialOffer"] = new JsonObject
            {
                ["credential_offer_uri"] = "openid-credential-offer://?credential_offer=minimal"
            }
        };

        var info = CredentialOfferSchemaResolver.TryResolve(ClaimActionSchema(), seed);

        info.Should().NotBeNull();
        info!.CredentialOfferUri.Should().Be("openid-credential-offer://?credential_offer=minimal");
        info.CredentialType.Should().BeNull();
        info.OfferId.Should().BeNull();
        info.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void TryResolve_MalformedExpiresAt_ReturnsInfoWithNullExpiresAt()
    {
        var seed = new JsonObject
        {
            ["credentialOffer"] = new JsonObject
            {
                ["credential_offer_uri"] = "openid-credential-offer://?credential_offer=bad",
                ["expires_at"] = "not-a-date"
            }
        };

        var info = CredentialOfferSchemaResolver.TryResolve(ClaimActionSchema(), seed);

        info.Should().NotBeNull();
        info!.ExpiresAt.Should().BeNull();
    }
}
