// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;

namespace Sorcha.ServiceDefaults.Tests;

/// <summary>
/// Issue #1506 — a failing dependency must not be able to refuse calls to a healthy, unrelated one.
/// </summary>
/// <remarks>
/// <para>The reported symptom was a best-effort inbox write to the Tenant Service taking credential
/// issuance down with it: a run of 500s from <c>POST /api/internal/inbox</c> opened a circuit
/// breaker, and the next wallet call failed with <c>"The circuit is now open and is not allowing
/// calls."</c> — a message that has nothing to do with the wallet.</para>
///
/// <para>The cause is a property of <c>ConfigureHttpClientDefaults</c>, not of the inbox: a
/// resilience handler added there is built once, for the nameless default client builder, so a
/// single pipeline instance — and therefore a single breaker — serves every <see cref="HttpClient"/>
/// in the process. <c>SelectPipelineByAuthority</c> partitions it per downstream host.</para>
///
/// <para>The counterfactual for this file is one line: drop <c>.SelectPipelineByAuthority()</c> from
/// <c>AddServiceDefaults</c> and <see cref="AnOpenBreakerOnOneAuthority_DoesNotRefuseCallsToAnother"/>
/// goes red with the exact production message, while
/// <see cref="AFailingAuthority_StillOpensItsOwnBreaker"/> stays green — i.e. the guard is about
/// isolation, not about whether breaking still works.</para>
/// </remarks>
public class HttpResilienceIsolationTests
{
    private sealed class StatusHandler(HttpStatusCode code) : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new HttpResponseMessage(code) { RequestMessage = request });
        }
    }

    /// <summary>
    /// Shrinks the standard pipeline's timings so a breaker can be opened in milliseconds. It does
    /// NOT touch how the pipeline is keyed, which is the only thing under test here.
    /// </summary>
    private static void Hasten(HttpStandardResilienceOptions o)
    {
        o.Retry.MaxRetryAttempts = 1;
        o.Retry.Delay = TimeSpan.Zero;
        o.Retry.UseJitter = false;
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(1);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(5);
        o.CircuitBreaker.MinimumThroughput = 4;
        o.CircuitBreaker.FailureRatio = 0.5;
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    }

    private static (IHttpClientFactory Factory, StatusHandler Sick, StatusHandler Healthy) Build()
    {
        var sick = new StatusHandler(HttpStatusCode.InternalServerError);
        var healthy = new StatusHandler(HttpStatusCode.OK);

        // The REAL producer, not a local copy of its wiring. Re-declaring
        // ConfigureHttpClientDefaults here would leave this file green if someone dropped
        // SelectPipelineByAuthority from AddServiceDefaults — a guard for a defect it can no longer see.
        var builder = WebApplication.CreateBuilder();
        builder.AddServiceDefaults();

        // ConfigureAll, because the standard pipeline's options are registered under a NAME derived
        // from the pipeline, which this test has no business knowing. Timings only.
        builder.Services.ConfigureAll<HttpStandardResilienceOptions>(Hasten);

        builder.Services.AddHttpClient("inbox", c => c.BaseAddress = new Uri("http://tenant-service"))
            .ConfigurePrimaryHttpMessageHandler(() => sick);
        builder.Services.AddHttpClient("issuance", c => c.BaseAddress = new Uri("http://wallet-service"))
            .ConfigurePrimaryHttpMessageHandler(() => healthy);

        return (builder.Services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), sick, healthy);
    }

    /// <summary>Drives the sick client until its breaker opens. Returns the exception that opened it.</summary>
    private static async Task<Exception> OpenTheBreakerAsync(HttpClient sickClient)
    {
        for (var i = 0; i < 40; i++)
        {
            try
            {
                await sickClient.GetAsync("/api/internal/inbox");
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        throw new InvalidOperationException(
            "The circuit never opened after 40 failing requests — the test can no longer demonstrate anything.");
    }

    [Fact]
    public async Task AFailingAuthority_StillOpensItsOwnBreaker()
    {
        var (factory, _, _) = Build();

        var opened = await OpenTheBreakerAsync(factory.CreateClient("inbox"));

        opened.Should().BeOfType<Polly.CircuitBreaker.BrokenCircuitException>(
            "partitioning the pipeline must not stop a sick dependency from breaking its own circuit");
    }

    [Fact]
    public async Task AnOpenBreakerOnOneAuthority_DoesNotRefuseCallsToAnother()
    {
        var (factory, _, healthy) = Build();
        await OpenTheBreakerAsync(factory.CreateClient("inbox"));

        var response = await factory.CreateClient("issuance").GetAsync("/api/v1/credentials");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a bell-drawer notification failing against the tenant service must never refuse credential issuance");
        healthy.Calls.Should().Be(1,
            "the healthy client's request must actually reach its handler — a shared breaker refuses before the wire, "
            + "so a call count of 0 is the signature of the #1506 defect even if no exception is observed");
    }
}
