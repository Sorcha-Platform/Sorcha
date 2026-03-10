// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Endpoints;

/// <summary>
/// API endpoints for validator registration and management
/// </summary>
public static class ValidatorRegistrationEndpoints
{
    /// <summary>
    /// Maps validator registration endpoints
    /// </summary>
    public static RouteGroupBuilder MapValidatorRegistrationEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/register", RegisterValidator)
            .WithName("RegisterValidator")
            .WithSummary("Register as a validator for a register")
            .WithDescription("Registers this validator node for participation in consensus. In public mode, registration is immediate. In consent mode, registration is pending until approved.")
            .Produces<object>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{registerId}", GetValidators)
            .WithName("GetValidators")
            .WithSummary("Get validators for a register")
            .WithDescription("Returns all active validators registered for the specified register")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{registerId}/pending", GetPendingValidators)
            .WithName("GetPendingValidators")
            .WithSummary("Get pending validators awaiting approval")
            .WithDescription("Returns validators with pending status awaiting approval (consent mode only)")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{registerId}/{validatorId}", GetValidator)
            .WithName("GetValidator")
            .WithSummary("Get validator details")
            .WithDescription("Returns details for a specific validator")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{registerId}/count", GetValidatorCount)
            .WithName("GetValidatorCount")
            .WithSummary("Get active validator count")
            .WithDescription("Returns the number of active validators for the register")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{registerId}/{validatorId}/approve", ApproveValidator)
            .WithName("ApproveValidator")
            .WithSummary("Approve a pending validator")
            .WithDescription("Approves a pending validator registration (consent mode only). Requires register owner authorization.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{registerId}/{validatorId}/reject", RejectValidator)
            .WithName("RejectValidator")
            .WithSummary("Reject a pending validator")
            .WithDescription("Rejects a pending validator registration (consent mode only). Requires register owner authorization.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{registerId}/refresh", RefreshValidators)
            .WithName("RefreshValidators")
            .WithSummary("Refresh validator list from chain")
            .WithDescription("Forces a refresh of the validator list from the transaction chain")
            .Produces<object>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return group;
    }

    /// <summary>
    /// Register as a validator
    /// </summary>
    private static async Task<IResult> RegisterValidator(
        [FromBody] RegisterValidatorRequest request,
        [FromServices] IValidatorRegistry registry,
        [FromServices] IGenesisConfigService genesisConfig,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation(
                "Processing validator registration for {ValidatorId} on register {RegisterId}",
                request.ValidatorId, request.RegisterId);

            // Check registration mode
            var validatorConfig = await genesisConfig.GetValidatorConfigAsync(request.RegisterId, cancellationToken);

            var registration = new ValidatorRegistration
            {
                ValidatorId = request.ValidatorId,
                PublicKey = request.PublicKey,
                GrpcEndpoint = request.GrpcEndpoint,
                Metadata = request.Metadata
            };

            var result = await registry.RegisterAsync(request.RegisterId, registration, cancellationToken);

            if (!result.Success)
            {
                logger.LogWarning(
                    "Validator registration failed for {ValidatorId}: {Error}",
                    request.ValidatorId, result.ErrorMessage);
                return Results.BadRequest(new
                {
                    error = "Registration failed",
                    message = result.ErrorMessage
                });
            }

            // Determine status based on registration mode
            var status = validatorConfig.IsPublicRegistration ? "active" : "pending";

            logger.LogInformation(
                "Validator {ValidatorId} registered for register {RegisterId} (status: {Status}, order: {Order})",
                request.ValidatorId, request.RegisterId, status, result.OrderIndex);

            var response = new
            {
                validatorId = request.ValidatorId,
                registerId = request.RegisterId,
                transactionId = result.TransactionId,
                orderIndex = result.OrderIndex,
                status,
                message = validatorConfig.IsPublicRegistration
                    ? "Registration successful"
                    : "Registration pending approval. Contact register owner for approval."
            };

            return Results.Created($"/api/validators/{request.RegisterId}/{request.ValidatorId}", response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error registering validator {ValidatorId}", request.ValidatorId);
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Registration error");
        }
    }

    /// <summary>
    /// Get all validators for a register
    /// </summary>
    private static async Task<IResult> GetValidators(
        string registerId,
        [FromServices] IValidatorRegistry registry,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var validators = await registry.GetActiveValidatorsAsync(registerId, cancellationToken);

            return Results.Ok(new
            {
                registerId,
                count = validators.Count,
                validators = validators.Select(v => new
                {
                    validatorId = v.ValidatorId,
                    publicKey = v.PublicKey,
                    grpcEndpoint = v.GrpcEndpoint,
                    status = v.Status.ToString().ToLowerInvariant(),
                    registeredAt = v.RegisteredAt,
                    orderIndex = v.OrderIndex
                })
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting validators for register {RegisterId}", registerId);
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Query error");
        }
    }

    /// <summary>
    /// Get a specific validator
    /// </summary>
    private static async Task<IResult> GetValidator(
        string registerId,
        string validatorId,
        [FromServices] IValidatorRegistry registry,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var validator = await registry.GetValidatorAsync(registerId, validatorId, cancellationToken);

            if (validator == null)
            {
                return Results.NotFound(new
                {
                    error = "Validator not found",
                    validatorId,
                    registerId
                });
            }

            return Results.Ok(new
            {
                validatorId = validator.ValidatorId,
                registerId,
                publicKey = validator.PublicKey,
                grpcEndpoint = validator.GrpcEndpoint,
                status = validator.Status.ToString().ToLowerInvariant(),
                registeredAt = validator.RegisteredAt,
                orderIndex = validator.OrderIndex,
                registrationTxId = validator.RegistrationTxId,
                metadata = validator.Metadata
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting validator {ValidatorId} for register {RegisterId}",
                validatorId, registerId);
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Query error");
        }
    }

    /// <summary>
    /// Get active validator count
    /// </summary>
    private static async Task<IResult> GetValidatorCount(
        string registerId,
        [FromServices] IValidatorRegistry registry,
        [FromServices] IGenesisConfigService genesisConfig,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var count = await registry.GetActiveCountAsync(registerId, cancellationToken);
            var config = await genesisConfig.GetValidatorConfigAsync(registerId, cancellationToken);

            return Results.Ok(new
            {
                registerId,
                activeCount = count,
                minValidators = config.MinValidators,
                maxValidators = config.MaxValidators,
                hasQuorum = count >= config.MinValidators
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting validator count for register {RegisterId}", registerId);
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Query error");
        }
    }

    /// <summary>
    /// Force refresh validator list
    /// </summary>
    private static async Task<IResult> RefreshValidators(
        string registerId,
        [FromServices] IValidatorRegistry registry,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Refreshing validator list for register {RegisterId}", registerId);

            await registry.RefreshAsync(registerId, cancellationToken);

            var count = await registry.GetActiveCountAsync(registerId, cancellationToken);

            return Results.Ok(new
            {
                registerId,
                refreshed = true,
                activeCount = count
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error refreshing validators for register {RegisterId}", registerId);
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Refresh error");
        }
    }

    /// <summary>
    /// Get pending validators awaiting approval
    /// </summary>
    private static async Task<IResult> GetPendingValidators(
        string registerId,
        [FromServices] IValidatorRegistry registry,
        [FromServices] IGenesisConfigService genesisConfig,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var validatorConfig = await genesisConfig.GetValidatorConfigAsync(registerId, cancellationToken);
            var pendingValidators = await registry.GetPendingValidatorsAsync(registerId, cancellationToken);

            return Results.Ok(new
            {
                registerId,
                registrationMode = validatorConfig.RegistrationMode,
                count = pendingValidators.Count,
                validators = pendingValidators.Select(v => new
                {
                    validatorId = v.ValidatorId,
                    publicKey = v.PublicKey,
                    grpcEndpoint = v.GrpcEndpoint,
                    status = v.Status.ToString().ToLowerInvariant(),
                    registeredAt = v.RegisteredAt,
                    orderIndex = v.OrderIndex,
                    metadata = v.Metadata
                })
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting pending validators for register {RegisterId}", registerId);
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Query error");
        }
    }

    /// <summary>
    /// Approve a pending validator
    /// </summary>
    private static async Task<IResult> ApproveValidator(
        string registerId,
        string validatorId,
        [FromBody] ApproveValidatorRequest request,
        [FromServices] IValidatorRegistry registry,
        [FromServices] IGenesisConfigService genesisConfig,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation(
                "Processing approval for validator {ValidatorId} on register {RegisterId} by {ApprovedBy}",
                validatorId, registerId, request.ApprovedBy);

            // Check registration mode
            var validatorConfig = await genesisConfig.GetValidatorConfigAsync(registerId, cancellationToken);
            if (validatorConfig.IsPublicRegistration)
            {
                return Results.BadRequest(new
                {
                    error = "Approval not required",
                    message = "This register uses public registration mode. Validators are automatically approved."
                });
            }

            var approvalRequest = new ValidatorApprovalRequest
            {
                ValidatorId = validatorId,
                ApprovedBy = request.ApprovedBy,
                ApprovalNotes = request.ApprovalNotes
            };

            var result = await registry.ApproveValidatorAsync(registerId, approvalRequest, cancellationToken);

            if (!result.Success)
            {
                logger.LogWarning(
                    "Validator approval failed for {ValidatorId}: {Error}",
                    validatorId, result.ErrorMessage);
                return Results.BadRequest(new
                {
                    error = "Approval failed",
                    message = result.ErrorMessage
                });
            }

            logger.LogInformation(
                "Validator {ValidatorId} approved for register {RegisterId}",
                validatorId, registerId);

            return Results.Ok(new
            {
                validatorId,
                registerId,
                status = "active",
                transactionId = result.TransactionId,
                orderIndex = result.OrderIndex,
                approvedAt = result.ApprovedAt,
                approvedBy = request.ApprovedBy
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error approving validator {ValidatorId}", validatorId);
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Approval error");
        }
    }

    /// <summary>
    /// Reject a pending validator
    /// </summary>
    private static async Task<IResult> RejectValidator(
        string registerId,
        string validatorId,
        [FromBody] RejectValidatorRequest request,
        [FromServices] IValidatorRegistry registry,
        [FromServices] IGenesisConfigService genesisConfig,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation(
                "Processing rejection for validator {ValidatorId} on register {RegisterId} by {RejectedBy}",
                validatorId, registerId, request.RejectedBy);

            // Check registration mode
            var validatorConfig = await genesisConfig.GetValidatorConfigAsync(registerId, cancellationToken);
            if (validatorConfig.IsPublicRegistration)
            {
                return Results.BadRequest(new
                {
                    error = "Rejection not applicable",
                    message = "This register uses public registration mode. Use validator removal instead."
                });
            }

            var success = await registry.RejectValidatorAsync(
                registerId, validatorId, request.Reason, request.RejectedBy, cancellationToken);

            if (!success)
            {
                logger.LogWarning(
                    "Validator rejection failed for {ValidatorId}",
                    validatorId);
                return Results.BadRequest(new
                {
                    error = "Rejection failed",
                    message = "Validator not found or not in pending status"
                });
            }

            logger.LogInformation(
                "Validator {ValidatorId} rejected for register {RegisterId}",
                validatorId, registerId);

            return Results.Ok(new
            {
                validatorId,
                registerId,
                status = "rejected",
                reason = request.Reason,
                rejectedBy = request.RejectedBy,
                rejectedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error rejecting validator {ValidatorId}", validatorId);
            return Results.Problem(
                detail: ex.Message,
                statusCode: 500,
                title: "Rejection error");
        }
    }
}

/// <summary>
/// Request to register as a validator
/// </summary>
public record RegisterValidatorRequest
{
    /// <summary>Register ID to join</summary>
    public required string RegisterId { get; init; }

    /// <summary>Validator's unique identifier (wallet address)</summary>
    public required string ValidatorId { get; init; }

    /// <summary>Validator's public key for signature verification</summary>
    public required string PublicKey { get; init; }

    /// <summary>gRPC endpoint for peer communication</summary>
    public required string GrpcEndpoint { get; init; }

    /// <summary>Optional metadata</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Request to approve a pending validator
/// </summary>
public record ApproveValidatorRequest
{
    /// <summary>Wallet address of approver (register owner)</summary>
    public required string ApprovedBy { get; init; }

    /// <summary>Optional approval notes</summary>
    public string? ApprovalNotes { get; init; }
}

/// <summary>
/// Request to reject a pending validator
/// </summary>
public record RejectValidatorRequest
{
    /// <summary>Wallet address of rejector (register owner)</summary>
    public required string RejectedBy { get; init; }

    /// <summary>Reason for rejection</summary>
    public required string Reason { get; init; }
}
