// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;

namespace Sorcha.Register.Service.Endpoints;

/// <summary>
/// Minimal API endpoints for register operational policy queries
/// </summary>
public static class RegisterPolicyEndpoints
{
    /// <summary>
    /// Maps register policy endpoints to the application
    /// </summary>
    public static void MapRegisterPolicyEndpoints(this WebApplication app)
    {
        var policyGroup = app.MapGroup("/api/registers/{registerId}/policy")
            .WithTags("Register Policy")
            .RequireAuthorization("CanManageRegisters");

        policyGroup.MapGet("/", async (
            IRegisterPolicyService policyService,
            string registerId,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var policy = await policyService.GetEffectivePolicyAsync(registerId, cancellationToken);
                var isDefault = policy.Version == 1 && policy.UpdatedBy == null;

                return Results.Ok(new RegisterPolicyResponse
                {
                    RegisterId = registerId,
                    Policy = policy,
                    IsDefault = isDefault
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Failed to retrieve register policy",
                    detail: ex.Message,
                    statusCode: 500);
            }
        })
        .WithName("GetRegisterPolicy")
        .WithSummary("Get the current operational policy for a register")
        .WithDescription("Returns the active RegisterPolicy for a register. For registers without an explicit policy (pre-feature), returns default values with isDefault=true.");

        policyGroup.MapGet("/history", async (
            IRegisterPolicyService policyService,
            string registerId,
            int? page,
            int? pageSize,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var p = page ?? 1;
                var ps = pageSize ?? 20;

                if (p < 1) return Results.BadRequest("Page must be >= 1");
                if (ps < 1 || ps > 100) return Results.BadRequest("PageSize must be between 1 and 100");

                var policies = await policyService.GetPolicyHistoryAsync(registerId, p, ps, cancellationToken);

                return Results.Ok(new PolicyHistoryResponse
                {
                    RegisterId = registerId,
                    Versions = policies.Select(pol => new PolicyVersionEntry
                    {
                        Version = pol.Version,
                        Policy = pol,
                        UpdatedAt = pol.UpdatedAt,
                        UpdatedBy = pol.UpdatedBy
                    }).ToList(),
                    Page = p,
                    PageSize = ps,
                    TotalCount = policies.Count
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Failed to retrieve policy history",
                    detail: ex.Message,
                    statusCode: 500);
            }
        })
        .WithName("GetRegisterPolicyHistory")
        .WithSummary("Get the policy version history for a register")
        .WithDescription("Returns a paginated list of policy snapshots from the control transaction chain, ordered chronologically.");

        policyGroup.MapPost("/update", async (
            IRegisterPolicyService policyService,
            string registerId,
            PolicyUpdateRequest request,
            CancellationToken cancellationToken) =>
        {
            try
            {
                // Validate the proposed policy
                var currentPolicy = await policyService.GetEffectivePolicyAsync(registerId, cancellationToken);

                if (request.Policy == null)
                    return Results.BadRequest("Policy is required");

                if (string.IsNullOrWhiteSpace(request.UpdatedBy))
                    return Results.BadRequest("UpdatedBy is required");

                if (request.Policy.Version <= currentPolicy.Version)
                    return Results.Conflict(new { message = $"Policy version must be > {currentPolicy.Version}, got {request.Policy.Version}" });

                // Validate min/max constraints
                if (request.Policy.Validators.MinValidators < 1)
                    return Results.BadRequest("MinValidators must be >= 1");

                if (request.Policy.Validators.MaxValidators < request.Policy.Validators.MinValidators)
                    return Results.BadRequest("MaxValidators must be >= MinValidators");

                if (request.Policy.Consensus.SignatureThresholdMin < 1)
                    return Results.BadRequest("SignatureThresholdMin must be >= 1");

                if (request.Policy.Consensus.SignatureThresholdMax < request.Policy.Consensus.SignatureThresholdMin)
                    return Results.BadRequest("SignatureThresholdMax must be >= SignatureThresholdMin");

                // Check transition mode requirement
                if (currentPolicy.Validators.RegistrationMode == RegistrationMode.Public &&
                    request.Policy.Validators.RegistrationMode == RegistrationMode.Consent &&
                    request.TransitionMode == null)
                {
                    return Results.BadRequest("TransitionMode is required when changing from Public to Consent mode");
                }

                // Stamp update metadata
                request.Policy.UpdatedAt = DateTimeOffset.UtcNow;
                request.Policy.UpdatedBy = request.UpdatedBy;

                return Results.Accepted(value: new PolicyUpdateResponse
                {
                    RegisterId = registerId,
                    ProposedVersion = request.Policy.Version,
                    CurrentVersion = currentPolicy.Version,
                    RequiresGovernanceVote = true,
                    Message = "Policy update proposal accepted. Submit as control.policy.update transaction for governance vote."
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Failed to validate policy update",
                    detail: ex.Message,
                    statusCode: 500);
            }
        })
        .WithName("ProposeRegisterPolicyUpdate")
        .WithSummary("Validate and propose a register policy update")
        .WithDescription("Validates the proposed policy update against the current policy and returns acceptance status. " +
            "The actual update must be submitted as a control.policy.update transaction through the governance process.");
    }

    /// <summary>
    /// Maps validator query endpoints for retrieving approved and operational validator lists
    /// </summary>
    public static void MapValidatorQueryEndpoints(this WebApplication app)
    {
        var validatorGroup = app.MapGroup("/api/registers/{registerId}/validators")
            .WithTags("Validator Queries")
            .RequireAuthorization("CanManageRegisters");

        validatorGroup.MapGet("/approved", async (
            IRegisterPolicyService policyService,
            string registerId,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var policy = await policyService.GetEffectivePolicyAsync(registerId, cancellationToken);

                return Results.Ok(new ApprovedValidatorsResponse
                {
                    RegisterId = registerId,
                    RegistrationMode = policy.Validators.RegistrationMode,
                    Validators = policy.Validators.ApprovedValidators,
                    Count = policy.Validators.ApprovedValidators.Count
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    title: "Failed to retrieve approved validators",
                    detail: ex.Message,
                    statusCode: 500);
            }
        })
        .WithName("GetApprovedValidators")
        .WithSummary("Get the approved validator list from register policy")
        .WithDescription("Returns the on-chain approved validator list from the register's policy. " +
            "In Public registration mode the list may be empty (all validators are accepted). " +
            "In Consent mode only listed validators may participate.");

        validatorGroup.MapGet("/operational", (
            string registerId,
            CancellationToken cancellationToken) =>
        {
            // TODO: Operational validator state lives in Redis within the Validator Service.
            // This endpoint needs cross-service resolution — either a service client call
            // to the Validator Service or a shared Redis read. Returning empty for now.
            return Results.Ok(new OperationalValidatorsResponse
            {
                RegisterId = registerId,
                Validators = [],
                Count = 0
            });
        })
        .WithName("GetOperationalValidators")
        .WithSummary("Get validators currently online for a register")
        .WithDescription("Returns validators currently reporting operational heartbeats via the ValidatorRegistry. " +
            "NOTE: This endpoint is a placeholder — operational state is managed by the Validator Service " +
            "and requires cross-service integration to resolve.");
    }
}

/// <summary>
/// Response DTO for register policy queries
/// </summary>
public class RegisterPolicyResponse
{
    /// <summary>
    /// Register identifier
    /// </summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// The effective register policy
    /// </summary>
    public RegisterPolicy Policy { get; set; } = RegisterPolicy.CreateDefault();

