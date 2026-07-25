// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models;
using Xunit;

namespace Sorcha.Blueprint.Models.Tests;

/// <summary>
/// Issue #1302 — <c>x-holder-key</c> shipped in two live blueprints with
/// <c>{ "required": true }</c> and nothing read it. This type gives the keyword its first consumer.
/// <para>
/// The declaration is load-bearing, not decoration: in <c>aias-assured-identity</c> both
/// participants are open (<c>walletAddress: null</c>), so a walk-in citizen has no published
/// participant record and must CARRY their public keys in the application payload. Action 2's
/// <c>holderKeySourceField: /holderKeys/holderJwk</c> reads them back to bind (SD-JWT <c>cnf</c>)
/// and encrypt the credential. Without them the analyst's approval throws
/// <c>VAL_RUNTIME_CRED_005</c> — a cross-participant failure caused by a gap in someone else's
/// earlier submission.
/// </para>
/// </summary>
public sealed class HolderKeySchemaExtensionTests
{
    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void ParsesTheRequiredFlagFromTheLiveBlueprintShape()
    {
        // Byte-for-byte the shape AIAS and AssuredIdentity ship today.
        var ok = HolderKeySchemaExtension.TryParseFromSchema(
            Schema("""{"type":"object","format":"sorcha-holder-key","x-holder-key":{"required":true}}"""),
            out var ext);

        ok.Should().BeTrue();
        ext!.Required.Should().BeTrue();
    }

    [Fact]
    public void AbsentExtensionIsNotParsed()
        => HolderKeySchemaExtension.TryParseFromSchema(
            Schema("""{"type":"object","format":"sorcha-holder-key"}"""), out _)
            .Should().BeFalse();

    [Fact]
    public void RequiredDefaultsToFalse_SoTheKeywordIsOptIn()
    {
        // An author who writes a bare `x-holder-key: {}` has not asked for enforcement. Defaulting
        // to true would retroactively block submissions on blueprints that never opted in.
        HolderKeySchemaExtension.TryParseFromSchema(
            Schema("""{"x-holder-key":{}}"""), out var ext).Should().BeTrue();

        ext!.Required.Should().BeFalse();
    }

    [Fact]
    public void AMalformedExtensionIsNotParsed_RatherThanThrowing()
    {
        HolderKeySchemaExtension.TryParseFromSchema(
            Schema("""{"x-holder-key":"nonsense"}"""), out _).Should().BeFalse();
        HolderKeySchemaExtension.TryParseFromSchema(
            Schema("""{"x-holder-key":{"required":"yes"}}"""), out _).Should().BeFalse();
    }

    [Fact]
    public void TheCarriedLeavesMatchWhatTheIssuerActuallyReads()
    {
        // ResolveCarriedHolderKeys needs the holder JWK (cnf binding, FR-014) plus the encryption
        // key + algorithm (delivery envelope, FR-012). Enforcing anything less would let a
        // submission through that still cannot be issued against.
        HolderKeySchemaExtension.CarriedLeafNames.Should()
            .BeEquivalentTo(["holderJwk", "encryptionPublicKey", "algorithm"]);
    }
}
