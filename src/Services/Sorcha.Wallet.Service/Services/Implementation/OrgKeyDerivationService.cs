// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.Enums;
using Sorcha.Wallet.Core.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Service for org-level HD key derivation. Provisions master keys,
/// derives user keys, rotates, and revokes.
/// </summary>
public class OrgKeyDerivationService : IOrgKeyDerivationService
{
    private readonly WalletDbContext _db;
    private readonly IOrgKeyProtectionProvider _protectionProvider;
    private readonly ILogger<OrgKeyDerivationService> _logger;

    public OrgKeyDerivationService(
        WalletDbContext db,
        IOrgKeyProtectionProvider protectionProvider,
        ILogger<OrgKeyDerivationService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _protectionProvider = protectionProvider ?? throw new ArgumentNullException(nameof(protectionProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<OrgMasterKeyProvisionResult> ProvisionMasterKeyAsync(
        string organizationId, string algorithm = "ED25519", CancellationToken ct = default)
    {
        // TODO: Implement in Phase 3 (T025)
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<DerivedKeyResult> DeriveUserKeyAsync(
        string organizationId, string userId, uint departmentId, KeyUsage usage, CancellationToken ct = default)
    {
        // TODO: Implement in Phase 3 (T026)
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task<DerivedKeyResult> RotateKeyAsync(Guid derivedKeyRecordId, CancellationToken ct = default)
    {
        // TODO: Implement in Phase 6 (T040)
        throw new NotImplementedException();
    }

    /// <inheritdoc />
    public Task RevokeKeyAsync(Guid derivedKeyRecordId, CancellationToken ct = default)
    {
        // TODO: Implement in Phase 7 (T045)
        throw new NotImplementedException();
    }
}
