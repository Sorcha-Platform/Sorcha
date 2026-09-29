// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

using Sorcha.Serialization;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Wallet.Contracts.Models;

using Xunit;

namespace Sorcha.ServiceClients.Tests.Wallet;

/// <summary>
/// TODO(095) / #1759 — the Blueprint Service asks the Wallet Service to sign each IETF status list
/// with the issuing org's key. Drives the real client over HTTP, and reads the request back with the
/// options the Wallet Service binds with, so a drift on either side fails here.
/// </summary>
public class WalletServiceClientSignStatusListTests
{
    private static readonly SignStatusListTokenRequest Request = new()
    {
        OrganizationId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Subject = "https://n1.sorcha.dev/api/v1/credentials/ietf-status-lists/list-1",
        Bits = 2,
        EntriesBase64 = Convert.ToBase64String([0xb9, 0xa3]),
        TtlSeconds = 300,
    };

    [Fact]
    public async Task SignStatusListTokenAsync_PostsTheRequestAndReadsTheSignedToken()
    {
        HttpRequestMessage? sent = null;
        string? sentBody = null;
        var response = new SignStatusListTokenResponse
        {
            Jwt = "h.p.s",
            IssuerDid = "did:sorcha:org:ws1",
            Kid = "did:sorcha:org:ws1#vc-issuance-0",
        };
        var client = BuildClient(req =>
        {
            sent = req;
            sentBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Respond(HttpStatusCode.OK, JsonSerializer.Serialize(response, SorchaJson.Options));
        });

        var result = await client.SignStatusListTokenAsync(Request);

        sent!.Method.Should().Be(HttpMethod.Post);
        sent.RequestUri!.AbsolutePath.Should().Be("/api/internal/status-lists/ietf/sign");
        sent.Headers.Authorization!.Parameter.Should().Be("test-token", "only a service token may reach this route");
        JsonSerializer.Deserialize<SignStatusListTokenRequest>(sentBody!, SorchaJson.Options)
            .Should().BeEquivalentTo(Request, "the Wallet Service binds this exact contract");
        result.Should().BeEquivalentTo(response);
    }

    [Fact]
    public async Task SignStatusListTokenAsync_OrgHasNoIssuanceKey_ReturnsNull()
    {
        var client = BuildClient(_ => Respond(HttpStatusCode.Conflict, """{"title":"no key"}"""));

        var result = await client.SignStatusListTokenAsync(Request);

        result.Should().BeNull("409 is the one status that means 'this org cannot sign a list'");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task SignStatusListTokenAsync_AnyOtherFailure_Throws(HttpStatusCode status)
    {
        var client = BuildClient(_ => Respond(status, "{}"));

        var act = () => client.SignStatusListTokenAsync(Request);

        await act.Should().ThrowAsync<HttpRequestException>(
            "a failure to sign must not be mistaken for an org with no key");
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static WalletServiceClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) => Task.FromResult(respond(req)));

        var http = new HttpClient(handler.Object);
        var auth = new Mock<IServiceAuthClient>();
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("test-token");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:WalletService:Address"] = "http://localhost:5001",
            })
            .Build();

        return new WalletServiceClient(http, auth.Object, config, Mock.Of<ILogger<WalletServiceClient>>());
    }
}
