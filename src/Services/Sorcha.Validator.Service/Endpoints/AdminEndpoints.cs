// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Endpoints;

/// <summary>
/// Administrative endpoints for validator management
/// </summary>
public static class AdminEndpoints
{
    /// <summary>
    /// Maps admin endpoints to the application
    /// </summary>
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization("RequireAdministrator", "RequirePlatformAudience");

        // Start validator for a register
        group.MapPost("/validators/start", async (
            [FromBody] StartValidatorRequest request,
            HttpContext context,
            ILogger<Program> logger,
            IValidatorOrchestrator orchestrator) =>
        {
            var userId = context.User.FindFirst("sub")?.Value ?? "unknown";
            logger.LogInformation("Admin action {Action} on register {RegisterId} by {UserId}",
                "StartValidator", request.RegisterId, userId);

            if (string.IsNullOrEmpty(request.RegisterId))
            {
                return Results.BadRequest(new { Error = "RegisterId is required" });
            }

            var success = await orchestrator.StartValidatorAsync(request.RegisterId);

            if (!success)
            {
                return Results.Problem(
                    title: "Failed to start validator",
                    detail: $"Could not start validator for register {request.RegisterId}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Ok(new
            {
                RegisterId = request.RegisterId,
                Status = "Started",
                Message = $"Validator started for register {request.RegisterId}"
            });
        })
        .WithName("StartValidator")
        .WithSummary("Start validator for a register")
        .WithDescription("Begins validation processing for the specified register")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        // Stop validator for a register
        group.MapPost("/validators/stop", async (
            [FromBody] StopValidatorRequest request,
            HttpContext context,
            ILogger<Program> logger,
            IValidatorOrchestrator orchestrator) =>
        {
            var userId = context.User.FindFirst("sub")?.Value ?? "unknown";
            logger.LogInformation("Admin action {Action} on register {RegisterId} by {UserId}",
                "StopValidator", request.RegisterId, userId);

            if (string.IsNullOrEmpty(request.RegisterId))
            {
                return Results.BadRequest(new { Error = "RegisterId is required" });
            }

            var success = await orchestrator.StopValidatorAsync(
                request.RegisterId,
                request.PersistMemPool);

            if (!success)
            {
                return Results.Problem(
                    title: "Failed to stop validator",
                    detail: $"Could not stop validator for register {request.RegisterId}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Ok(new
            {
                RegisterId = request.RegisterId,
                Status = "Stopped",
                Message = $"Validator stopped for register {request.RegisterId}",
                MemPoolPersisted = request.PersistMemPool
            });
        })
        .WithName("StopValidator")
        .WithSummary("Stop validator for a register")
        .WithDescription("Gracefully stops validation processing for the specified register")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        // Get validator status
        group.MapGet("/validators/{registerId}/status", async (
            string registerId,
            IValidatorOrchestrator orchestrator) =>
        {
            var status = await orchestrator.GetValidatorStatusAsync(registerId);

            if (status == null)
            {
                return Results.NotFound(new
                {
                    Error = $"No validator found for register {registerId}"
                });
            }

            return Results.Ok(status);
        })
        .WithName("GetValidatorStatus")
        .WithSummary("Get validator status")
        .WithDescription("Retrieves the current status of a validator for a register")
        .Produces<ValidatorStatus>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // Manual pipeline execution (for testing/debugging)
        group.MapPost("/validators/{registerId}/process", async (
            string registerId,
            HttpContext context,
            ILogger<Program> logger,
            IValidatorOrchestrator orchestrator) =>
        {
            var userId = context.User.FindFirst("sub")?.Value ?? "unknown";
            logger.LogInformation("Admin action {Action} on register {RegisterId} by {UserId}",
                "ProcessPipeline", registerId, userId);

            var result = await orchestrator.ProcessValidationPipelineAsync(registerId);

            if (result == null)
            {
                return Results.Ok(new
                {
                    Message = "No docket was built (triggers not met or no pending transactions)",
                    RegisterId = registerId
                });
            }

            return Results.Ok(new
            {
                result.Docket.DocketNumber,
                result.ConsensusAchieved,
                result.WrittenToRegister,
                result.Duration,
                result.ErrorMessage,
                TransactionCount = result.Docket.Transactions.Count
            });
        })
        .WithName("ProcessValidationPipeline")
        .WithSummary("Manually process validation pipeline")
        .WithDescription("Triggers a single validation pipeline iteration for testing/debugging")
        .Produces(StatusCodes.Status200OK);

        // Query monitored registers
        group.MapGet("/validators/monitoring", (
            IRegisterMonitoringRegistry registry) =>
        {
            var registerIds = registry.GetAll().ToList();

            return Results.Ok(new
            {
                RegisterIds = registerIds,
                Count = registerIds.Count
            });
        })
        .WithName("GetMonitoredRegisters")
        .WithSummary("Get monitored registers")
        .WithDescription("Returns the list of register IDs currently being monitored for docket building")
        .Produces(StatusCodes.Status200OK);

        return app;
    }
}

/// <summary>
/// Request to start a validator
/// </summary>
public record StartValidatorRequest
{
    /// <summary>
    /// Register ID to validate
    /// </summary>
    public required string RegisterId { get; init; }
}

/// <summary>
/// Request to stop a validator
/// </summary>
public record StopValidatorRequest
{
    /// <summary>
    /// Register ID to stop
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Whether to persist memory pool state before stopping
    /// </summary>
    public bool PersistMemPool { get; init; }
}
