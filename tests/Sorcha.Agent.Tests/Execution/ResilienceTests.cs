// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;

namespace Sorcha.Agent.Tests.Execution;

public class ResilienceTests
{
    private class FakeHandler : DelegatingHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public int CallCount { get; private set; }

        public FakeHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_responses.Count > 0)
                return Task.FromResult(_responses.Dequeue());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task RetryPolicy_RetriesOnServerError()
    {
        var handler = new FakeHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.OK));

        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(10));

        var client = new HttpClient(new PolicyHttpMessageHandler(retryPolicy) { InnerHandler = handler });

        var response = await client.GetAsync("http://localhost/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.CallCount.Should().Be(3); // 2 failures + 1 success
    }

    [Fact]
    public async Task CircuitBreaker_OpensAfterThreshold()
    {
        var handler = new FakeHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(2, TimeSpan.FromSeconds(30));

        var client = new HttpClient(new PolicyHttpMessageHandler(circuitBreakerPolicy) { InnerHandler = handler });

        // First 2 calls fail and trip the breaker
        await client.GetAsync("http://localhost/test");
        await client.GetAsync("http://localhost/test");

        // Third call should throw BrokenCircuitException
        var act = () => client.GetAsync("http://localhost/test");
        await act.Should().ThrowAsync<BrokenCircuitException>();
    }

    [Fact]
    public async Task RetryPolicy_DoesNotRetryOn401()
    {
        var handler = new FakeHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(10));

        var client = new HttpClient(new PolicyHttpMessageHandler(retryPolicy) { InnerHandler = handler });

        var response = await client.GetAsync("http://localhost/test");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        handler.CallCount.Should().Be(1); // No retry on 401
    }
}
