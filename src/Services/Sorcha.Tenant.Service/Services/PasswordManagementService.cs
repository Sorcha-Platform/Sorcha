// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Telemetry;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Default <see cref="IPasswordManagementService"/> implementation backed by
/// <see cref="TenantDbContext"/>. The change-tracker is shared with the
/// floor service so the floor query and the password mutation commit together.
/// Hashing uses BCrypt — same primitive used everywhere else in the service
/// (<c>BCrypt.Net.BCrypt.HashPassword</c>) so reset/change/set converge on one
/// crypto code path.
/// </summary>
public sealed class PasswordManagementService : IPasswordManagementService
{
    private readonly TenantDbContext _db;
    private readonly IAuthMethodService _authMethodService;
    private readonly IPasswordPolicyService _passwordPolicy;
    private readonly ISecurityChangeNotifier _notifier;
    private readonly AuthMetrics _metrics;
    private readonly ILogger<PasswordManagementService> _logger;

    /// <summary>Creates a new <see cref="PasswordManagementService"/>.</summary>
    public PasswordManagementService(
        TenantDbContext db,
        IAuthMethodService authMethodService,
        IPasswordPolicyService passwordPolicy,
        ISecurityChangeNotifier notifier,
        AuthMetrics metrics,
        ILogger<PasswordManagementService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _authMethodService = authMethodService ?? throw new ArgumentNullException(nameof(authMethodService));
        _passwordPolicy = passwordPolicy ?? throw new ArgumentNullException(nameof(passwordPolicy));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PasswordSetOutcome> SetAsync(
        Guid platformUserId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPassword);

        var policy = await _passwordPolicy.ValidateAsync(newPassword, cancellationToken);
        if (!policy.IsValid) return PasswordSetOutcome.PolicyViolation;

        var user = await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Id == platformUserId, cancellationToken);
        if (user is null) return PasswordSetOutcome.NotFound;

        if (user.PasswordHash is not null) return PasswordSetOutcome.AlreadySet;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync(cancellationToken);

        _metrics.RecordMethodAdded(AuthMethodKindTag.Password);
        _logger.LogInformation("Password set for {PlatformUserId}", platformUserId);
        await _notifier.NotifyAsync(platformUserId, SecurityChangeKind.PasswordSet, cancellationToken);
        return PasswordSetOutcome.Set;
    }

    /// <inheritdoc />
    public async Task<PasswordChangeOutcome> ChangeAsync(
        Guid platformUserId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPassword);

        var policy = await _passwordPolicy.ValidateAsync(newPassword, cancellationToken);
        if (!policy.IsValid) return PasswordChangeOutcome.PolicyViolation;

        var user = await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Id == platformUserId, cancellationToken);
        if (user is null) return PasswordChangeOutcome.NotFound;

        if (user.PasswordHash is null) return PasswordChangeOutcome.NoCurrentPassword;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Password rotated for {PlatformUserId}", platformUserId);
        await _notifier.NotifyAsync(platformUserId, SecurityChangeKind.PasswordChanged, cancellationToken);
        return PasswordChangeOutcome.Changed;
    }

    /// <inheritdoc />
    public async Task<PasswordRemoveOutcome> RemoveAsync(
        Guid platformUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.PlatformUsers
            .FirstOrDefaultAsync(u => u.Id == platformUserId, cancellationToken);
        if (user is null) return PasswordRemoveOutcome.NotFound;

        if (user.PasswordHash is null) return PasswordRemoveOutcome.NoCurrentPassword;

        // Floor check inside the same SaveChanges as the mutation. The shared
        // change-tracker means the floor query and the PasswordHash=null
        // assignment commit together; concurrent removes on different methods
        // both read the same user row but the floor's denominator only flips
        // after the first commit lands.
        var leavesZero = await _authMethodService.WouldRemovingLeaveZeroAsync(
            platformUserId,
            AuthMethodKind.Password,
            methodId: null,
            cancellationToken);

        if (leavesZero)
        {
            _metrics.RecordFloorBlocked(AuthMethodKindTag.Password);
            _logger.LogWarning("Password remove blocked by floor for {PlatformUserId}", platformUserId);
            return PasswordRemoveOutcome.BlockedByFloor;
        }

        user.PasswordHash = null;
        await _db.SaveChangesAsync(cancellationToken);

        _metrics.RecordMethodRemoved(AuthMethodKindTag.Password);
        _logger.LogInformation("Password removed for {PlatformUserId}", platformUserId);
        await _notifier.NotifyAsync(platformUserId, SecurityChangeKind.PasswordRemoved, cancellationToken);
        return PasswordRemoveOutcome.Removed;
    }

    /// <inheritdoc />
    public async Task<bool> IsBootstrapModeAsync(
        Guid platformUserId,
        CancellationToken cancellationToken = default)
    {
        var counts = await _authMethodService.GetCountsAsync(platformUserId, cancellationToken);
        return counts.Total == 0;
    }
}
