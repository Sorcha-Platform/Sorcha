// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.CitizenWallet;

namespace Sorcha.Citizen.Wallet.Services;

/// <summary>
/// PWA-side renewal trigger (Feature 114, T106 PWA part). Inspects the
/// persisted delegation expiry; when within the renewal window (default
/// 30 days), calls the Wallet Service <c>/devices/renew-delegation</c>
/// endpoint and persists the fresh delegation. No-op otherwise.
/// </summary>
public interface IDelegationRenewalClient
{
    /// <summary>
    /// Check expiry and renew if due. Idempotent — safe to call on every
    /// page load. Returns the outcome describing what happened.
    /// </summary>
    Task<DelegationRenewalOutcome> RenewIfDueAsync(CancellationToken ct = default);
}

/// <summary>What happened on a single <see cref="IDelegationRenewalClient.RenewIfDueAsync"/> call.</summary>
public enum DelegationRenewalOutcome
{
    /// <summary>Wallet not enrolled — nothing to renew.</summary>
    NotEnrolled = 0,
    /// <summary>Delegation is still well within its lifetime; no action taken.</summary>
    NotDue = 1,
    /// <summary>Renewal succeeded; the new delegation has been persisted.</summary>
    Renewed = 2,
    /// <summary>Renewal was attempted but the server responded 404 (device unknown).</summary>
    DeviceNotFound = 3,
    /// <summary>Network or server error during renewal — caller may retry.</summary>
    Failed = 4,
}

/// <summary>Default <see cref="IDelegationRenewalClient"/>.</summary>
public sealed class DelegationRenewalClient : IDelegationRenewalClient
{
    /// <summary>Renew when within this window of expiry.</summary>
    public static readonly TimeSpan RenewalWindow = TimeSpan.FromDays(30);

    private readonly IDeviceMetaStore _meta;
    private readonly IDelegationStore _delegations;
    private readonly ICitizenWalletClient _client;
    private readonly TimeProvider _clock;
    private readonly ILogger<DelegationRenewalClient> _logger;

    /// <summary>Initialises a new instance.</summary>
    public DelegationRenewalClient(
        IDeviceMetaStore meta,
        IDelegationStore delegations,
        ICitizenWalletClient client,
        TimeProvider clock,
        ILogger<DelegationRenewalClient> logger)
    {
        _meta = meta ?? throw new ArgumentNullException(nameof(meta));
        _delegations = delegations ?? throw new ArgumentNullException(nameof(delegations));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DelegationRenewalOutcome> RenewIfDueAsync(CancellationToken ct = default)
    {
        var meta = await _meta.GetAsync(ct);
        if (meta is null) return DelegationRenewalOutcome.NotEnrolled;

        var now = _clock.GetUtcNow();
        if (meta.DelegationExpiresAt - now > RenewalWindow)
        {
            return DelegationRenewalOutcome.NotDue;
        }

        try
        {
            var renewal = await _client.RenewDelegationAsync(meta.DeviceId, ct);
            if (renewal is null)
            {
                _logger.LogWarning(
                    "Delegation renewal returned 404 for device {DeviceId}; the device may have been revoked",
                    meta.DeviceId);
                return DelegationRenewalOutcome.DeviceNotFound;
            }

            await _delegations.SetAsync(renewal.DelegationCredential, ct);
            await _meta.SetAsync(meta with { DelegationExpiresAt = renewal.ExpiresAt }, ct);
            _logger.LogInformation(
                "Renewed delegation for device {DeviceId}; new expiry {Exp:O}",
                meta.DeviceId, renewal.ExpiresAt);
            return DelegationRenewalOutcome.Renewed;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Delegation renewal HTTP failure for device {DeviceId}", meta.DeviceId);
            return DelegationRenewalOutcome.Failed;
        }
    }
}
