// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceDefaults;

/// <summary>
/// Global unhandled-exception handler (issue #1433). Every service registers this via
/// <see cref="Microsoft.Extensions.Hosting.Extensions.AddServiceDefaults{TBuilder}"/> and applies it
/// via <see cref="Microsoft.Extensions.Hosting.Extensions.UseSanitizedExceptionHandling"/>, called as
/// the FIRST middleware in the pipeline so it wraps every other middleware's unhandled exceptions too.
/// </summary>
/// <remarks>
/// Deliberately environment-UNGATED. ASP.NET Core auto-adds the DeveloperExceptionPage — a response
/// body containing the full exception type, message, and stack trace — whenever no exception-handling
/// middleware is registered and <c>ASPNETCORE_ENVIRONMENT=Development</c>. Before this handler existed,
/// Sorcha had no exception-handling middleware at all, so that auto-added page was the live behaviour
/// wherever a node happened to run with <c>Development</c> set — including at least one internet-facing
/// node (issue #1433). A "safe in Development, sanitized elsewhere" posture is not good enough for that
/// failure mode: the sanitized response below must be the ONLY possible unhandled-exception response,
/// in every environment, so a misconfigured node can never leak implementation detail to a caller.
/// Local debugging is unaffected — the exception, full type, message, and stack trace are still logged
/// server-side via <see cref="ILogger"/>; only the HTTP response body is sanitized.
/// </remarks>
public sealed class SanitizedExceptionHandler(ILogger<SanitizedExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = ResolveStatusCode(exception);
        var isClientError = statusCode is >= 400 and < 500;

        // Full detail (exception type, message, stack trace) is for the operator's log only — a caller
        // must never see any of it in the HTTP response.
        //
        // A malformed request is the CALLER's fault, so it is logged at Warning. Logging it at Error
        // buries genuine server faults under a stack trace for every client that omits a field — on a
        // node open to external testers that is the difference between a readable error log and noise
        // (issue #1476).
        var level = isClientError ? LogLevel.Warning : LogLevel.Error;
        logger.Log(
            level,
            exception,
            "Unhandled exception processing {Method} {Path} -> {StatusCode} (TraceId: {TraceId})",
            httpContext.Request.Method,
            httpContext.Request.Path,
            statusCode,
            httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = isClientError
                ? ReasonPhrases.GetReasonPhrase(statusCode)
                : "An error occurred while processing your request.",
            Type = isClientError
                ? "https://tools.ietf.org/html/rfc7231#section-6.5.1"
                : "https://tools.ietf.org/html/rfc7231#section-6.6.1",
        };
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        var problemDetailsService = httpContext.RequestServices.GetService<IProblemDetailsService>();
        if (problemDetailsService is not null)
        {
            return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails
            });
        }

        // IProblemDetailsService is registered by AddServiceDefaults -> AddProblemDetails() on every
        // service, so this branch should not be reachable in practice. Kept as a defensive fallback
        // that stays equally sanitized if that registration is ever missing.
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    /// <summary>
    /// The status an exception is asking for, defaulting to 500 when it is not asking for one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BadHttpRequestException"/> is how ASP.NET Core reports a request the framework
    /// could not read at all — a body that is not valid JSON, a body missing a <c>required</c>
    /// property, a payload over the size limit. It carries its own <see cref="BadHttpRequestException.StatusCode"/>
    /// (400, 413, …), and before this method existed the handler overwrote it with 500 (issue #1476).
    /// </para>
    /// <para>
    /// That is worse than untidy. Clients and agents retry a 500 and do not retry a 400, so telling a
    /// caller "the server broke" when they omitted a field invites a retry storm against a request
    /// that can never succeed — and it did, in practice, mask a malformed probe as a server fault
    /// while #1475 was being diagnosed.
    /// </para>
    /// <para>
    /// Only the STATUS and the log level change. The body stays sanitized either way: the whole point
    /// of #1433 is that no exception type, message or stack trace reaches the caller, and a 4xx must
    /// not become a loophole in that. <see cref="ReasonPhrases"/> yields a fixed, well-known string
    /// ("Bad Request") rather than anything derived from the exception.
    /// </para>
    /// <para>
    /// A non-4xx value is deliberately NOT honoured. This exists to report what the CALLER got wrong;
    /// letting an exception nominate its own 5xx (or a 2xx) would let a failure choose how it is
    /// presented, which is the leak this handler exists to close.
    /// </para>
    /// </remarks>
    private static int ResolveStatusCode(Exception exception) => exception switch
    {
        BadHttpRequestException bad when bad.StatusCode is >= 400 and < 500 => bad.StatusCode,
        _ => StatusCodes.Status500InternalServerError,
    };
}
