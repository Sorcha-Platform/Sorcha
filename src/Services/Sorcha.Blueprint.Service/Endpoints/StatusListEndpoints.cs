// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Sorcha.Blueprint.Service.Services;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// REST endpoints for W3C Bitstring Status List management.
/// Public GET for verifiers; internal POST/PUT for credential lifecycle.
/// </summary>
public static class StatusListEndpoints
{
    /// <summary>
    /// Maps status list endpoints under /api/v1/credentials/status-lists.
    /// </summary>
    public static void MapStatusListEndpoints(this WebApplication app)
    {
        // Public endpoint — no auth required (verifiers need to check revocation)
        var publicGroup = app.MapGroup("/api/v1/credentials/status-lists")
            .WithTags("StatusLists");

        publicGroup.MapGet("/{listId}", GetStatusList)
            .WithName("GetStatusList")
            .WithSummary("Get a Bitstring Status List Credential (W3C format)")
            .WithDescription(
                "Returns the status list as a W3C BitstringStatusListCredential. " +
                "This endpoint is public and unauthenticated — verifiers use it to check credential revocation/suspension status.")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        // Internal endpoints — service-to-service auth required
        var internalGroup = app.MapGroup("/api/v1/credentials/status-lists")
            .WithTags("StatusLists")
            .RequireAuthorization("CanManageBlueprints");

        internalGroup.MapPost("/{listId}/allocate", AllocateIndex)
            .WithName("AllocateStatusListIndex")
            .WithSummary("Allocate next available index in a status list (internal)")
            .WithDescription("Allocates the next available index for a new credential. Service-to-service auth required.")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        internalGroup.MapPut("/{listId}/bits/{index:int}", SetBit)
            .WithName("SetStatusListBit")
            .WithSummary("Set or clear a bit in a status list (internal)")
            .WithDescription("Sets or clears the bit at a given index. Used by lifecycle operations (revoke, suspend, reinstate).")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> GetStatusList(
        string listId,
        IStatusListManager statusListManager,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var list = await statusListManager.GetListAsync(listId, cancellationToken);
        if (list == null)
            return Results.NotFound(new { error = $"Status list '{listId}' not found" });

        var baseUrl = configuration.GetValue<string>("StatusList:BaseUrl")
            ?? "https://sorcha.example/api/v1/credentials/status-lists";

        // Return W3C BitstringStatusListCredential format
        var response = new
        {
            context = new[] { "https://www.w3.org/ns/credentials/v2" },
            id = $"{baseUrl}/{list.Id}",
            type = new[] { "VerifiableCredential", "BitstringStatusListCredential" },
            issuer = $"did:sorcha:w:{list.IssuerWallet}",
            validFrom = list.LastUpdated,
            credentialSubject = new
            {
                id = $"{baseUrl}/{list.Id}#list",
                type = "BitstringStatusList",
                statusPurpose = list.Purpose,
                encodedList = list.EncodedList
            }
        };

        // Cache-Control: max-age=300 (5 minutes, configurable)
        var maxAge = configuration.GetValue<int>("StatusList:CacheMaxAgeSeconds", 300);
        return Results.Json(response, statusCode: 200, contentType: "application/json")
            is IResult result
            ? new CachedResult(result, maxAge)
            : Results.Ok(response);
    }

    private static async Task<IResult> AllocateIndex(
        string listId,
        [FromBody] AllocateIndexRequest request,
        IStatusListManager statusListManager,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Blueprint.Service.Endpoints.StatusListEndpoints");

        if (string.IsNullOrWhiteSpace(request.CredentialId))
            return Results.BadRequest(new { error = "CredentialId is required" });

        // Get the list to find the issuer/register info
        var list = await statusListManager.GetListAsync(listId, cancellationToken);
        if (list == null)
            return Results.NotFound(new { error = $"Status list '{listId}' not found" });

        try
        {
            var allocation = await statusListManager.AllocateIndexAsync(
                list.IssuerWallet, list.RegisterId, request.CredentialId, cancellationToken);

            logger.LogInformation(
                "Allocated index {Index} in list {ListId} for credential {CredentialId}",
                allocation.Index, allocation.ListId, request.CredentialId);

            return Results.Ok(allocation);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("full"))
        {
            return Results.Conflict(new { error = "Status list is full — all positions allocated" });
        }
    }

    private static async Task<IResult> SetBit(
        string listId,
        int index,
        [FromBody] SetBitRequest request,
        IStatusListManager statusListManager,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Blueprint.Service.Endpoints.StatusListEndpoints");

        try
        {
            var update = await statusListManager.SetBitAsync(
                listId, index, request.Value, request.Reason, cancellationToken);

            logger.LogInformation(
                "Set bit {Index} to {Value} in list {ListId} (v{Version})",
                index, request.Value, listId, update.Version);

            return Results.Ok(update);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = $"Status list '{listId}' not found" });
        }
        catch (ArgumentOutOfRangeException)
        {
            return Results.NotFound(new { error = $"Index {index} is out of range for list '{listId}'" });
        }
    }
}

/// <summary>
/// Wraps an IResult to add Cache-Control header.
/// </summary>
internal class CachedResult : IResult
{
    private readonly IResult _inner;
    private readonly int _maxAgeSeconds;

    public CachedResult(IResult inner, int maxAgeSeconds)
    {
        _inner = inner;
        _maxAgeSeconds = maxAgeSeconds;
    }

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = $"public, max-age={_maxAgeSeconds}";
        await _inner.ExecuteAsync(httpContext);
    }
}

/// <summary>
/// Request to allocate an index in a status list.
/// </summary>
public class AllocateIndexRequest
{
    public required string CredentialId { get; init; }
}

/// <summary>
/// Request to set or clear a bit in a status list.
/// </summary>
public class SetBitRequest
{
    public bool Value { get; init; }
    public string? Reason { get; init; }
}
