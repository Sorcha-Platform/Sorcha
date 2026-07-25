// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// Issue #1302 — enforcing <c>x-holder-key: { "required": true }</c> at the form.
/// <para>
/// Before this, a citizen whose wallet-key fetch failed saw a warning and a Retry but could still
/// submit: <c>HolderKeyRenderer</c> set no <c>FormContext</c> error, and <c>HandleSubmit</c> clears
/// errors and repopulates them from schema validation alone, so a renderer-set error would have
/// been wiped anyway. The application sealed fine and then the <b>analyst's</b> approval threw
/// <c>VAL_RUNTIME_CRED_005</c> — a cross-participant failure, on an approval already granted.
/// </para>
/// <para>
/// Putting <c>holderKeys</c> in the parent <c>required</c> array cannot fix it:
/// <c>ValidateDataRecursive</c> deliberately skips object-typed entries there. Hence the keyword.
/// </para>
/// </summary>
public sealed class HolderKeyRequiredValidationTests
{
    private readonly FormSchemaService _sut = new();

    private const string HolderKeyScope = "/holderKeys";

    /// <summary>The exact shape the AIAS and AssuredIdentity starting actions ship.</summary>
    private static JsonDocument LiveBlueprintSchema(bool required = true) => JsonDocument.Parse($$"""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string" },
            "holderKeys": {
              "type": "object",
              "title": "Your holder key",
              "format": "sorcha-holder-key",
              "x-holder-key": { "required": {{(required ? "true" : "false")}} }
            }
          },
          "required": ["name"]
        }
        """);

    private static Dictionary<string, object?> Captured() => new()
    {
        ["/name"] = "Ada",
        [$"{HolderKeyScope}/holderJwk"] = """{"kty":"EC"}""",
        [$"{HolderKeyScope}/encryptionPublicKey"] = "YmFzZTY0",
        [$"{HolderKeyScope}/algorithm"] = "ED25519",
    };

    [Fact]
    public void KeysNotCaptured_BlocksSubmission()
    {
        var data = new Dictionary<string, object?> { ["/name"] = "Ada" };

        var errors = _sut.ValidateData(LiveBlueprintSchema(), data);

        errors.Should().ContainKey(HolderKeyScope,
            "an uncapturable application must not reach the analyst — they cannot complete an "
            + "approval whose credential can never be bound");
    }

    [Fact]
    public void TheMessageSendsTheCitizenToTheRetry_NotToAFieldTheyCannotType()
    {
        var errors = _sut.ValidateData(
            LiveBlueprintSchema(), new Dictionary<string, object?> { ["/name"] = "Ada" });

        var message = string.Join(" ", errors[HolderKeyScope]);
        message.Should().NotContain("encryptionPublicKey",
            "naming an internal leaf is useless — the citizen never types these");
        message.Should().ContainEquivalentOf("retry");
    }

    [Fact]
    public void KeysCaptured_Submits()
    {
        var errors = _sut.ValidateData(LiveBlueprintSchema(), Captured());

        errors.Should().NotContainKey(HolderKeyScope);
    }

    [Theory]
    [InlineData("holderJwk")]
    [InlineData("encryptionPublicKey")]
    [InlineData("algorithm")]
    public void APartialCaptureStillBlocks(string missingLeaf)
    {
        // All three are read back at issuance — the cnf binding needs the JWK, the delivery
        // envelope needs the key and its algorithm. Two out of three is still unissuable.
        var data = Captured();
        data.Remove($"{HolderKeyScope}/{missingLeaf}");

        _sut.ValidateData(LiveBlueprintSchema(), data).Should().ContainKey(HolderKeyScope);
    }

    [Fact]
    public void OptIn_NotDeclaredRequired_DoesNotBlock()
    {
        // Blueprints that never asked for enforcement must keep working exactly as before.
        var errors = _sut.ValidateData(
            LiveBlueprintSchema(required: false), new Dictionary<string, object?> { ["/name"] = "Ada" });

        errors.Should().NotContainKey(HolderKeyScope);
    }

    [Fact]
    public void AFieldWithNoHolderKeyExtensionIsUntouched()
    {
        var schema = JsonDocument.Parse("""
            {"type":"object","properties":{"holderKeys":{"type":"object","format":"sorcha-holder-key"}}}
            """);

        _sut.ValidateData(schema, new Dictionary<string, object?>()).Should().NotContainKey(HolderKeyScope);
    }

    [Fact]
    public void AnEmptyStringLeafCountsAsMissing()
    {
        // The renderer writes all three or none, but a resumed draft or a hand-built payload can
        // carry blanks. A blank encryption key is not a key.
        var data = Captured();
        data[$"{HolderKeyScope}/algorithm"] = "";

        _sut.ValidateData(LiveBlueprintSchema(), data).Should().ContainKey(HolderKeyScope);
    }
}
