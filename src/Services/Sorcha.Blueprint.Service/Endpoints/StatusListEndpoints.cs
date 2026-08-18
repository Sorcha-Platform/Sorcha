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

        // IETF Token Status List endpoint (spec 095) — HAIP-conformant envelope
        var ietfPublicGroup = app.MapGroup("/api/v1/credentials/ietf-status-lists")
            .WithTags("StatusLists");

        ietfPublicGroup.MapGet("/{listId}", GetIetfStatusList)
            .WithName("GetIetfStatusList")
            .WithSummary("Get a Token Status List (IETF format)")
            .WithDescription(
                "Returns the status list as a signed JWT per the IETF Token Status List draft. " +
                "HAIP-conformant verifiers use this endpoint. The underlying bitstring is shared with the W3C endpoint.")
            .Produces<string>(StatusCodes.Status200OK, "application/statuslist+jwt")
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
        Sorcha.Blueprint.Service.Configuration.StatusListUrls.Resolved urls,
        CancellationToken cancellationToken)
    {
        var list = await statusListManager.GetListAsync(listId, cancellationToken);
        if (list == null)
            return Results.NotFound(new { error = $"Status list '{listId}' not found" });

        var baseUrl = urls.BaseUrl;

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

    private static async Task<IResult> GetIetfStatusList(
        string listId,
        IStatusListManager statusListManager,
        IIetfTokenStatusListSerializer serializer,
        IConfiguration configuration,
        Sorcha.Blueprint.Service.Configuration.StatusListUrls.Resolved urls,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Blueprint.Service.Endpoints.StatusListEndpoints");

        var list = await statusListManager.GetListAsync(listId, cancellationToken);
        if (list == null)
            return Results.NotFound(new { error = $"Status list '{listId}' not found" });

        var rawBytes = await statusListManager.GetRawBitstringBytesAsync(listId, cancellationToken);
        if (rawBytes == null)
            return Results.NotFound(new { error = $"Status list '{listId}' bitstring not available" });

        // IETF models suspension as a VALUE inside one list, where W3C uses a separate list per
        // purpose. So the IETF view is a PROJECTION of Sorcha's two 1-bit lists into one 2-bit
        // array — never the 1-bit array relabelled, which would make a reader take entry N from
        // bits 2N..2N+1 and report a status for a credential nobody touched.
        var bitsPerEntry = 1;
        var suspensionListId = CredentialEndpoints.RetargetListIdToPurpose(listId, "suspension");
        var suspensionList = await statusListManager.GetListAsync(suspensionListId, cancellationToken);

        if (suspensionList is not null)
        {
            var revocationListId = CredentialEndpoints.RetargetListIdToPurpose(listId, "revocation");
            var revocationList = await statusListManager.GetListAsync(revocationListId, cancellationToken)
                                 ?? list;

            bitsPerEntry = 2;
            rawBytes = IetfStatusListPacker.PackTwoBit(revocationList, suspensionList, revocationList.Size);
        }

        // Build the full sub URL per IETF Token Status List spec
        var subUrl = $"{urls.IetfBaseUrl}/{listId}";

        // Signing key: configurable for production, ephemeral fallback for dev.
        // The ephemeral key fallback is intentional for pre-release — no startup validation
        // is needed until the production deployment guide exists. The runtime warning below
        // is sufficient to flag misconfiguration during development.
        // TODO(095): Wire real signing key from issuer wallet via IHaipIssuerCoKeyService
        var signingKeyBase64 = configuration.GetValue<string>("StatusList:IetfSigningKey");
        var algorithm = configuration.GetValue<string>("StatusList:IetfSigningAlgorithm") ?? "ES256";

        byte[] signingKey;
        if (!string.IsNullOrWhiteSpace(signingKeyBase64))
        {
            signingKey = Convert.FromBase64String(signingKeyBase64);
        }
        else
        {
            logger.LogWarning(
                "IETF Token Status List endpoint using ephemeral signing key for list {ListId}. " +
                "Set StatusList:IetfSigningKey in production — JWTs signed with ephemeral keys are unverifiable.",
                listId);
            using var ecdsa = System.Security.Cryptography.ECDsa.Create(
                System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
            signingKey = ecdsa.ExportECPrivateKey();
        }

        var issuerDid = $"did:sorcha:org:{list.IssuerWallet}";
        var maxAge = configuration.GetValue<int>("StatusList:CacheMaxAgeSeconds", 300);
        var jwt = serializer.Serialize(rawBytes, subUrl, issuerDid, bitsPerEntry, signingKey, algorithm, maxAge);

        // Return as application/statuslist+jwt with cache headers
        var result = Results.Text(jwt, "application/statuslist+jwt", statusCode: 200);
        return new CachedResult(result, maxAge);
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
