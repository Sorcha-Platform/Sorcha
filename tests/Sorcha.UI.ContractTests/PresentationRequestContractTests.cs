// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.UI.Core.Models.Workflows;

namespace Sorcha.UI.ContractTests;

/// <summary>
/// The presentation-request DTO is the only channel by which the web app learns which lifecycle
/// owns a gate, and therefore which endpoints it must poll. A member missing from either side does
/// not fail to compile and does not throw — the client silently polls the wrong service and
/// reports the eventual silence as an expiry.
/// </summary>
/// <remarks>
/// <see cref="PresentationInitiationResult"/> has always carried a <c>ClaimsFetchToken</c>, but the
/// response record had no field for it, so <c>ActionExecutionService</c> dropped it during mapping.
/// Same defect class as the T032 Redis fields (#1314) and the DID-document publish (#1318): a
/// hand-maintained mapping loses a field and nothing verifies the join.
/// </remarks>
public class PresentationRequestContractTests
{
    [Fact]
    public void ServerResponseCarriesSourceAndClaimsToken()
    {
        var response = new PresentationRequestResponse
        {
            RequestId = Guid.NewGuid(),
            PresentationRequestUri = "openid4vp://authorize?x=1",
            CredentialType = "https://sorcha.dev/vc/assured-identity/v1",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            Source = PresentationSource.SorchaWallet,
            ClaimsFetchToken = "tok-abc"
        };

        response.Source.Should().Be(PresentationSource.SorchaWallet);
        response.ClaimsFetchToken.Should().Be("tok-abc",
            "the web app cannot fetch disclosed claims without it");
    }

    [Fact]
    public void ClientMirrorHasEveryServerMember()
    {
        var server = typeof(PresentationRequestResponse)
            .GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var client = typeof(PresentationRequestInfo)
            .GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        client.Should().BeEquivalentTo(server,
            "a member present on one side only is a field the mapping will silently drop");
    }

    [Fact]
    public void ClientMirrorCarriesTheSourceDiscriminator()
    {
        // Guards the specific value, not just the member: PresentationSource is what the card
        // switches its transport on, so an int-vs-string or renamed enum member would route a
        // SorchaWallet gate to HAIP again.
        var info = new PresentationRequestInfo
        {
            RequestId = Guid.NewGuid(),
            Source = PresentationSource.SorchaWallet,
            ClaimsFetchToken = "tok-abc"
        };

        info.Source.Should().Be(PresentationSource.SorchaWallet);
        info.ClaimsFetchToken.Should().Be("tok-abc");
    }
}
