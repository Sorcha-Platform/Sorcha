// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.UI.Core.Services.User.Pairing;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Services;

/// <summary>
/// Regression for the live n1 bug where a signed-in citizen on <c>/setup/add-device</c> saw
/// "Couldn't generate a pairing code." The pairing surfaces reached for the host's <b>ambient</b>
/// <see cref="HttpClient"/>, which in the web client is deliberately anonymous (it is the client
/// <c>AuthenticationService</c> itself uses, so it cannot carry the auth handler without a circular
/// dependency). Every mint therefore went out with no <c>Authorization</c> header and the server
/// correctly answered 401.
/// <para>
/// The fix is this seam: a typed client the <b>host</b> registers with its own auth handler. These
/// tests pin the client's contract — it returns the mint on success and degrades to null (never
/// throws) on failure, so the surface can render its own message.
/// </para>
/// </summary>
public class PairingClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body = "")
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (PairingClient Client, StubHandler Handler) Create(
        HttpStatusCode status, string body = "")
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://n1.sorcha.dev") };
        return (new PairingClient(http, NullLogger<PairingClient>.Instance), handler);
    }

    [Fact]
    public async Task MintAsync_Success_ReturnsTheMintedSession()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """
            {"sessionToken":"eyJhbG.stub","qrUrl":"https://n1.sorcha.dev/wallet/enrol?t=eyJhbG.stub",
             "expiresAt":"2026-07-13T13:47:52+00:00","mode":"standalone"}
            """);

        var mint = await client.MintAsync("standalone");

        mint.Should().NotBeNull();
        mint!.QrUrl.Should().Be("https://n1.sorcha.dev/wallet/enrol?t=eyJhbG.stub");
        mint.SessionToken.Should().Be("eyJhbG.stub");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/auth/enrol-session");
        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task MintAsync_Unauthorized_ReturnsNullRatherThanThrowing()
    {
        // The exact live failure. The surface turns null into its own message; it must never see
        // an exception, and must never mistake a 401 for a successful mint.
        var (client, _) = Create(HttpStatusCode.Unauthorized);

        var mint = await client.MintAsync("standalone");

        mint.Should().BeNull();
    }

    [Fact]
    public async Task MintAsync_NoMode_OmitsTheModeField_PreservingTheGatedDefault()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """
            {"sessionToken":"t","qrUrl":"https://x/e","expiresAt":"2026-07-13T13:47:52+00:00"}
            """);

        await client.MintAsync(mode: null);

        var body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        body.Should().NotContain("mode");
    }

    [Fact]
    public async Task MintShortCodeAsync_Success_ReturnsTheCode()
    {
        var (client, handler) = Create(HttpStatusCode.OK, """
            {"code":"243806","expiresAt":"2026-07-13T13:47:52+00:00"}
            """);

        var code = await client.MintShortCodeAsync("MobilewebHandoff");

        code.Should().NotBeNull();
        code!.Code.Should().Be("243806");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/api/auth/enrol-session/short-code");
    }

    [Fact]
    public async Task MintShortCodeAsync_Failure_ReturnsNull_SoTheSurfaceDegradesQuietly()
    {
        var (client, _) = Create(HttpStatusCode.Unauthorized);

        var code = await client.MintShortCodeAsync("MobilewebHandoff");

        code.Should().BeNull();
    }
}
