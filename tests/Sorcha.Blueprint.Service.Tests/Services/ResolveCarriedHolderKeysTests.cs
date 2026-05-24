// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Text.Json;
using Sorcha.Blueprint.Service.Services.Implementation;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="ActionExecutionService.ResolveCarriedHolderKeys"/> — the Feature 137
/// extractor that reads the citizen's carried delivery keys (written by a <c>sorcha-holder-key</c>
/// form field) from reconstructed instance state. Verifies the holder JWK is returned as a
/// <see cref="JsonElement"/>, the sibling encryption key + algorithm are derived from the same
/// parent object, and missing/empty segments collapse to null.
/// </summary>
public class ResolveCarriedHolderKeysTests
{
    private static Dictionary<string, object> Merged(string holderKeysJson)
    {
        // Mirror reconstructed state: the action payload spread at root, holderKeys a JsonElement.
        var element = JsonSerializer.Deserialize<JsonElement>(holderKeysJson);
        return new Dictionary<string, object> { ["holderKeys"] = element };
    }

    [Fact]
    public void ResolvesHolderJwk_EncryptionKey_AndAlgorithm_FromSiblings()
    {
        var merged = Merged("""
            {
              "holderJwk": { "kty": "EC", "crv": "P-256", "x": "AAA", "y": "BBB" },
              "encryptionPublicKey": "QkFTRTY0S0VZ",
              "algorithm": "ED25519"
            }
            """);

        var (holderJwk, encKey, algorithm) =
            ActionExecutionService.ResolveCarriedHolderKeys(merged, "/holderKeys/holderJwk");

        holderJwk.Should().NotBeNull();
        holderJwk!.Value.GetProperty("kty").GetString().Should().Be("EC");
        holderJwk.Value.GetProperty("crv").GetString().Should().Be("P-256");
        encKey.Should().Be("QkFTRTY0S0VZ");
        algorithm.Should().Be("ED25519");
    }

    [Fact]
    public void MissingHolderKeysObject_ReturnsAllNull()
    {
        var merged = new Dictionary<string, object>
        {
            ["name"] = JsonSerializer.Deserialize<JsonElement>("""{ "givenName": "Alice" }""")
        };

        var (holderJwk, encKey, algorithm) =
            ActionExecutionService.ResolveCarriedHolderKeys(merged, "/holderKeys/holderJwk");

        holderJwk.Should().BeNull();
        encKey.Should().BeNull();
        algorithm.Should().BeNull();
    }

    [Fact]
    public void EmptySiblingStrings_CollapseToNull()
    {
        var merged = Merged("""
            {
              "holderJwk": { "kty": "OKP", "crv": "Ed25519", "x": "ZZZ" },
              "encryptionPublicKey": "",
              "algorithm": "   "
            }
            """);

        var (holderJwk, encKey, algorithm) =
            ActionExecutionService.ResolveCarriedHolderKeys(merged, "/holderKeys/holderJwk");

        holderJwk.Should().NotBeNull();
        encKey.Should().BeNull();
        algorithm.Should().BeNull();
    }

    [Fact]
    public void MissingHolderJwk_ButPresentEncryptionKey_ReturnsNullJwk()
    {
        // FR-014 fail-closed precondition: an opted-in blueprint with no holder JWK must be
        // detectable by the caller (holderJwk == null) so it can refuse to issue an unbound credential.
        var merged = Merged("""
            {
              "encryptionPublicKey": "QkFTRTY0",
              "algorithm": "NISTP256"
            }
            """);

        var (holderJwk, encKey, algorithm) =
            ActionExecutionService.ResolveCarriedHolderKeys(merged, "/holderKeys/holderJwk");

        holderJwk.Should().BeNull();
        encKey.Should().Be("QkFTRTY0");
        algorithm.Should().Be("NISTP256");
    }
}
