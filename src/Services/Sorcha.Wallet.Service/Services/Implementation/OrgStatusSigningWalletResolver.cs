// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Default <see cref="IOrgStatusSigningWalletResolver"/>. Backs each org's
/// status-signing key with a Sorcha system wallet keyed by org id.
/// </summary>
public sealed class OrgStatusSigningWalletResolver : IOrgStatusSigningWalletResolver
{
    private const string SystemTenant = "system";
    private const string OwnerPrefix = "system:citizen-status:";
    private const string NamePrefix = "citizen-status-signer-";

    private readonly WalletManager _walletManager;
    private readonly ILogger<OrgStatusSigningWalletResolver> _logger;

    /// <summary>Initialises a new instance of the <see cref="OrgStatusSigningWalletResolver"/> class.</summary>
    public OrgStatusSigningWalletResolver(
        WalletManager walletManager,
        ILogger<OrgStatusSigningWalletResolver> logger)
    {
        _walletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> ResolveAsync(Guid organizationId, CancellationToken ct = default)
    {
        var owner = OwnerPrefix + organizationId.ToString("N");
        var name = NamePrefix + organizationId.ToString("N");

        var existing = await _walletManager.GetWalletsByOwnerAsync(owner, SystemTenant, ct);
        var match = existing.FirstOrDefault(w => w.Name == name && w.Status == WalletStatus.Active);
        if (match is not null)
        {
            return match.Address;
        }

        // ED25519 — fast deterministic signing, matches CitizenStatusListPublisher's
        // EdDSA branch which avoids the per-signature randomness of ECDSA.
        var (wallet, _) = await _walletManager.CreateWalletAsync(
            name, "ED25519", owner, SystemTenant,
            wordCount: 24,
            passphrase: null,
            signingModeOverride: null,
            cancellationToken: ct);

        _logger.LogInformation(
            "Provisioned citizen status-signing wallet {Address} for org {OrgId}",
            wallet.Address, organizationId);

        return wallet.Address;
    }
}
