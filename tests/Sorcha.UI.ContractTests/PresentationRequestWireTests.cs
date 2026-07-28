// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models.Workflows;

namespace Sorcha.UI.ContractTests;

/// <summary>
/// Round-trips the presentation-request payload through the serializers each side actually uses.
/// Matching member names is not enough — the wire form has to agree too, and enum form is where
/// these two ends can disagree silently.
/// </summary>
public class PresentationRequestWireTests
{
    /// <summary>What the Blueprint Service writes with: ASP.NET Core's Minimal API defaults.</summary>
    private static readonly JsonSerializerOptions ServerWrites = new(JsonSerializerDefaults.Web);

    private static PresentationRequestResponse Sample(PresentationSource source) => new()
    {
        RequestId = Guid.NewGuid(),
        PresentationRequestUri = "openid4vp://authorize?x=1",
        CredentialType = "https://sorcha.dev/vc/assured-identity/v1",
        RequestedClaims = ["givenName", "familyName"],
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        Source = source,
        ClaimsFetchToken = "tok-abc"
    };

    [Theory]
    [InlineData(PresentationSource.SorchaWallet)]
    [InlineData(PresentationSource.HaipExternalWallet)]
    public void ServerPayloadDeserialisesIntoTheClientMirror(PresentationSource source)
    {
        var json = JsonSerializer.Serialize(Sample(source), ServerWrites);

        var client = JsonSerializer.Deserialize<PresentationRequestInfo>(json, JsonDefaults.Api);

        client.Should().NotBeNull();
        client!.Source.Should().Be(source,
            "the card picks its transport from this value; a mis-read routes a SorchaWallet gate to HAIP");
        client.ClaimsFetchToken.Should().Be("tok-abc");
        client.PresentationRequestUri.Should().Be("openid4vp://authorize?x=1");
        client.RequestedClaims.Should().BeEquivalentTo("givenName", "familyName");
    }

    [Fact]
    public void SourceIsNeverSilentlyDefaulted()
    {
        // A failure to read the enum must not land on the first enum member. SorchaInternal is
        // PresentationSource's default(0), so "unreadable" and "internal" would be
        // indistinguishable — and an internal source means the card polls nothing at all.
        var json = JsonSerializer.Serialize(Sample(PresentationSource.SorchaWallet), ServerWrites);

        var client = JsonSerializer.Deserialize<PresentationRequestInfo>(json, JsonDefaults.Api);

        client!.Source.Should().NotBe(PresentationSource.SorchaInternal);
    }
}
