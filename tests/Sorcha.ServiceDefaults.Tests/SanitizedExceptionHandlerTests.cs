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
