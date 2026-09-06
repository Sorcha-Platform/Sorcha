// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.ServiceClients.Auth;
using Sorcha.Verifier.Services;
using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Feature 164 / #1189 — <see cref="ServiceAuthMessageHandler"/> attaches the service-tier bearer
/// token from <see cref="IServiceAuthClient"/> to outbound HAIP verifier requests, so the Open
/// Verifier can call the authenticated <c>POST /api/v1/verifier/requests</c> endpoint.
/// </summary>
public sealed class ServiceAuthMessageHandlerTests
{
    private sealed class StubServiceAuthClient(string? token) : IServiceAuthClient
    {
        // The Verifier IS a configured service principal — it authenticates as itself, so it never
        // takes the "no credentials configured" path ServiceClientAuthHelper skips on.
        public bool HasNoCredentialsConfigured => false;

        public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(token);
    }

    private sealed class RecordingInnerHandler : DelegatingHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task SendAsync_WithToken_SetsBearerHeader()
    {
        var inner = new RecordingInnerHandler();
        var handler = new ServiceAuthMessageHandler(new StubServiceAuthClient("test-service-token"))
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://haip-service/api/v1/verifier/requests");

        await invoker.SendAsync(request, CancellationToken.None);

        inner.LastRequest.Should().NotBeNull();
        inner.LastRequest!.Headers.Authorization.Should().NotBeNull();
        inner.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        inner.LastRequest.Headers.Authorization.Parameter.Should().Be("test-service-token");
    }

    [Fact]
    public async Task SendAsync_NoToken_LeavesAuthorizationUnset()
    {
        var inner = new RecordingInnerHandler();
        var handler = new ServiceAuthMessageHandler(new StubServiceAuthClient(null))
        {
            InnerHandler = inner
        };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://haip-service/api/v1/verifier/requests");

        await invoker.SendAsync(request, CancellationToken.None);

        inner.LastRequest.Should().NotBeNull();
        inner.LastRequest!.Headers.Authorization.Should().BeNull();
    }
}
