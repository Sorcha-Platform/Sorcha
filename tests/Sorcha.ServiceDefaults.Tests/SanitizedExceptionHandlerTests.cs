// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Sorcha.ServiceDefaults;

namespace Sorcha.ServiceDefaults.Tests;

/// <summary>
/// Issue #1433: before this handler existed, Sorcha had no exception-handling middleware anywhere,
/// so ASP.NET Core's auto-added DeveloperExceptionPage served full stack traces whenever
/// <c>ASPNETCORE_ENVIRONMENT=Development</c> — including on an internet-facing node that was
/// (mis)configured that way. These tests exercise the real <c>AddServiceDefaults</c> /
/// <c>UseSanitizedExceptionHandling</c> wiring end-to-end via a <see cref="TestServer"/>, in BOTH
/// the "Development" and "Production" environment names, to prove the sanitized response is the
/// only possible outcome regardless of environment.
/// </summary>
public class SanitizedExceptionHandlerTests
{
    private static async Task<WebApplication> BuildAppAsync(string environmentName)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName
        });

        builder.WebHost.UseTestServer();

        // Exercises the actual production wiring: AddServiceDefaults registers AddProblemDetails()
        // and AddExceptionHandler<SanitizedExceptionHandler>() (see Extensions.cs), so a regression
        // in that registration fails this test, not just a hand-rolled equivalent.
        builder.AddServiceDefaults();

        var app = builder.Build();

        // Mirrors the FIRST-in-pipeline placement every service's Program.cs uses.
        app.UseSanitizedExceptionHandling();

        app.MapGet("/throws", () =>
        {
            throw new InvalidOperationException(
                "Sensitive internal detail that must never reach the HTTP response body.");
#pragma warning disable CS0162 // Unreachable code detected — required to satisfy IResult return type
            return Results.Ok();
#pragma warning restore CS0162
        });

        app.MapGet("/ok", () => Results.Ok(new { status = "fine" }));

        // A body-binding endpoint, so the framework itself raises BadHttpRequestException when the
        // caller sends something it cannot read. This is the real #1476 path — not a hand-thrown
        // exception — so it also proves the handler sits where model binding failures reach it.
        app.MapPost("/binds", (ProbeRequest request) => Results.Ok(request));

        await app.StartAsync();
        return app;
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnhandledException_AnyEnvironment_ReturnsSanitizedProblemDetails(string environmentName)
    {
        // Arrange
        await using var app = await BuildAppAsync(environmentName);
        var client = app.GetTestClient();

        // Act
        var response = await client.GetAsync("/throws");
        var body = await response.Content.ReadAsStringAsync();

        // Assert — status + content type are the RFC 7807 shape.
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        // Assert — no stack trace ever reaches the body (a stack trace frame always contains " at ",
        // and .NET's default DeveloperExceptionPage / exception formatting always names the file).
        body.Should().NotContain(" at ", "a stack trace frame must never appear in the response body");
        body.Should().NotContain(".cs:line", "a source file/line reference must never appear in the response body");

        // Assert — no exception type name or message leaks either.
        body.Should().NotContain(nameof(InvalidOperationException));
        body.Should().NotContain("Sensitive internal detail");

        // Assert — the response is still useful: generic title + a trace id an operator can correlate
        // against the server-side log this handler also writes.
        body.Should().Contain("\"title\"");
        body.Should().Contain("\"traceId\"");
    }

    /// <summary>
    /// Issue #1476: a request the framework cannot read is the CALLER's fault and must be reported
    /// as such. The handler used to overwrite BadHttpRequestException's own 400 with 500 — so a
    /// caller who omitted a required field was told the server had broken, and clients and agents
    /// (which retry 500s and do not retry 400s) were invited into a retry storm against a request
    /// that can never succeed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The environment here is load-bearing, and getting it wrong makes this test pass for the
    /// wrong reason.</b> A minimal-API body-binding failure only THROWS when
    /// <c>RouteHandlerOptions.ThrowOnBadRequest</c> is true, which ASP.NET Core defaults to in
    /// <c>Development</c> and not otherwise. Under <c>Production</c> the endpoint returns a bare,
    /// bodiless 400 by itself and this handler never runs — so a Production-based version of this
    /// test asserts 400 successfully against the UNFIXED handler and proves nothing.
    /// </para>
    /// <para>
    /// Development is also the environment the bug was reported from: Sorcha's services run with
    /// <c>ASPNETCORE_ENVIRONMENT=Development</c>, which is precisely why #1476 was reachable on a
    /// live node rather than being a theoretical path.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("{ \"reason\": \"test\" }", "a body missing a required property")]
    [InlineData("{ not json at all", "a body that is not valid JSON")]
    public async Task MalformedRequestBody_Returns400_NotServerError(string body, string why)
    {
        await using var app = await BuildAppAsync("Development");
        var client = app.GetTestClient();

        var response = await client.PostAsync("/binds",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"{why} is a client error — reporting it as 500 tells the caller the server broke and " +
            "invites a retry of something that can never succeed");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        // The #1433 guarantee must not have become a 4xx-shaped loophole. Everything the 500 path
        // is forbidden from leaking is equally forbidden here — and a binding failure is exactly the
        // case where .NET's own message would otherwise name the request TYPE and the property.
        responseBody.Should().NotContain(" at ", "a stack trace frame must never appear in the body");
        responseBody.Should().NotContain(".cs:line");
        responseBody.Should().NotContain(nameof(BadHttpRequestException));
        responseBody.Should().NotContain(nameof(ProbeRequest),
            "the framework's own binding message names the target type — that must not reach the caller");
        responseBody.Should().NotContain("issuerWallet",
            "nor may it name the property that was missing");

        responseBody.Should().Contain("\"traceId\"",
            "an operator still needs to correlate this against the server-side log line");
    }

    [Fact]
    public async Task ClientErrorAndServerError_AreDistinguishable()
    {
        // The whole value of #1476 is that these two are no longer the same response. Asserting the
        // pair means a regression that collapses them again fails here even if either single-status
        // test were somehow satisfied. Development for the reason given above — it is the only
        // environment in which a binding failure reaches this handler at all.
        await using var app = await BuildAppAsync("Development");
        var client = app.GetTestClient();

        var clientError = await client.PostAsync("/binds",
            new StringContent("{ }", System.Text.Encoding.UTF8, "application/json"));
        var serverError = await client.GetAsync("/throws");

        clientError.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        serverError.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    /// <summary>Minimal body-bound request type with a required property, for the binding-failure tests.</summary>
    private sealed record ProbeRequest(string Reason)
    {
        public required string IssuerWallet { get; init; }
    }

    [Fact]
    public async Task NoException_ReturnsOkUnaffected()
    {
        // The global handler must only intercept UNHANDLED exceptions — normal successful responses
        // must pass through completely unchanged.
        await using var app = await BuildAppAsync("Production");
        var client = app.GetTestClient();

        var response = await client.GetAsync("/ok");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("fine");
    }
}
