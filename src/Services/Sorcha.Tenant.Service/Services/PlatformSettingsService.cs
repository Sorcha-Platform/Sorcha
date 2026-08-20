// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Implementation of <see cref="IPlatformSettingsService"/> that manages the singleton
/// platform settings record and atomically updates the public organisation state.
/// </summary>
public class PlatformSettingsService : IPlatformSettingsService
{
    private readonly TenantDbContext _dbContext;
    private readonly ILogger<PlatformSettingsService> _logger;

    /// <summary>
    /// Creates a new <see cref="PlatformSettingsService"/> instance.
    /// </summary>
    /// <param name="dbContext">The tenant database context.</param>
    /// <param name="logger">Logger instance.</param>
    private readonly IWalletServiceClient? _walletClient;

    public PlatformSettingsService(
        TenantDbContext dbContext,
        ILogger<PlatformSettingsService> logger,
        IWalletServiceClient? walletClient = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _walletClient = walletClient;
    }

    /// <inheritdoc />
    public async Task<PlatformSettings> GetAsync(CancellationToken ct = default)
    {
        var settings = await _dbContext.PlatformSettings.FirstOrDefaultAsync(ct);

        if (settings is null)
        {
            throw new InvalidOperationException("Platform settings not initialized. Run bootstrap first.");
        }

        return settings;
    }

    /// <inheritdoc />
    public async Task<PublicOrgEnableResult> UpdatePublicOrgEnabledAsync(bool enabled, Guid updatedBy, CancellationToken ct = default)
    {
        var settings = await GetAsync(ct);

        settings.PublicOrgEnabled = enabled;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedBy = updatedBy;

        // Atomically update the public organisation status and self-registration flag
        var publicOrg = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == WellKnownIds.PublicOrgId, ct);

        if (publicOrg is not null)
        {
            publicOrg.Status = enabled ? OrganizationStatus.Active : OrganizationStatus.Suspended;
            publicOrg.SelfRegistrationEnabled = enabled;
        }
        else
        {
            _logger.LogWarning("Public organisation {PublicOrgId} not found — skipping org status update", WellKnownIds.PublicOrgId);
        }

        // #1530 — give the public organisation its OWN wallet, the first time it is enabled.
        //
        // Every other organisation's wallet is created by an administrator OF that organisation,
        // because the BIP39 recovery phrase is shown once, never stored and can never be reissued
        // (#1525). The public organisation has no administrator of its own, so nobody can do that
        // for it — and it was previously left with no wallet at all, which is why callers ended up
        // trying to auto-subscribe it with someone else's credentials and getting a 403.
        //
        // The system administrator performing this enable IS the human in the loop, so the phrase
        // goes to them, in the response, once. First time only: re-enabling must never re-mint,
        // because replacing the wallet would orphan anything already issued under the old one.
        string[]? mnemonic = null;
        if (enabled && publicOrg is not null && string.IsNullOrWhiteSpace(publicOrg.WalletAddress)
            && _walletClient is not null)
        {
            var created = await _walletClient.CreatePlatformOrgWalletAsync(
                publicOrg.Id, $"org-{publicOrg.Subdomain}-signing", "ED25519", ct);

            if (created is not null)
            {
                publicOrg.WalletAddress = created.Address;
                publicOrg.PublicKey = created.PublicKey;
                publicOrg.SigningAlgorithm = created.Algorithm;
                mnemonic = created.MnemonicWords;

                _logger.LogInformation(
                    "Public organisation wallet created on first enable: {Address}. Its recovery "
                    + "phrase was returned to {UpdatedBy} and is stored nowhere.",
                    created.Address, updatedBy);
            }
            else
            {
                // Not fatal — enabling must still take effect. But say so plainly: the organisation
                // is enabled without a wallet and someone has to notice.
                _logger.LogWarning(
                    "Public organisation was enabled but its wallet could not be created. It has no "
                    + "signing identity until one is; re-run the enable to try again.");
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Platform settings updated: PublicOrgEnabled={Enabled} by {UpdatedBy}",
            enabled,
            updatedBy);

        return new PublicOrgEnableResult(settings, publicOrg?.WalletAddress, mnemonic);
    }

    /// <inheritdoc />
    public async Task<PlatformSettings> UpdateMaxOrgsPerUserAsync(int maxOrgs, Guid updatedBy, CancellationToken ct = default)
    {
        if (maxOrgs is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOrgs), maxOrgs, "MaxOrgsPerUser must be between 1 and 100.");
        }

        var settings = await GetAsync(ct);

        settings.MaxOrgsPerUser = maxOrgs;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedBy = updatedBy;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Platform settings updated: MaxOrgsPerUser={MaxOrgs} by {UpdatedBy}",
            maxOrgs,
            updatedBy);

        return settings;
    }
}

/// <summary>
/// Well-known identifiers for platform-level entities created during bootstrap.
/// </summary>
public static class WellKnownIds
{
    /// <summary>
    /// ID of the System Admin organisation (created during bootstrap).
    /// </summary>
    public static readonly Guid SystemAdminOrgId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// ID of the Public organisation (created during bootstrap).
    /// </summary>
    public static readonly Guid PublicOrgId = new("00000000-0000-0000-0000-000000000002");

    /// <summary>
    /// ID of the default system administrator user (created during bootstrap).
    /// </summary>
    public static readonly Guid DefaultAdminUserId = new("00000000-0000-0001-0000-000000000001");
}
