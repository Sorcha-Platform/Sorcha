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
using Sorcha.ServiceClients.Wallet;
using Sorcha.Wallet.Contracts.Models;

using Xunit;

namespace Sorcha.ServiceClients.Tests.Wallet;

/// <summary>
/// Contract tests for <see cref="WalletServiceClient.GetMyWalletsAsync"/> — "which wallets does the
/// CALLER hold?".
/// </summary>
/// <remarks>
/// <para>
/// Regression cover for cold-start run #5, where every agent reported "You hold no wallet to bind"
/// and fell back to its ORGANISATION's wallet. The MCP server forwards the caller's own bearer, but
/// the lookup went to <c>GET /api/v1/wallets/by-owner/{ownerId}</c> — an endpoint documented as
/// "never exposed to end users" and gated by <c>RequireService</c>. Every caller therefore got 403,
/// the client swallowed it into an empty list, and the tool reported an ABSENCE of wallets as fact.
/// </para>
/// <para>
/// Both halves are pinned here. The route must be the caller-scoped one, whose handler resolves the
/// owner from the caller's own <c>platform_user_id</c> claim; and a lookup that FAILED must be
/// distinguishable from one that genuinely found nothing, because those call for opposite responses
/// from the caller.
/// </para>
/// </remarks>
public class WalletServiceClientCallerWalletsTests
{
    private const string Personal = "ws11qqzq42w3y80d367f6a2ft0szr0tdttm97alxrsvrwtq7zxtrax0dsq07pez";
    private const string Second = "ws11qr3tj8tm6dtxh8fn5aw3qu0kz2x8ty7llat9t9sdnfru5eyh2asrsjgh32w";

    /// <summary>
    /// The bytes the Wallet Service actually writes: <c>Results.Ok(wallets.Select(w =&gt; w.ToDto()))</c>
    /// over the canonical <see cref="WalletDto"/>, serialised with the options that service configures.
    /// Building the fixture from the server's own type means a rename on either side fails here.
    /// </summary>
    private static string RealWalletListPayload(params string[] addresses)
    {
        var dtos = addresses.Select(a => new WalletDto
        {
            Address = a,
            Name = "Run 5 personal",
            PublicKey = "pk-" + a,
            Algorithm = "ED25519",
            Status = "Active",
            Owner = "184c63af-5a0f-484b-9351-f4d67cea7ff7",
            Tenant = "default",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        }).ToList();

        return System.Text.Json.JsonSerializer.Serialize(dtos, SorchaJson.Options);
    }

    [Fact]
    public async Task GetMyWalletsAsync_AsksTheCallerScopedRoute_NotTheServiceOnlyOne()
    {
        // by-owner is RequireService and refuses a user token. Forwarding the caller's bearer to it
        // cannot ever succeed, which is why the route itself is the contract under test.
        Uri? requested = null;
        var client = BuildClient(req =>
        {
            requested = req.RequestUri;
            return Respond(HttpStatusCode.OK, RealWalletListPayload(Personal));
        });

        await client.GetMyWalletsAsync();

        requested!.AbsolutePath.Should().Be("/api/v1/wallets");
        requested.AbsolutePath.Should().NotContain("by-owner");
    }

    [Fact]
    public async Task GetMyWalletsAsync_WhenRefused_ReportsUnavailable_NotAnEmptyList()
    {
        // THE defect. A 403 that becomes "[]" is indistinguishable from "this user owns nothing",
        // so the caller confidently tells a person they hold no wallet when it never managed to look.
        var client = BuildClient(_ => Respond(HttpStatusCode.Forbidden, """{"error":"forbidden"}"""));

        var lookup = await client.GetMyWalletsAsync();

        lookup.Status.Should().Be(CallerWalletLookupStatus.Unavailable);
        lookup.Wallets.Should().BeEmpty();
        lookup.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetMyWalletsAsync_WhenTheServiceIsUnreachable_ReportsUnavailable()
    {
        var client = BuildClient(_ => throw new HttpRequestException("connection refused"));

        var lookup = await client.GetMyWalletsAsync();

        lookup.Status.Should().Be(CallerWalletLookupStatus.Unavailable);
        lookup.Wallets.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyWalletsAsync_WhenTheBodyIsNotAReadableList_ReportsUnavailable()
    {
        // A 200 carrying a null body is not an answer either. Deserialising it to an empty list
        // would report "you hold no wallet" on the strength of a response nobody could read.
        var client = BuildClient(_ => Respond(HttpStatusCode.OK, "null"));

        var lookup = await client.GetMyWalletsAsync();

        lookup.Status.Should().Be(CallerWalletLookupStatus.Unavailable);
        lookup.Wallets.Should().BeEmpty();
        lookup.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetMyWalletsAsync_WhenTheCallerGenuinelyHoldsNone_ReportsFoundAndEmpty()
    {
        // The counterfactual to the test above: an ANSWERED lookup of zero wallets is a fact the
        // caller may act on, and must not be conflated with a lookup that failed.
        var client = BuildClient(_ => Respond(HttpStatusCode.OK, "[]"));

        var lookup = await client.GetMyWalletsAsync();

        lookup.Status.Should().Be(CallerWalletLookupStatus.Found);
        lookup.Wallets.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMyWalletsAsync_ReadsEveryWalletTheServiceSent()
    {
        var client = BuildClient(_ => Respond(HttpStatusCode.OK, RealWalletListPayload(Personal, Second)));

        var lookup = await client.GetMyWalletsAsync();

        lookup.Status.Should().Be(CallerWalletLookupStatus.Found);
        lookup.Wallets.Select(w => w.Address).Should().Equal(Personal, Second);
        lookup.Wallets[0].PublicKey.Should().Be("pk-" + Personal);
        lookup.Wallets[0].Algorithm.Should().Be("ED25519");
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

        var http = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost:5001") };
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
