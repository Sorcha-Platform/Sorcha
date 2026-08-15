// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
        // Full detail (exception type, message, stack trace) is for the operator's log only — a caller
        // must never see any of it in the HTTP response.
        logger.LogError(
            exception,
            "Unhandled exception processing {Method} {Path} (TraceId: {TraceId})",
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An error occurred while processing your request.",
            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
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
}
