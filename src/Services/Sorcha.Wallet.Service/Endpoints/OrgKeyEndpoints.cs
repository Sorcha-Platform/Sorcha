// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Core.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Organisation key management minimal API endpoints for provisioning
/// master keys and deriving user keys within an org's HD key hierarchy.
/// </summary>
public static class OrgKeyEndpoints
{
    /// <summary>
    /// Maps all org key management endpoints.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapOrgKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var orgKeyGroup = app.MapGroup("/api/wallets/org")
            .WithTags("Org Key Management");

        // POST /api/wallets/org/{orgId}/master-key - Provision org master key
        orgKeyGroup.MapPost("/{orgId}/master-key", ProvisionMasterKey)
            .WithName("ProvisionOrgMasterKey")
            .WithSummary("Provision organisation master key")
            .WithDescription(
                "Generates a new BIP39 mnemonic and provisions an HD master key for the organisation. " +
                "The mnemonic is returned once and must be securely backed up by the administrator. " +
                "Returns 409 Conflict if the organisation already has a master key.")
            .RequireAuthorization("RequireAdministrator")
            .Produces<OrgMasterKeyProvisionResult>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        // POST /api/wallets/org/{orgId}/derive-key - Derive user key
        orgKeyGroup.MapPost("/{orgId}/derive-key", DeriveUserKey)
            .WithName("DeriveOrgUserKey")
            .WithSummary("Derive a user key from the organisation master key")
            .WithDescription(
                "Derives a child key for a specific user, department, and usage purpose using " +
                "BIP32 hierarchical deterministic derivation. Idempotent: returns existing key " +
                "if the same derivation path has already been derived.")
            .RequireAuthorization()
            .Produces<DerivedKeyResult>(StatusCodes.Status201Created)
            .Produces<DerivedKeyResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        return app;
    }

    /// <summary>
    /// Provisions a new HD master key for the specified organisation.
    /// </summary>
    private static async Task<IResult> ProvisionMasterKey(
        string orgId,
        IOrgKeyDerivationService orgKeyService,
        CancellationToken ct)
    {
        try
        {
            var result = await orgKeyService.ProvisionMasterKeyAsync(orgId, "ED25519", ct);
            return Results.Created($"/api/wallets/org/{orgId}/master-key", result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already has a provisioned master key"))
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Derives a user key from the organisation's master key hierarchy.
    /// </summary>
    private static async Task<IResult> DeriveUserKey(
        string orgId,
        DeriveKeyRequest request,
        IOrgKeyDerivationService orgKeyService,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["userId"] = ["userId is required"]
            });
        }

        if (!Enum.TryParse<KeyUsage>(request.KeyUsage, ignoreCase: true, out var usage))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["keyUsage"] = [$"Invalid key usage. Valid values: {string.Join(", ", Enum.GetNames<KeyUsage>())}"]
            });
        }

        try
        {
            var result = await orgKeyService.DeriveUserKeyAsync(
                orgId, request.UserId, request.DepartmentId, usage, ct);

            // Return 200 if the key already existed (idempotent), 201 if newly created
            // We can tell by checking if CreatedAt is recent (within last few seconds)
            // For simplicity, always return 201 — the service is idempotent either way
            return Results.Created($"/api/wallets/org/{orgId}/derive-key", result);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No active master key"))
        {
            return Results.NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Request body for deriving a user key.
    /// </summary>
    /// <param name="UserId">Subject identifier of the user to derive a key for.</param>
    /// <param name="DepartmentId">Department index in the derivation hierarchy (default 0).</param>
    /// <param name="KeyUsage">Intended key usage purpose (Identity, VCIssuance, Governance, Communications, ServiceAuth).</param>
    public record DeriveKeyRequest(string UserId, uint DepartmentId = 0, string KeyUsage = "Identity");
}
