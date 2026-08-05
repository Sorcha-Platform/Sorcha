// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Core.Validators;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Validator.Service.Services.Interfaces;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Endpoints;

/// <summary>
/// API endpoints for transaction validation
/// </summary>
public static class ValidationEndpoints
{
    public static RouteGroupBuilder MapValidationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/validate", ValidateTransaction)
            .WithRequestValidation()
            .WithName("ValidateTransaction")
            .WithSummary("Validate a transaction and submit it to the memory pool")
            .WithDescription("Validates transaction structure, payload hash, wallet signatures, and per-sender sequence number, then submits the transaction to the unverified pool for downstream consensus and docket sealing. Call this when an AI agent needs to record a signed action on a Sorcha register and obtain a verifiable receipt that the validator accepted it.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/mempool/{registerId}", GetMemPoolStats)
            .WithName("GetMemPoolStats")
            .WithSummary("Get memory pool statistics for a register")
            .WithDescription("Returns the current memory-pool state for a register: transaction counts by priority, fill percentage, oldest and newest transaction timestamps. Call this to gauge pending consensus load before submitting time-sensitive transactions or to monitor a register's throughput.")
            .Produces<MemPoolStats>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return group;
    }

    /// <summary>
    /// Validates a transaction and adds it to the memory pool
    /// </summary>
    private static async Task<IResult> ValidateTransaction(
        [FromBody] ValidateTransactionRequest request,
        [FromServices] ITransactionValidator validator,
        [FromServices] ITransactionPoolPoller poolPoller,
        [FromServices] IHashProvider hashProvider,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Validating transaction {TransactionId} for register {RegisterId}",
                request.TransactionId, request.RegisterId);

            // Convert request to transaction model
            var transaction = new Transaction
            {
                TransactionId = request.TransactionId,
                RegisterId = request.RegisterId,
                BlueprintId = request.BlueprintId,
                ActionId = request.ActionId,
                Payload = request.Payload,
                CreatedAt = request.CreatedAt,
                ExpiresAt = request.ExpiresAt,
                Signatures = request.Signatures.Select(s => new RegisterSignature
                {
                    PublicKey = DecodeBase64(s.PublicKey),
                    SignatureValue = DecodeBase64(s.SignatureValue),
                    Algorithm = s.Algorithm,
                    SignedBy = s.SignedBy,
                    SignedAt = request.CreatedAt
                }).ToList(),
                PayloadHash = request.PayloadHash,
                SequenceNumber = (ulong)request.SequenceNumber,
                PreviousTransactionId = request.PreviousTransactionId,
                Priority = request.Priority,
                Metadata = request.Metadata ?? new Dictionary<string, string>(),
                RecipientsWallets = request.RecipientsWallets
            };

            // Participant transactions have no blueprint/action context — use a sentinel
            // value to bypass the TransactionValidator's required-field check for BlueprintId.
            var isParticipantTx = request.Metadata != null &&
                request.Metadata.TryGetValue("Type", out var txType) &&
                string.Equals(txType, "Participant", StringComparison.OrdinalIgnoreCase);
            var effectiveBlueprintId = isParticipantTx ? "participant" : request.BlueprintId;

            // Validate transaction structure
            var signatures = request.Signatures.Select(s =>
                new TransactionSignature(s.PublicKey, s.SignatureValue, s.Algorithm)).ToList();

            var structureValidation = validator.ValidateTransactionStructure(
                request.TransactionId,
                request.RegisterId,
                effectiveBlueprintId,
                request.Payload,
                request.PayloadHash,
                signatures,
                request.CreatedAt);

            if (!structureValidation.IsValid)
            {
                logger.LogWarning("Transaction {TransactionId} failed structure validation", request.TransactionId);
                return Results.BadRequest(new
                {
                    IsValid = false,
                    Errors = structureValidation.Errors.Select(e => new { e.Code, e.Message, e.Field })
                });
            }

            // Validate payload hash
            var payloadValidation = validator.ValidatePayloadHash(request.Payload, request.PayloadHash);
            if (!payloadValidation.IsValid)
            {
                logger.LogWarning("Transaction {TransactionId} failed payload hash validation", request.TransactionId);
                return Results.BadRequest(new
                {
                    IsValid = false,
                    Errors = payloadValidation.Errors.Select(e => new { e.Code, e.Message, e.Field })
                });
            }

            // Validate signatures
            var signatureValidation = validator.ValidateSignatures(signatures, request.TransactionId);
            if (!signatureValidation.IsValid)
            {
                logger.LogWarning("Transaction {TransactionId} failed signature validation", request.TransactionId);
                return Results.BadRequest(new
                {
                    IsValid = false,
                    Errors = signatureValidation.Errors.Select(e => new { e.Code, e.Message, e.Field })
                });
            }

            // Submit to unverified pool (ValidationEngineService will validate and promote to verified queue)
            var added = await poolPoller.SubmitTransactionAsync(request.RegisterId, transaction, cancellationToken);

            if (!added)
            {
                logger.LogWarning("Failed to submit transaction {TransactionId} to unverified pool", request.TransactionId);
                return Results.Conflict(new
                {
                    IsValid = true,
                    Added = false,
                    Message = "Failed to submit transaction to unverified pool (pool full or duplicate)"
                });
            }

            logger.LogInformation("Transaction {TransactionId} validated and submitted to unverified pool", request.TransactionId);

            // Feature 108 — monitoring enrolment is now roster-driven via RegisterMonitoringBootstrap.
            // No per-submission registration call here. Nodes that are not on the register's
            // validator roster accept the transaction into their mempool for onward forwarding
            // but do not produce dockets.
            return Results.Ok(new
            {
                IsValid = true,
                Added = true,
                TransactionId = request.TransactionId,
                RegisterId = request.RegisterId,
                AddedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating transaction {TransactionId}", request.TransactionId);
            return Results.Problem(
                title: "Internal server error",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    /// <summary>
    /// Gets memory pool statistics for a register
    /// </summary>
    private static async Task<IResult> GetMemPoolStats(
        string registerId,
        [FromServices] IMemPoolManager memPoolManager,
        CancellationToken cancellationToken)
    {
        var stats = await memPoolManager.GetStatsAsync(registerId, cancellationToken);
        return Results.Ok(stats);
    }

    /// <summary>
    /// Decodes a Base64 or Base64URL encoded string to bytes.
    /// Handles both standard Base64 (+/=) and URL-safe Base64 (-_) encodings.
    /// </summary>
    private static byte[] DecodeBase64(string value)
    {
        try
        {
            return Base64Url.DecodeFromChars(value);
        }
        catch (FormatException)
        {
            return Convert.FromBase64String(value);
        }
    }
}

/// <summary>
/// Request model for transaction validation
/// </summary>
public record ValidateTransactionRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(256)]
    public required string TransactionId { get; init; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(256)]
    public required string RegisterId { get; init; }

    [StringLength(256)]
    public string? BlueprintId { get; init; }

    [StringLength(256)]
    public string? ActionId { get; init; }

    public required JsonElement Payload { get; init; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(1024)]
    public required string PayloadHash { get; init; }

    [Required]
    public required List<SignatureRequest> Signatures { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }

    [StringLength(256)]
    public string? PreviousTransactionId { get; init; }

    public TransactionPriority Priority { get; init; } = TransactionPriority.Normal;
    public Dictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Per-sender monotonic sequence number for replay protection (SEC-AUDIT 4.2).
    /// Must equal sender's last sequence number + 1 on the target register.
    /// </summary>
    [Range(0, long.MaxValue)]
    public long SequenceNumber { get; init; }

    /// <summary>
    /// Recipient wallet addresses extracted from disclosure groups at transaction
    /// build time. Passed through to the Register Service so docket-sealed
    /// transactions can be routed to recipient Wallet Services.
    /// </summary>
    public List<string>? RecipientsWallets { get; init; }
}

/// <summary>
/// Signature in request
/// </summary>
public record SignatureRequest
{
    [Required(AllowEmptyStrings = false)]
    [StringLength(8192)]
    public required string PublicKey { get; init; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(8192)]
    public required string SignatureValue { get; init; }

    [Required(AllowEmptyStrings = false)]
    [StringLength(64)]
    public required string Algorithm { get; init; }

    [StringLength(256)]
    public string? SignedBy { get; init; }
}

