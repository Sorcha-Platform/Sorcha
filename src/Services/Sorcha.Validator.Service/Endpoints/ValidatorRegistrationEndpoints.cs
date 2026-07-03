// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Validator.Service.Services;
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
            .WithRequestValidation()
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
            .WithRequestValidation()
            .WithName("ApproveValidator")
            .WithSummary("Approve a pending validator")
            .WithDescription("Approves a pending validator registration (consent mode only). Requires register owner authorization.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{registerId}/{validatorId}/reject", RejectValidator)
            .WithRequestValidation()
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

        group.MapPost("/{registerId}/{validatorId}/suspend", SuspendValidator)
            .WithRequestValidation()
            .WithName("SuspendValidator")
            .WithSummary("Suspend an active validator")
            .WithDescription("Suspends an active validator, preventing consensus participation. Cannot suspend the last active validator. Requires SystemAdmin authorization.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{registerId}/{validatorId}/reactivate", ReactivateValidator)
            .WithRequestValidation()
            .WithName("ReactivateValidator")
            .WithSummary("Reactivate a suspended validator")
            .WithDescription("Reactivates a previously suspended validator. Only valid from Suspended state. Requires SystemAdmin authorization.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{registerId}/{validatorId}/revoke", RevokeValidator)
            .WithRequestValidation()
            .WithName("RevokeValidator")
            .WithSummary("Permanently revoke a validator")
            .WithDescription("Permanently revokes a validator (terminal state). Cannot be re-activated. Cannot revoke the last active validator. Requires SystemAdmin authorization.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{registerId}/sequence/{walletAddress}", GetSequenceNumber)
            .WithName("GetSequenceNumber")
            .WithSummary("Get wallet sequence number")
            .WithDescription("Returns the current and next sequence number for a wallet on a register. Used by clients to determine the correct sequence number for their next transaction.")
            .Produces<object>(StatusCodes.Status200OK);

        group.MapGet("/{registerId}/audit", GetAuditTrail)
            .WithName("GetValidatorAuditTrail")
            .WithSummary("Get validator audit trail")
            .WithDescription("Returns audit trail of all validator state transitions for a register. Supports filtering by validator and pagination.")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience")
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

    /// <summary>
    /// Suspend an active validator
    /// </summary>
    private static async Task<IResult> SuspendValidator(
        string registerId,
        string validatorId,
        [FromBody] SuspendValidatorRequest request,
        [FromServices] IValidatorRegistry registry,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var success = await registry.SuspendValidatorAsync(
                registerId, validatorId, request.SuspendedBy, request.Reason, cancellationToken);

            if (!success)
            {
                return Results.BadRequest(new
                {
                    error = "Suspension failed",
                    message = "Validator not found, not active, or is the last active validator"
                });
            }

            return Results.Ok(new
            {
                validatorId,
                registerId,
                status = "suspended",
                suspendedAt = DateTimeOffset.UtcNow,
                suspendedBy = request.SuspendedBy
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error suspending validator {ValidatorId}", validatorId);
            return Results.Problem(detail: ex.Message, statusCode: 500, title: "Suspension error");
        }
    }

    /// <summary>
    /// Reactivate a suspended validator
    /// </summary>
    private static async Task<IResult> ReactivateValidator(
        string registerId,
        string validatorId,
        [FromBody] ReactivateValidatorRequest request,
        [FromServices] IValidatorRegistry registry,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var success = await registry.ReactivateValidatorAsync(
                registerId, validatorId, request.ReactivatedBy, request.Notes, cancellationToken);

            if (!success)
            {
                return Results.BadRequest(new
                {
                    error = "Reactivation failed",
                    message = "Validator not found or not in Suspended state"
                });
            }

            return Results.Ok(new
            {
                validatorId,
                registerId,
                status = "active",
                reactivatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reactivating validator {ValidatorId}", validatorId);
            return Results.Problem(detail: ex.Message, statusCode: 500, title: "Reactivation error");
        }
    }

    /// <summary>
    /// Permanently revoke a validator
    /// </summary>
    private static async Task<IResult> RevokeValidator(
        string registerId,
        string validatorId,
        [FromBody] RevokeValidatorRequest request,
        [FromServices] IValidatorRegistry registry,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var success = await registry.RevokeValidatorAsync(
                registerId, validatorId, request.RevokedBy, request.Reason, cancellationToken);

            if (!success)
            {
                return Results.BadRequest(new
                {
                    error = "Revocation failed",
                    message = "Validator not found, already revoked, or is the last active validator"
                });
            }

            return Results.Ok(new
            {
                validatorId,
                registerId,
                status = "revoked",
                revokedAt = DateTimeOffset.UtcNow,
                revokedBy = request.RevokedBy
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error revoking validator {ValidatorId}", validatorId);
            return Results.Problem(detail: ex.Message, statusCode: 500, title: "Revocation error");
        }
    }

    /// <summary>
    /// Get wallet sequence number for replay protection
    /// </summary>
    private static async Task<IResult> GetSequenceNumber(
        string registerId,
        string walletAddress,
        [FromServices] IWalletSequenceRepository sequenceRepo,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var lastSeq = await sequenceRepo.GetSequenceNumberAsync(registerId, walletAddress, cancellationToken);
            return Results.Ok(new
            {
                registerId,
                walletAddress,
                lastSequenceNumber = lastSeq,
                nextSequenceNumber = lastSeq + 1
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting sequence number for wallet {WalletAddress} on register {RegisterId}",
                walletAddress, registerId);
            return Results.Problem(detail: "An error occurred retrieving the sequence number.", statusCode: 500, title: "Sequence query error");
        }
    }

    /// <summary>
    /// Get validator audit trail
    /// </summary>
    private static async Task<IResult> GetAuditTrail(
        string registerId,
        [FromQuery] string? validatorId,
        [FromQuery] int limit,
        [FromQuery] int offset,
        [FromServices] IValidatorRegistry registry,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var effectiveLimit = limit > 0 ? Math.Min(limit, 100) : 50;
            var effectiveOffset = Math.Max(offset, 0);

            var (entries, total) = await registry.GetAuditTrailAsync(
                registerId, validatorId, effectiveLimit, effectiveOffset, cancellationToken);

            return Results.Ok(new
            {
                registerId,
                entries = entries.Select(e => new
                {
                    validatorId = e.ValidatorId,
                    previousStatus = e.PreviousStatus.ToString().ToLowerInvariant(),
                    newStatus = e.NewStatus.ToString().ToLowerInvariant(),
                    performedBy = e.PerformedBy,
                    reason = e.Reason,
                    timestamp = e.Timestamp
                }),
                total
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting audit trail for register {RegisterId}", registerId);
            return Results.Problem(detail: ex.Message, statusCode: 500, title: "Audit trail error");
        }
    }
}

/// <summary>
/// Request to suspend a validator
/// </summary>
public record SuspendValidatorRequest
{
    /// <summary>Wallet address of administrator</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(256)]
    public required string SuspendedBy { get; init; }

    /// <summary>Reason for suspension</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(2048)]
    public required string Reason { get; init; }
}

/// <summary>
/// Request to reactivate a suspended validator
/// </summary>
public record ReactivateValidatorRequest
{
    /// <summary>Wallet address of administrator</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(256)]
    public required string ReactivatedBy { get; init; }

    /// <summary>Optional notes</summary>
    [StringLength(2048)]
    public string? Notes { get; init; }
}

/// <summary>
/// Request to permanently revoke a validator
/// </summary>
public record RevokeValidatorRequest
{
    /// <summary>Wallet address of administrator</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(256)]
    public required string RevokedBy { get; init; }

    /// <summary>Reason for revocation</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(2048)]
    public required string Reason { get; init; }
}

/// <summary>
/// Request to register as a validator
/// </summary>
public record RegisterValidatorRequest
{
    /// <summary>Register ID to join</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(256)]
    public required string RegisterId { get; init; }

    /// <summary>Validator's unique identifier (wallet address)</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(256)]
    public required string ValidatorId { get; init; }

    /// <summary>Validator's public key for signature verification</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(8192)]
    public required string PublicKey { get; init; }

    /// <summary>gRPC endpoint for peer communication</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(512)]
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
    [Required(AllowEmptyStrings = false)]
    [StringLength(256)]
    public required string ApprovedBy { get; init; }

    /// <summary>Optional approval notes</summary>
    [StringLength(2048)]
    public string? ApprovalNotes { get; init; }
}

/// <summary>
/// Request to reject a pending validator
/// </summary>
public record RejectValidatorRequest
{
    /// <summary>Wallet address of rejector (register owner)</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(256)]
    public required string RejectedBy { get; init; }

    /// <summary>Reason for rejection</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(2048)]
    public required string Reason { get; init; }
}