    /// <summary>
    /// True if policy was generated from defaults (no explicit policy on-chain)
    /// </summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// Response DTO for the approved validators query endpoint
/// </summary>
public class ApprovedValidatorsResponse
{
    /// <summary>
    /// Register identifier
    /// </summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// The registration mode governing how validators join this register
    /// </summary>
    public RegistrationMode RegistrationMode { get; set; }

    /// <summary>
    /// List of approved validators from the register policy
    /// </summary>
    public List<ApprovedValidator> Validators { get; set; } = [];

    /// <summary>
    /// Number of approved validators
    /// </summary>
    public int Count { get; set; }
}

/// <summary>
/// Response DTO for the operational validators query endpoint
/// </summary>
public class OperationalValidatorsResponse
{
    /// <summary>
    /// Register identifier
    /// </summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// List of validators currently reporting operational heartbeats
    /// </summary>
    public List<OperationalValidatorInfo> Validators { get; set; } = [];

    /// <summary>
    /// Number of operational validators
    /// </summary>
    public int Count { get; set; }
}

/// <summary>
/// Request DTO for proposing a register policy update
/// </summary>
public class PolicyUpdateRequest
{
    /// <summary>
    /// The proposed policy with version incremented
    /// </summary>
    public RegisterPolicy? Policy { get; set; }

    /// <summary>
    /// Transition mode when changing registrationMode from public to consent
    /// </summary>
    public TransitionMode? TransitionMode { get; set; }

    /// <summary>
    /// DID of the proposer
    /// </summary>
    public string UpdatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Response DTO for policy update proposals
/// </summary>
public class PolicyUpdateResponse
{
    /// <summary>Register identifier</summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>Proposed policy version</summary>
    public uint ProposedVersion { get; set; }

    /// <summary>Current active policy version</summary>
    public uint CurrentVersion { get; set; }

    /// <summary>Whether the update requires a governance vote</summary>
    public bool RequiresGovernanceVote { get; set; }

    /// <summary>Status message</summary>
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Response DTO for policy version history
/// </summary>
public class PolicyHistoryResponse
{
    /// <summary>Register identifier</summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>Policy version entries</summary>
    public List<PolicyVersionEntry> Versions { get; set; } = [];

    /// <summary>Current page number</summary>
    public int Page { get; set; }

    /// <summary>Page size</summary>
    public int PageSize { get; set; }

    /// <summary>Total count of versions on this page</summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// A single policy version entry in the history
/// </summary>
public class PolicyVersionEntry
{
    /// <summary>Policy version number</summary>
    public uint Version { get; set; }

    /// <summary>Full policy snapshot</summary>
    public RegisterPolicy Policy { get; set; } = RegisterPolicy.CreateDefault();

    /// <summary>When this version was created</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Who created this version</summary>
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Information about a validator that is currently operational
/// </summary>
public class OperationalValidatorInfo
{
    /// <summary>
    /// Decentralized identifier (DID) of the validator
    /// </summary>
    public string Did { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of the last heartbeat received from this validator
    /// </summary>
    public DateTimeOffset LastHeartbeat { get; set; }

    /// <summary>
    /// Whether this validator is the current leader
    /// </summary>
    public bool IsLeader { get; set; }
}
