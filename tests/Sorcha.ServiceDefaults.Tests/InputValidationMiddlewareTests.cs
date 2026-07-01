// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Sorcha.ServiceDefaults.Tests;

/// <summary>
/// Unit tests for <see cref="InputValidationMiddleware"/> query-string XSS detection.
/// </summary>
public class InputValidationMiddlewareTests
{
    private static async Task<(bool nextCalled, int statusCode)> InvokeWithQueryAsync(string queryString)
    {
        var nextCalled = false;
        var middleware = new InputValidationMiddleware(
            next: _ => { nextCalled = true; return Task.CompletedTask; },
            logger: NullLogger<InputValidationMiddleware>.Instance,
            options: Options.Create(new InputValidationOptions()));

        var context = new DefaultHttpContext();
        context.Request.Path = "/";
        context.Request.QueryString = new QueryString(queryString);

        await middleware.InvokeAsync(context);

        return (nextCalled, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("?onboarding=true")]   // Feature 157 wallet-wizard redirect — the reported false positive
    [InlineData("?online=1")]
    [InlineData("?once=abc")]
    [InlineData("?returnUrl=/app/wallets")]
    public async Task InvokeAsync_LegitimateOnPrefixedQueryParam_IsAllowed(string queryString)
    {
        // The XSS event-handler heuristic must not flag ordinary query params that merely start
        // with "on" (onboarding, online, once, …). A real event-handler injection always has a
        // preceding attribute separator (whitespace / quote / slash); a bare query param does not.
        var (nextCalled, statusCode) = await InvokeWithQueryAsync(queryString);

        nextCalled.Should().BeTrue("'{0}' is a benign query string and must pass the WAF", queryString);
        statusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Theory]
    [InlineData("?q=<img src=x onerror=alert(1)>")]   // event handler with a leading space
    [InlineData("?q=\"><svg onload=alert(1)>")]
    [InlineData("?q=<script>alert(1)</script>")]
    [InlineData("?q=javascript:alert(1)")]
    public async Task InvokeAsync_ActualXssPayload_IsBlocked(string queryString)
    {
        var (nextCalled, statusCode) = await InvokeWithQueryAsync(queryString);

        nextCalled.Should().BeFalse("'{0}' is an XSS payload and must be blocked", queryString);
        statusCode.Should().Be(StatusCodes.Status400BadRequest);
    }
}
