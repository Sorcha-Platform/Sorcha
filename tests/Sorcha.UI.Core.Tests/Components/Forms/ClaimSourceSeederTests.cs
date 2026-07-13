// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// Feature 183 (US1) — the AIAS web-app happy path was rejected because the
/// read-only <c>emailVerified</c> field (on no form page) was never carried
/// onto the wallet-signed payload, so the agent read it as absent → false →
/// reject. <see cref="ClaimSourceSeeder"/> stamps a field from a named JWT
/// claim (<c>x-claim-source</c>) into the submission at form init. These tests
/// prove the value lands even though the field is on no visible page, and that
/// a boolean binding FAILS CLOSED when the claim is missing.
/// </summary>
public class ClaimSourceSeederTests
{
    private const string SchemaWithEmailVerified = """
        {
          "type": "object",
          "properties": {
            "name":          { "type": "string",  "title": "Name" },
            "emailVerified": { "type": "boolean", "readOnly": true, "default": true, "x-claim-source": "email_verified" }
          }
        }
        """;

    private static JsonDocument Schema(string json) => JsonDocument.Parse(json);

    private static ClaimsPrincipal PrincipalWith(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "jwt");
        return new ClaimsPrincipal(identity);
    }

    private static readonly IClaimSourceSeeder Seeder = new ClaimSourceSeeder();

    [Fact]
    public void Resolve_BooleanClaimTrue_SeedsTrue()
    {
        var result = Seeder.Resolve(Schema(SchemaWithEmailVerified), PrincipalWith(("email_verified", "true")));

        result.Should().ContainKey("/emailVerified");
        result["/emailVerified"].Should().Be(true);
    }

    [Fact]
    public void Resolve_BooleanClaimTrue_IsCaseInsensitive()
    {
        var result = Seeder.Resolve(Schema(SchemaWithEmailVerified), PrincipalWith(("email_verified", "True")));

        result["/emailVerified"].Should().Be(true);
    }

    [Fact]
    public void Resolve_BooleanClaimFalse_SeedsFalse()
    {
        var result = Seeder.Resolve(Schema(SchemaWithEmailVerified), PrincipalWith(("email_verified", "false")));

        result.Should().ContainKey("/emailVerified");
        result["/emailVerified"].Should().Be(false);
    }

    [Fact]
    public void Resolve_BooleanClaimAbsent_FailsClosedToFalse()
    {
        // Authenticated principal, but no email_verified claim → must not be treated as verified.
        var result = Seeder.Resolve(Schema(SchemaWithEmailVerified), PrincipalWith(("sub", "user-123")));

        result.Should().ContainKey("/emailVerified");
        result["/emailVerified"].Should().Be(false);
    }

    [Fact]
    public void Resolve_BooleanClaimUnparseable_FailsClosedToFalse()
    {
        var result = Seeder.Resolve(Schema(SchemaWithEmailVerified), PrincipalWith(("email_verified", "yes-ish")));

        result["/emailVerified"].Should().Be(false);
    }

    [Fact]
    public void Resolve_PropertyWithoutBinding_IsNotSeeded()
    {
        var result = Seeder.Resolve(Schema(SchemaWithEmailVerified), PrincipalWith(("email_verified", "true")));

        result.Should().NotContainKey("/name");
    }

    [Fact]
    public void Resolve_NonBooleanBindingPresent_SeedsRawString()
    {
        const string schema = """
            {
              "type": "object",
              "properties": {
                "tenant": { "type": "string", "x-claim-source": "org_id" }
              }
            }
            """;

        var result = Seeder.Resolve(Schema(schema), PrincipalWith(("org_id", "acme-org")));

        result["/tenant"].Should().Be("acme-org");
    }

    [Fact]
    public void Resolve_NonBooleanBindingAbsentClaim_IsNotSeeded()
    {
        const string schema = """
            {
              "type": "object",
              "properties": {
                "tenant": { "type": "string", "x-claim-source": "org_id" }
              }
            }
            """;

        var result = Seeder.Resolve(Schema(schema), PrincipalWith(("sub", "user-123")));

        result.Should().NotContainKey("/tenant");
    }

    [Fact]
    public void Resolve_NullSchema_ReturnsEmpty()
    {
        Seeder.Resolve(null, PrincipalWith(("email_verified", "true"))).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NullUser_ReturnsEmpty()
    {
        Seeder.Resolve(Schema(SchemaWithEmailVerified), null).Should().BeEmpty();
    }
}
