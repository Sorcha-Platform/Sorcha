// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service for managing platform-level configuration settings (singleton entity).
/// Controls public organisation availability and org creation limits.
/// </summary>
public interface IPlatformSettingsService
{
    /// <summary>
    /// Gets the current platform settings.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The singleton <see cref="PlatformSettings"/> record.</returns>
    /// <exception cref="InvalidOperationException">Thrown when platform settings have not been initialized via bootstrap.</exception>
    Task<PlatformSettings> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Toggles the public organisation enabled status. When disabled, the public org
    /// is suspended and self-registration is turned off. When enabled, the public org
    /// is activated and self-registration is restored.
    /// </summary>
    /// <param name="enabled">Whether the public organisation should be enabled.</param>
    /// <param name="updatedBy">ID of the system admin performing the update.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="PlatformSettings"/> record.</returns>
    /// <exception cref="InvalidOperationException">Thrown when platform settings have not been initialized via bootstrap.</exception>
    Task<PublicOrgEnableResult> UpdatePublicOrgEnabledAsync(bool enabled, Guid updatedBy, CancellationToken ct = default);

    /// <summary>
    /// Updates the maximum number of private organisations a single user can create.
    /// </summary>
    /// <param name="maxOrgs">Maximum organisations per user. Must be in range 1-100.</param>
    /// <param name="updatedBy">ID of the system admin performing the update.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="PlatformSettings"/> record.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxOrgs"/> is not in range 1-100.</exception>
    /// <exception cref="InvalidOperationException">Thrown when platform settings have not been initialized via bootstrap.</exception>
    Task<PlatformSettings> UpdateMaxOrgsPerUserAsync(int maxOrgs, Guid updatedBy, CancellationToken ct = default);
}

/// <summary>
/// Outcome of enabling or disabling the public organisation (#1530).
/// </summary>
/// <param name="Settings">The updated platform settings.</param>
/// <param name="WalletAddress">The public organisation's signing wallet, if it now has one.</param>
/// <param name="MnemonicWords">
/// The wallet's recovery phrase, present ONLY on the enable that first created it. Shown once,
/// stored nowhere, and impossible to reissue — the caller must surface it to the administrator who
/// enabled the organisation, because they are the only person who will ever hold it.
/// </param>
public sealed record PublicOrgEnableResult(
    PlatformSettings Settings,
    string? WalletAddress = null,
    string[]? MnemonicWords = null);
