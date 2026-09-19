// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

using Sorcha.Serialization;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Register.Models;

using Xunit;

namespace Sorcha.ServiceClients.Tests.Register;

/// <summary>
/// Wire contract for <c>GET /registers/{id}/participants/resolve</c> — found by cold-start run #5.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint hand-wrote an anonymous projection of the record instead of returning it. The
/// projection emitted <c>organisationName</c> where the DTO requires <c>organizationName</c>, and
/// omitted <c>version</c> and <c>latestTxId</c> altogether. All three are <c>required</c>, and one
/// missing required property throws for the WHOLE payload — so <c>ResolveParticipantAsync</c>
/// never returned a record to anybody, on any register, ever.
/// </para>
/// <para>
/// Silently disabled by that: recipient-key resolution (an encrypted register had no key to encrypt
/// to, which before #1581 meant it stored PLAINTEXT), the published-record tier of
/// <c>SenderWalletResolver</c> (#1664), and <c>VAL_BP_002</c>'s published-record tier in the
/// Validator. "Publish a participant record to bind a role to a wallet" never worked end to end.
/// </para>
/// <para>
/// Nothing caught it because all three consumers mock <c>IRegisterServiceClient</c>, and an
/// anonymous return type is invisible to the response-shape gate (CLAUDE.md §25). These tests bind
/// real bytes, so a rename or a reintroduced projection on either side fails here.
/// </para>
/// </remarks>
public class RegisterServiceClientResolveParticipantWireTests
{
    /// <summary>
    /// The exact body n1 returned on 2026-09-19 while the defect was live, captured from the running
    /// node. It is well-formed, complete and useful — and unreadable by the client.
    /// </summary>
    private const string BrokenProjection = """
        {"participantId":"c5aff551-03cc-482e-9562-72a24b54dec7","participantName":"recipient",
         "organisationName":"Cold-start Run 5 Recipient","status":"Active",
         "addresses":[{"walletAddress":"ws11qzzrademphyfqkpyrp7kghmewjd23fepwn4vdu0sx8vq2ev9cfa6zwm5epy",
         "publicKey":"hD63Ow3IkFgkGH1kX3l0mqinIXTqxvHwMdgFZYXCe6E=","algorithm":"ED25519","primary":true}]}
        """;

    [Fact]
    public async Task ResolveParticipantAsync_ReadsTheRecordTheEndpointNowReturns()
    {
        // The fixture is the SERVER's own type, serialised as the endpoint serialises it — which is
        // the point of the fix: the endpoint returns this type rather than a copy of it.
        var record = new PublishedParticipantRecord
        {
            ParticipantId = "c5aff551-03cc-482e-9562-72a24b54dec7",
            ParticipantName = "recipient",
            OrganizationName = "Cold-start Run 5 Recipient",
            Status = "Active",
            Version = 1,
            LatestTxId = "ffdec8d12e6719c6c3f24981c94f46afd1365e236d9cca9e5063cfc223d5995b",
            Addresses =
            [
                new ParticipantAddressInfo
                {
                    WalletAddress = "ws11qzzrademphyfqkpyrp7kghmewjd23fepwn4vdu0sx8vq2ev9cfa6zwm5epy",
                    PublicKey = "hD63Ow3IkFgkGH1kX3l0mqinIXTqxvHwMdgFZYXCe6E=",
                    Algorithm = "ED25519",
                    Primary = true,
                }
            ],
        };

        var client = BuildClient(HttpStatusCode.OK,
            System.Text.Json.JsonSerializer.Serialize(record, SorchaJson.Options));

        var resolved = await client.ResolveParticipantAsync("reg-1", "recipient", "Cold-start Run 5 Recipient");

        resolved.Should().NotBeNull();
        resolved!.ParticipantName.Should().Be("recipient");
        resolved.OrganizationName.Should().Be("Cold-start Run 5 Recipient");
        resolved.Version.Should().Be(1);
        // The public key is the whole point: without it an encrypted register has nothing to
        // encrypt the recipient's disclosure to.
        resolved.Addresses.Should().ContainSingle()
            .Which.PublicKey.Should().Be("hD63Ow3IkFgkGH1kX3l0mqinIXTqxvHwMdgFZYXCe6E=");
    }

    [Fact]
    public async Task ResolveParticipantAsync_TheOldProjection_WasUnreadable()
    {
        // Pins WHY the endpoint changed. If someone reintroduces a lossy projection, the consumers
        // go silently dead again — so this documents the shape that must never come back.
        //
        // The client catches only HttpRequestException, so this escapes to the CALLER, which logs it
        // and carries on without a record. That is why the symptom was a warning in the Blueprint
        // Service's log and a bare 400 at the surface, rather than anything naming the real cause.
        var client = BuildClient(HttpStatusCode.OK, BrokenProjection);

        var act = () => client.ResolveParticipantAsync("reg-1", "recipient", null);

        (await act.Should().ThrowAsync<System.Text.Json.JsonException>())
            .Which.Message.Should().Contain("organizationName");
    }

    [Fact]
    public async Task ResolveParticipantAsync_NotFound_IsNullWithoutThrowing()
    {
        var client = BuildClient(HttpStatusCode.NotFound, """{"error":"No published participant record found"}""");

        (await client.ResolveParticipantAsync("reg-1", "nobody", null)).Should().BeNull();
    }

    [Fact]
    public async Task ResolveParticipantAsync_Revoked_IsNullRatherThanARecordToTrust()
    {
        var client = BuildClient(HttpStatusCode.Gone, """{"title":"Participant Revoked"}""");

        (await client.ResolveParticipantAsync("reg-1", "recipient", null)).Should().BeNull();
    }

    private static RegisterServiceClient BuildClient(HttpStatusCode status, string body)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:5290") };
        var auth = new Mock<IServiceAuthClient>();
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("test-token");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:RegisterService:Address"] = "http://localhost:5290",
            })
            .Build();

        return new RegisterServiceClient(http, auth.Object, config, Mock.Of<ILogger<RegisterServiceClient>>());
    }
}
