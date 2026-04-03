// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sorcha.ServiceClients.Register;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service implementation for managing organization register subscriptions.
/// </summary>
public partial class RegisterSubscriptionService : IRegisterSubscriptionService
{
    private readonly TenantDbContext _dbContext;
    private readonly ILogger<RegisterSubscriptionService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Regex pattern for validating register IDs (32-character lowercase hex string).
    /// </summary>
    [GeneratedRegex(@"^[a-f0-9]{32}$")]
    private static partial Regex RegisterIdRegex();

    public RegisterSubscriptionService(
        TenantDbContext dbContext,
        ILogger<RegisterSubscriptionService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <inheritdoc />
    public async Task<RegisterSubscriptionListResponse> ListAsync(
        Guid orgId, int page, int pageSize, CancellationToken ct)
    {
        var query = _dbContext.OrganizationRegisterSubscriptions
            .Where(s => s.OrganizationId == orgId)
            .OrderByDescending(s => s.SubscribedAt);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new RegisterSubscriptionListResponse
        {
            Items = items.Select(MapToResponse).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <inheritdoc />
    public async Task<RegisterSubscriptionResponse?> GetAsync(
        Guid orgId, string registerId, CancellationToken ct)
    {
        var subscription = await _dbContext.OrganizationRegisterSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == orgId && s.RegisterId == registerId, ct);

        return subscription is null ? null : MapToResponse(subscription);
    }

    /// <inheritdoc />
    public async Task<RegisterSubscriptionResponse> SubscribeAsync(
        Guid orgId, string registerId, string? registerName, Guid subscribedByUserId, string? description = null, CancellationToken ct = default)
    {
        ValidateRegisterId(registerId);

        await EnsureNoExistingSubscription(orgId, registerId, ct);

        var subscription = new OrganizationRegisterSubscription
        {
            OrganizationId = orgId,
            RegisterId = registerId,
            RegisterName = registerName,
            SubscriptionType = SubscriptionType.Public,
            Status = SubscriptionStatus.Active,
            SubscribedAt = DateTimeOffset.UtcNow,
            SubscribedByUserId = subscribedByUserId
        };

        _dbContext.OrganizationRegisterSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Organization {OrgId} subscribed to register {RegisterId} (Public, Active)",
            orgId, registerId);

        // Fire-and-forget: notify Register Service to create stub and start peer sync
        NotifyRegisterServiceAsync(orgId, registerId, registerName, description, action: "subscribe");

        return MapToResponse(subscription);
    }

    /// <inheritdoc />
    public async Task<RegisterSubscriptionResponse> CreateOwnerSubscriptionAsync(
        Guid orgId, string registerId, string? registerName, Guid subscribedByUserId, CancellationToken ct)
    {
        ValidateRegisterId(registerId);

        await EnsureNoExistingSubscription(orgId, registerId, ct);

        var subscription = new OrganizationRegisterSubscription
        {
            OrganizationId = orgId,
            RegisterId = registerId,
            RegisterName = registerName,
            SubscriptionType = SubscriptionType.Owner,
            Status = SubscriptionStatus.Active,
            SubscribedAt = DateTimeOffset.UtcNow,
            SubscribedByUserId = subscribedByUserId
        };

        _dbContext.OrganizationRegisterSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Organization {OrgId} created owner subscription for register {RegisterId}",
            orgId, registerId);

        return MapToResponse(subscription);
    }

    /// <inheritdoc />
    public async Task UnsubscribeAsync(
        Guid orgId, string registerId, Guid revokedByUserId, CancellationToken ct)
    {
        var subscription = await _dbContext.OrganizationRegisterSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == orgId && s.RegisterId == registerId, ct);

        if (subscription is null)
        {
            throw new KeyNotFoundException(
                $"No subscription found for organization {orgId} and register {registerId}.");
        }

        if (subscription.SubscriptionType == SubscriptionType.Owner)
        {
            throw new InvalidOperationException(
                "Cannot unsubscribe from an owner subscription. Owner subscriptions are permanent.");
        }

        subscription.Status = SubscriptionStatus.Revoked;
        subscription.RevokedAt = DateTimeOffset.UtcNow;
        subscription.RevokedByUserId = revokedByUserId;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Organization {OrgId} unsubscribed from register {RegisterId} by user {UserId}",
            orgId, registerId, revokedByUserId);

        // Fire-and-forget: notify Register Service to stop sync and remove stub
        NotifyRegisterServiceAsync(orgId, registerId, registerName: null, description: null, action: "unsubscribe");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegisterSubscriptionResponse>> GetSubscribedRegistersForOrgAsync(
        Guid orgId, CancellationToken ct)
    {
        var subscriptions = await _dbContext.OrganizationRegisterSubscriptions
            .Where(s => s.OrganizationId == orgId && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.SubscribedAt)
            .ToListAsync(ct);

        return subscriptions.Select(MapToResponse).ToList();
    }

    /// <summary>
    /// Fire-and-forget notification to Register Service about subscription changes.
    /// Subscription is already persisted — this notification is best-effort.
    /// </summary>
    private void NotifyRegisterServiceAsync(
        Guid orgId, string registerId, string? registerName, string? description, string action)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var client = scope.ServiceProvider.GetService<IRegisterServiceClient>();
                if (client == null)
                {
                    _logger.LogDebug("RegisterServiceClient not available — skipping subscription notification");
                    return;
                }

                await client.NotifySubscriptionAsync(
                    new SubscriptionNotificationRequest
                    {
                        OrganizationId = orgId,
                        RegisterId = registerId,
                        RegisterName = registerName,
                        Description = description,
                        Action = action
                    });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to notify Register Service of {Action} for register {RegisterId} — subscription is persisted, sync will reconcile later",
                    action, registerId);
            }
        });
    }

    private static void ValidateRegisterId(string registerId)
    {
        if (!RegisterIdRegex().IsMatch(registerId))
        {
            throw new ArgumentException(
                $"Register ID must be a 32-character lowercase hex string. Got: '{registerId}'",
                nameof(registerId));
        }
    }

    private async Task EnsureNoExistingSubscription(Guid orgId, string registerId, CancellationToken ct)
    {
        var existing = await _dbContext.OrganizationRegisterSubscriptions
            .FirstOrDefaultAsync(s => s.OrganizationId == orgId && s.RegisterId == registerId, ct);

        if (existing is null)
            return;

        // Allow re-subscribing to revoked subscriptions by marking old record for removal.
        // No SaveChangesAsync here — caller commits delete + insert together atomically.
        if (existing.Status == SubscriptionStatus.Revoked)
        {
            _dbContext.OrganizationRegisterSubscriptions.Remove(existing);
            return;
        }

        throw new InvalidOperationException(
            $"Organization {orgId} already has a subscription to register {registerId}.");
    }

    private static RegisterSubscriptionResponse MapToResponse(OrganizationRegisterSubscription subscription)
    {
        return new RegisterSubscriptionResponse
        {
            Id = subscription.Id,
            OrganizationId = subscription.OrganizationId,
            RegisterId = subscription.RegisterId,
            RegisterName = subscription.RegisterName,
            SubscriptionType = subscription.SubscriptionType.ToString(),
            Status = subscription.Status.ToString(),
            InvitationId = subscription.InvitationId,
            SubscribedAt = subscription.SubscribedAt,
            SubscribedByUserId = subscription.SubscribedByUserId,
            RevokedAt = subscription.RevokedAt,
            RevokedByUserId = subscription.RevokedByUserId
        };
    }
}
