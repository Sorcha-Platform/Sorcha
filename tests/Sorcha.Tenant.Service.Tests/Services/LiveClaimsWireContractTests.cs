// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Serialization;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Pins the live-claims wire contract (issue #1264).
/// <para>
/// The response is a dictionary whose <b>keys are claim names</b> (<c>email_verified</c>), and the
/// consumer looks values up by that exact name. Sorcha's JSON conventions set
/// <c>PropertyNamingPolicy = CamelCase</c>; System.Text.Json applies that to <i>properties</i>, and
/// applies <c>DictionaryKeyPolicy</c> — currently unset — to dictionary <i>keys</i>. So keys survive
/// verbatim today. If anyone ever sets <c>DictionaryKeyPolicy</c>, <c>email_verified</c> would ship as
/// <c>emailVerified</c>, every lookup would miss, and every binding would silently fail closed —
/// re-rejecting compliant citizens with no error anywhere. This test is that tripwire.
/// </para>
/// </summary>
public sealed class LiveClaimsWireContractTests
{
    [Fact]
    public void ClaimNameKeys_SurviveTheSorchaWireFormat_Verbatim()
    {
        var payload = new Dictionary<string, string>
        {
            [LivePlatformUserClaimsService.EmailVerified] = "true",
            [LivePlatformUserClaimsService.Email] = "citizen@example.test",
            [LivePlatformUserClaimsService.Name] = "Stuart Fraser",
        };

        var json = JsonSerializer.Serialize(payload, SorchaJson.Options);

        json.Should().Contain("\"email_verified\"",
            "the consumer looks claims up by exact name; camelCasing the key would make every "
            + "x-claim-source binding silently fail closed (#1264)");
        json.Should().NotContain("emailVerified");
    }

    [Fact]
    public void ClaimNameKeys_RoundTrip_ThroughTheSameOptions()
    {
        var original = new Dictionary<string, string>
        {
            [LivePlatformUserClaimsService.EmailVerified] = "true",
        };

        var json = JsonSerializer.Serialize(original, SorchaJson.Options);
        var roundTripped = JsonSerializer.Deserialize<Dictionary<string, string>>(json, SorchaJson.Options);

        roundTripped.Should().NotBeNull();
        roundTripped!.Should().ContainKey(LivePlatformUserClaimsService.EmailVerified)
            .WhoseValue.Should().Be("true");
    }

    /// <summary>
    /// The vocabulary is the JWT claim vocabulary — the same names <c>TokenService</c> mints — so a
    /// caller asks for "the live value of the claim I would otherwise have read off the token".
    /// Pinning the literal wire values here means a rename cannot silently desynchronise the two sides.
    /// </summary>
    [Fact]
    public void SupportedNames_MatchTheMintedJwtClaimVocabulary()
    {
        LivePlatformUserClaimsService.EmailVerified.Should().Be("email_verified");
        LivePlatformUserClaimsService.Email.Should().Be("email");
        LivePlatformUserClaimsService.Name.Should().Be("name");
    }
}
