// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Verifier.Engine.Dcql;
using Xunit;

namespace Sorcha.Verifier.Tests.Dcql;

/// <summary>
/// Feature 181 T007 — the shared DCQL builder/parser pair round-trips, validates ids and
/// referential integrity, and refuses the retired Presentation Exchange dialect.
/// </summary>
public class DcqlRoundTripTests
{
    private static readonly string[] RequiredClaims = ["givenName", "familyName"];

    [Fact]
    public void Build_SingleSdJwtAsk_EmitsSpecShape()
    {
        var query = DcqlRequestBuilder.Build(
            [DcqlCredentialAsk.SdJwt("identity", "https://sorcha.dev/vc/assured-identity/v1", RequiredClaims)],
            purpose: "Prove your identity");

        var json = DcqlRequestBuilder.ToJson(query);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("credentials")[0].GetProperty("format").GetString()
            .Should().Be("dc+sd-jwt");
        doc.RootElement.GetProperty("credentials")[0].GetProperty("meta").GetProperty("vct_values")[0].GetString()
            .Should().Be("https://sorcha.dev/vc/assured-identity/v1");
        doc.RootElement.GetProperty("credential_sets")[0].GetProperty("purpose").GetString()
            .Should().Be("Prove your identity");
        json.Should().NotContain("input_descriptors").And.NotContain("presentation_definition");
    }

    [Fact]
    public void Parse_BuilderOutput_RoundTripsIncludingOptionalSplit()
    {
        var built = DcqlRequestBuilder.Build(
            [DcqlCredentialAsk.SdJwt("identity", "vct:test", RequiredClaims, optionalClaims: ["portrait"])]);

        var parsed = DcqlRequestParser.Parse(DcqlRequestBuilder.ToJson(built));

        parsed.Credentials.Should().HaveCount(1);
        var (required, optional) = DcqlRequestParser.SplitClaims(parsed.Credentials[0]);
        required.Should().BeEquivalentTo(RequiredClaims);
        optional.Should().BeEquivalentTo(["portrait"]);
    }

    [Fact]
    public void Build_NestedClaimPath_RoundTripsSlashConvention()
    {
        var built = DcqlRequestBuilder.Build(
            [DcqlCredentialAsk.SdJwt("addr", "vct:test", ["/address/street"])]);

        built.Credentials[0].Claims![0].Path.Should().Equal("address", "street");
        DcqlRequestParser.FromPath(built.Credentials[0].Claims![0].Path).Should().Be("/address/street");
    }

    [Fact]
    public void Build_AlternativeGroups_EmitCredentialSets()
    {
        var built = DcqlRequestBuilder.Build(
            [
                DcqlCredentialAsk.SdJwt("pid", "vct:pid", ["given_name"]),
                DcqlCredentialAsk.SdJwt("assured", "vct:assured", ["givenName"]),
            ],
            alternatives: [new DcqlAlternativeGroup([["pid"], ["assured"]], Purpose: "Prove your identity")]);

        var parsed = DcqlRequestParser.Parse(DcqlRequestBuilder.ToJson(built));
        parsed.CredentialSets.Should().HaveCount(1);
        parsed.CredentialSets![0].Options.Should().HaveCount(2);
        parsed.CredentialSets[0].Required.Should().BeTrue();
    }

    [Fact]
    public void Build_MdocAsk_RequiresDoctype()
    {
        var act = () => DcqlRequestBuilder.Build(
            [new DcqlCredentialAsk("mdl", DcqlFormats.MsoMdoc, null, DoctypeValue: null, ["family_name"])]);

        act.Should().Throw<DcqlParseException>()
            .Which.Code.Should().Be(DcqlErrorCodes.Invalid);
    }

    [Theory]
    [InlineData("bad id!")]
    [InlineData("")]
    public void Build_InvalidQueryId_Throws(string id)
    {
        var act = () => DcqlRequestBuilder.Build([DcqlCredentialAsk.SdJwt(id, "vct:test", RequiredClaims)]);

        act.Should().Throw<DcqlParseException>().Which.Code.Should().Be(DcqlErrorCodes.Invalid);
    }

    [Fact]
    public void Build_DuplicateQueryIds_Throws()
    {
        var act = () => DcqlRequestBuilder.Build(
            [
                DcqlCredentialAsk.SdJwt("same", "vct:a", RequiredClaims),
                DcqlCredentialAsk.SdJwt("same", "vct:b", RequiredClaims),
            ]);

        act.Should().Throw<DcqlParseException>().Which.Message.Should().Contain("Duplicate credential query id");
    }

    [Fact]
    public void Parse_CredentialSetsReferencingUnknownId_Throws()
    {
        const string json = """
            { "credentials": [ { "id": "a", "format": "dc+sd-jwt", "meta": { "vct_values": ["vct:a"] } } ],
              "credential_sets": [ { "options": [["a"], ["missing"]] } ] }
            """;

        var act = () => DcqlRequestParser.Parse(json);

        act.Should().Throw<DcqlParseException>().Which.Message.Should().Contain("unknown credential query id 'missing'");
    }

    [Fact]
    public void Parse_PresentationExchangeShape_RejectedAsLegacyDialect()
    {
        const string json = """
            { "id": "pd-1", "input_descriptors": [ { "id": "x", "constraints": { "fields": [] } } ] }
            """;

        var act = () => DcqlRequestParser.Parse(json);

        act.Should().Throw<DcqlParseException>().Which.Code.Should().Be(DcqlErrorCodes.LegacyDialect);
    }

    [Fact]
    public void ParseFromRequestObjectPayload_LegacyPayload_RejectedAsLegacyDialect()
    {
        using var doc = JsonDocument.Parse("""{ "presentation_definition": {}, "nonce": "n" }""");

        var act = () => DcqlRequestParser.ParseFromRequestObjectPayload(doc.RootElement);

        act.Should().Throw<DcqlParseException>().Which.Code.Should().Be(DcqlErrorCodes.LegacyDialect);
    }

    [Fact]
    public void VpToken_ObjectEnvelope_RoundTrips()
    {
        var token = new DcqlVpToken
        {
            Presentations = new Dictionary<string, IReadOnlyList<string>>
            {
                ["identity"] = ["jwt~disc1~kb"],
                ["addr"] = ["jwt2~kb2"],
            },
        };

        var parsed = DcqlVpToken.Parse(token.ToJson());

        parsed.Presentations.Should().HaveCount(2);
        parsed.Presentations["identity"].Should().Equal("jwt~disc1~kb");
    }

    [Fact]
    public void VpToken_BareCompactString_RejectedAsLegacyDialect()
    {
        var act = () => DcqlVpToken.Parse("eyJhbGciOi.payload.sig~disclosure~kbjwt");

        act.Should().Throw<DcqlParseException>().Which.Code.Should().Be(DcqlErrorCodes.LegacyDialect);
    }

    [Fact]
    public void VpToken_EmptyObject_RejectedAsInvalid()
    {
        var act = () => DcqlVpToken.Parse("{}");

        act.Should().Throw<DcqlParseException>().Which.Code.Should().Be(DcqlErrorCodes.Invalid);
    }
}
