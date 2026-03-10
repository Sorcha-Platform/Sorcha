// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Core.Validators;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Endpoints;

/// <summary>
/// API endpoints for transaction validation
/// </summary>
public static class ValidationEndpoints
{
    public static RouteGroupBuilder MapValidationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/validate", ValidateTransaction)
            .WithName("ValidateTransaction")
            .WithSummary("Validates a transaction and adds it to the memory pool")
            .WithDescription("Validates transaction structure, signatures, and blueprint compliance before adding to memory pool")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/mempool/{registerId}", GetMemPoolStats)
            .WithName("GetMemPoolStats")
            .WithSummary("Gets memory pool statistics for a register")
            .WithDescription("Returns current transaction counts, fill percentage, and other memory pool metrics")
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
        [FromServices] IRegisterMonitoringRegistry monitoringRegistry,
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
                Signatures = request.Signatures.Select(s => new Signature
                {
                    PublicKey = Base64Url.DecodeFromChars(s.PublicKey),
                    SignatureValue = Base64Url.DecodeFromChars(s.SignatureValue),
                    Algorithm = s.Algorithm,
                    SignedAt = request.CreatedAt
                }).ToList(),
                PayloadHash = request.PayloadHash,
                PreviousTransactionId = request.PreviousTransactionId,
                Priority = request.Priority,
                Metadata = request.Metadata ?? new Dictionary<string, string>()
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

            // Register for docket building so DocketBuildTriggerService polls this register
            monitoringRegistry.RegisterForMonitoring(request.RegisterId);

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
}

/// <summary>
/// Request model for transaction validation
/// </summary>
public record ValidateTransactionRequest
{
    public required string TransactionId { get; init; }
    public required string RegisterId { get; init; }
    public string? BlueprintId { get; init; }
    public string? ActionId { get; init; }
    public required JsonElement Payload { get; init; }
    public required string PayloadHash { get; init; }
    public required List<SignatureRequest> Signatures { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? PreviousTransactionId { get; init; }
    public TransactionPriority Priority { get; init; } = TransactionPriority.Normal;
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Signature in request
/// </summary>
public record SignatureRequest
{
    public required string PublicKey { get; init; }
    public required string SignatureValue { get; init; }
    public required string Algorithm { get; init; }
}

