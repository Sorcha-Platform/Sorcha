// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Supplies the publication id of the governance blueprint definition a new proposal is pinned to
/// (Feature 197, #1466).
/// </summary>
/// <remarks>
/// Deliberately uncached: the pin must name the definition current at the moment the proposal is
/// raised, and a stale cached id would pin the proposal to a superseded definition.
/// </remarks>
public interface IGovernanceDefinitionPinSource
{
    /// <summary>
    /// The current system-register publication id of the governance blueprint, or null when it
    /// cannot be read (no entry, or the ledger read failed).
    /// </summary>
    Task<string?> GetCurrentAsync(CancellationToken ct = default);
}

/// <summary>
/// <see cref="IGovernanceDefinitionPinSource"/> over the ledger-ordered system register.
/// </summary>
public sealed class GovernanceDefinitionPinSource : IGovernanceDefinitionPinSource
{
    private readonly SystemRegisterService _systemRegister;
    private readonly ILogger<GovernanceDefinitionPinSource> _logger;

    /// <summary>Creates the pin source.</summary>
    public GovernanceDefinitionPinSource(
        SystemRegisterService systemRegister,
        ILogger<GovernanceDefinitionPinSource> logger)
    {
        _systemRegister = systemRegister;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> GetCurrentAsync(CancellationToken ct = default)
    {
        try
        {
            var entry = await _systemRegister.GetBlueprintAsync(
                SystemBlueprintCatalog.GovernanceBlueprintId, ct);

            return entry?.PublicationTransactionId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Could not read the current publication id of governance blueprint {BlueprintId}",
                SystemBlueprintCatalog.GovernanceBlueprintId);
            return null;
        }
    }
}
