// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Blueprints;
using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Client-side aggregator for the Feature 142 Go-live system-info card (FR-026 / D6). Composes the
/// user-facing register read surface (<see cref="IRegisterReadService"/>) and the governance roster
/// read (<see cref="IRegisterGovernanceService"/>) — no new server endpoint. Pure aggregation:
/// every sub-read is independently guarded so one failure degrades a single field, not the card.
/// </summary>
public sealed class RegisterSystemInfoService : IRegisterSystemInfoService
{
    private readonly IRegisterReadService _registerRead;
    private readonly IRegisterGovernanceService _governance;
    private readonly ILogger<RegisterSystemInfoService> _logger;

    /// <summary>Creates the aggregator over the register read + governance services.</summary>
    public RegisterSystemInfoService(
        IRegisterReadService registerRead,
        IRegisterGovernanceService governance,
        ILogger<RegisterSystemInfoService> logger)
    {
        _registerRead = registerRead;
        _governance = governance;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RegisterSystemInfoViewModel> GetSystemInfoAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        // Fan out independently — each read is its own guarded task so a single failure degrades
        // only its field. The register read carries visibility (Advertise), DevMode, and name.
        var register = await SafeAsync(
            () => _registerRead.GetRegisterAsync(registerId, cancellationToken),
            "register read", registerId);
        var relationship = await SafeAsync(
            () => _registerRead.GetLocalRelationshipAsync(registerId, cancellationToken),
            "local relationship", registerId);
        var syncState = await SafeAsync(
            () => _registerRead.GetSyncStateAsync(registerId, cancellationToken),
            "sync state", registerId);
        var roster = await SafeAsync(
            () => _governance.GetGovernanceRosterAsync(registerId, cancellationToken),
            "governance roster", registerId);
        var publishedCount = await SafeAsync(
            () => _registerRead.GetPublishedBlueprintCountAsync(registerId, cancellationToken),
            "published count", registerId);

        var validatorCount = roster?.Members
            .Count(m => string.Equals(m.Role, "Validator", StringComparison.OrdinalIgnoreCase)) ?? 0;

        return new RegisterSystemInfoViewModel
        {
            RegisterId = registerId,
            Name = register?.Name ?? string.Empty,

            OwnershipAvailable = relationship is not null,
            IsLocallyOwned = relationship?.IsOwner ?? false,

            ValidationAvailable = roster is not null,
            ValidatorCount = validatorCount,
            RequiredSignatures = validatorCount,

            IsPublic = register?.Advertise ?? false,

            SyncStateAvailable = syncState is not null,
            SyncStateText = DescribeSyncState(register, syncState is not null),

            DevMode = register?.DevMode ?? false,

            PublishedServiceCount = publishedCount,

            CallerRole = DeriveRole(relationship),
        };
    }

    /// <summary>
    /// Derives the caller's governance role from the local-relationship role flags (highest wins).
    /// Owner / Admin / Designer may publish; anything else (subscriber, auditor-only, or no
    /// relationship) is <see cref="RegisterGovernanceRole.None"/>.
    /// </summary>
    private static RegisterGovernanceRole DeriveRole(
        Sorcha.Register.Models.LocalRelationship.RegisterLocalRelationship? relationship)
    {
        if (relationship is null)
        {
            return RegisterGovernanceRole.None;
        }
        if (relationship.IsOwner)
        {
            return RegisterGovernanceRole.Owner;
        }
        if (relationship.IsAdmin)
        {
            return RegisterGovernanceRole.Admin;
        }
        if (relationship.IsDesigner)
        {
            return RegisterGovernanceRole.Designer;
        }
        return RegisterGovernanceRole.None;
    }

    /// <summary>
    /// Plain-language sync state. When the typed sync-state read succeeds we prefer the register
    /// view-model's computed <c>SyncStateText</c> (it covers both legacy + F108 strings); otherwise
    /// "Unknown" — a locally-owned register has no remote sync lifecycle but still reads as caught up.
    /// </summary>
    private static string DescribeSyncState(RegisterViewModel? register, bool syncAvailable)
    {
        if (register is not null && !string.IsNullOrEmpty(register.SyncStateText))
        {
            return register.SyncStateText;
        }
        // A locally-owned register reports no SyncState string but is authoritative ⇒ caught up.
        return syncAvailable ? "Caught up" : "Unknown";
    }

    /// <summary>Runs a sub-read, swallowing failures so the aggregate never crashes on one field.</summary>
    private async Task<T?> SafeAsync<T>(Func<Task<T?>> read, string what, string registerId)
    {
        try
        {
            return await read();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "System-info sub-read '{What}' failed for register {RegisterId}; degrading that field",
                what, registerId);
            return default;
        }
    }
}
