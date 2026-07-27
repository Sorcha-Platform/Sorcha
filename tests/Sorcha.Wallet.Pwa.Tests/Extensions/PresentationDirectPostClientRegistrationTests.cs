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
public sealed class PresentationDirectPostClientRegistrationTests
{
    [Fact]
    public async Task PresentationDirectPostClient_ProductionRegistration_AttachesStoredBearerToken()
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

        // Act
        await client.PostAsync("https://verifier.test/api/presentations/callbacks/sorcha-wallet/abc", form);

        // Assert — this is exactly what BearerTokenHandler.SendAsync stamps (see
        // BearerTokenHandler.cs:50). If .AddHttpMessageHandler<BearerTokenHandler>() were removed
        // from the production registration, this header would be null and the assertion fails.
        captured.Should().NotBeNull("the request must reach the bottom of the DI-built handler chain");
        captured!.Headers.Authorization.Should().NotBeNull(
            "the sorcha-wallet callback requires RequireConsumerAudience — a direct_post with no " +
            "bearer token 401s before the endpoint's form-vs-json branch ever runs (#1310/#1311)");
        captured.Headers.Authorization.Should().BeEquivalentTo(
            new AuthenticationHeaderValue("Bearer", "citizen-bearer-token"));
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }
}
