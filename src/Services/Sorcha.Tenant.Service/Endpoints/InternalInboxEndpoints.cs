// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Internal endpoint for service-principal callers to write inbox entries on
/// behalf of users. Phase 5 (US3) of Feature 118.
/// </summary>
/// <remarks>
/// Idempotent on <c>(PlatformUserId, SourceEventId)</c>. A duplicate POST
/// returns the original entry with HTTP 200 + <c>Idempotent:true</c>; the
/// first call returns HTTP 201 Created.
/// </remarks>
public static class InternalInboxEndpoints
{
    /// <summary>Maps <c>POST /api/internal/inbox</c> with the RequireService policy.</summary>
    public static IEndpointRouteBuilder MapInternalInboxEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/internal/inbox", WriteAsync)
            .RequireAuthorization("RequireService")
            .WithName("WriteInboxEntry")
            .WithTags("Internal", "Inbox")
            .WithSummary("Write an inbox entry on behalf of a user. Idempotent on (PlatformUserId, SourceEventId).");

        return app;
    }

    private static async Task<IResult> WriteAsync(
        HttpContext context,
        IInboxService service,
        InboxWriteRequestDto request,
        CancellationToken ct)
    {
        if (request is null)
        {
            return Results.BadRequest(new { error = "request body required" });
        }
        if (request.PlatformUserId == Guid.Empty)
        {
            return Results.BadRequest(new { error = "platformUserId required" });
        }
        if (string.IsNullOrWhiteSpace(request.CorrelationKey))
        {
            return Results.BadRequest(new { error = "correlationKey required" });
        }
        if (string.IsNullOrWhiteSpace(request.DetailHref) || !request.DetailHref.StartsWith("/api/", StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "detailHref must start with /api/" });
        }
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
        {
            return Results.BadRequest(new { error = "title required, ≤ 200 chars" });
        }

        // #1506 — PlatformUserId is a FOREIGN KEY, so an id that is merely well-formed reaches
        // Postgres and throws. The resulting 500 is caused entirely by the caller's argument, and
        // on n1 a run of them tripped a circuit breaker that then blocked credential issuance
        // outright: a best-effort notification write took out the operation it was describing.
        // Refusing here keeps a caller's bug looking like a caller's bug.
        if (!await service.PlatformUserExistsAsync(request.PlatformUserId, ct))
        {
            return Results.BadRequest(new
            {
                error = "platformUserId does not name a known platform user",
                platformUserId = request.PlatformUserId
            });
        }

        var write = new InboxWriteRequest(
            PlatformUserId: request.PlatformUserId,
            Category: request.Category,
            Severity: request.Severity,
            CorrelationKey: request.CorrelationKey,
            DetailHref: request.DetailHref,
            SourceEventId: request.SourceEventId == Guid.Empty ? Guid.NewGuid() : request.SourceEventId,
            OccurredAt: request.OccurredAt == default ? DateTimeOffset.UtcNow : request.OccurredAt,
            Title: request.Title,
            Summary: request.Summary,
            IconKey: request.IconKey,
            ChannelHints: request.ChannelHints,
            WriterServiceId: request.WriterServiceId);

        var result = await service.WriteAsync(write, ct);
        if (result.IsIdempotent)
        {
            return Results.Ok(new InboxEntryResponseDto(result.Entry, Idempotent:true));
        }
        return Results.Created($"/api/me/inbox/{result.Entry.Id:N}", new InboxEntryResponseDto(result.Entry, Idempotent:false));
    }
}

/// <summary>Wire shape for <see cref="InternalInboxEndpoints.MapInternalInboxEndpoints"/>.</summary>
public sealed record InboxWriteRequestDto(
    Guid PlatformUserId,
    InboxCategory Category,
    InboxSeverity Severity,
    string CorrelationKey,
    string DetailHref,
    Guid SourceEventId,
    DateTimeOffset OccurredAt,
    string Title,
    string? Summary = null,
    string? IconKey = null,
    ChannelHints? ChannelHints = null,
    Guid? WriterServiceId = null);

/// <summary>Wire shape returned by the internal write endpoint.</summary>
public sealed record InboxEntryResponseDto(InboxEntry Entry, bool Idempotent);
