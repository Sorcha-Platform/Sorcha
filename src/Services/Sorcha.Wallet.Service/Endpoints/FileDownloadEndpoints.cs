// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// REST endpoints for decrypting and downloading multi-chunk file attachments via the Wallet Service.
/// The Wallet Service mediates the download: it unwraps the recipient's master file key,
/// derives per-chunk keys via HKDF-SHA256, decrypts each chunk with XChaCha20-Poly1305,
/// and streams the reassembled plaintext to the caller.
/// </summary>
public static class FileDownloadEndpoints
{
    /// <summary>
    /// Maps all file download endpoints onto the provided route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapFileDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/wallets/{address}/files")
            .WithTags("FileDownload")
            .RequireAuthorization();

        group.MapGet("/download", DownloadFile)
            .WithName("DownloadFile")
            .WithSummary("Download and decrypt a file attachment from a register transaction")
            .WithDescription(
                "Fetches the encrypted chunks of a file attachment from the Register Service, " +
                "unwraps the master file key using the wallet's private key, derives per-chunk " +
                "XChaCha20-Poly1305 keys via HKDF-SHA256, decrypts every chunk, reassembles the " +
                "file, and streams the result. The SHA-256 hash of the reassembled plaintext is " +
                "verified before the response is committed. " +
                "The requesting JWT subject must own the wallet specified in {address}.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>
    /// Downloads and decrypts a file attachment.
    /// </summary>
    private static async Task<IResult> DownloadFile(
        string address,
        [FromQuery] string? registerId,
        [FromQuery] string? actionTxId,
        [FromQuery] string? fieldName,
        [FromQuery] int? fileIndex,
        IFileReassemblyService reassemblyService,
        IWalletRepository walletRepository,
        HttpContext context,
        ILogger<Program> logger,
        CancellationToken cancellationToken = default)
    {
        // Validate required query parameters
        if (string.IsNullOrWhiteSpace(registerId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Missing Parameter",
                Detail = "Query parameter 'registerId' is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(actionTxId))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Missing Parameter",
                Detail = "Query parameter 'actionTxId' is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Missing Parameter",
                Detail = "Query parameter 'fieldName' is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // fileIndex defaults to 0 when not supplied (nullable int with no value = first file)
        var resolvedFileIndex = fileIndex ?? 0;
        if (resolvedFileIndex < 0)
        {
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Parameter",
                Detail = "Query parameter 'fileIndex' must be 0 or greater.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Ownership check: JWT subject must own the wallet
        var jwtSubject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? context.User.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(jwtSubject))
        {
            return Results.Unauthorized();
        }

        var wallet = await walletRepository.GetByAddressAsync(
            address, false, false, false, cancellationToken);

        if (wallet is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "Wallet Not Found",
                Detail = $"No wallet found with address '{address}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (!string.Equals(wallet.Owner, jwtSubject, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Forbidden: JWT subject {Subject} attempted to download file from wallet {Address} owned by {Owner}",
                jwtSubject, address, wallet.Owner);
            return Results.Forbid();
        }

        // Prepare the download (fetch, decrypt, assemble)
        FileDownloadResult? downloadResult;
        try
        {
            downloadResult = await reassemblyService.PrepareDownloadAsync(
                address,
                registerId,
                actionTxId,
                fieldName,
                resolvedFileIndex,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to prepare download for wallet {Address}, action {ActionTxId}, field {FieldName}[{FileIndex}]",
                address, actionTxId, fieldName, resolvedFileIndex);
            return Results.Problem(
                title: "Download Preparation Failed",
                detail: "An error occurred while fetching or decrypting the file. " +
                        "The file may not be accessible with this wallet.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        if (downloadResult is null)
        {
            return Results.NotFound(new ProblemDetails
            {
                Title = "File Not Found",
                Detail = $"No file found at field '{fieldName}' (index {resolvedFileIndex}) " +
                         $"in transaction '{actionTxId}', or wallet '{address}' is not an authorised recipient.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Verify integrity by writing to a temporary buffer first, then stream to the client.
        // The integrity check (SHA-256 comparison) must complete before we commit the response
        // headers, so we cannot stream directly to the response body. However, we avoid holding
        // a second copy of the bytes by passing the verified buffer as a stream to Results.Stream.
        logger.LogInformation(
            "Preparing {FileName} ({ContentType}, {Size} bytes) for wallet {Address}",
            downloadResult.FileName, downloadResult.ContentType, downloadResult.Size, address);

        using var buffer = new MemoryStream();
        try
        {
            await downloadResult.WriteToStreamAsync(buffer, cancellationToken);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("integrity check failed"))
        {
            logger.LogError(ex,
                "Integrity check FAILED for {FileName}, wallet {Address}",
                downloadResult.FileName, address);
            return Results.UnprocessableEntity(new ProblemDetails
            {
                Title = "File Integrity Check Failed",
                Detail = $"The SHA-256 hash of '{downloadResult.FileName}' does not match " +
                         "the value recorded in the transaction. The file may have been tampered with.",
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }

        logger.LogInformation(
            "Streaming {FileName} ({ContentType}, {Size} bytes) to wallet {Address}",
            downloadResult.FileName, downloadResult.ContentType, buffer.Length, address);

        buffer.Position = 0;
        return Results.Stream(
            buffer,
            contentType: downloadResult.ContentType,
            fileDownloadName: downloadResult.FileName,
            enableRangeProcessing: false);
    }
}
