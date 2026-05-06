// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Phase 5 follow-up #3 of Feature 118 — bridges Tenant-domain membership
/// events to the durable user inbox. Third writer in the triad alongside
/// <c>BlueprintInboxWriter</c> (Action) and <c>WalletInboxWriter</c>
/// (Credential). Tenant calls its own inbox directly via <see cref="IInboxService"/>;
/// no cross-service HTTP hop.
/// </summary>
public interface ITenantMembershipInboxWriter
{
    /// <summary>Write a "you joined {org}" inbox entry for the user just added to an organisation.</summary>
    Task WriteOrgMembershipAddedAsync(
        Guid platformUserId,
        Guid organizationId,
        string role,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class TenantMembershipInboxWriter : ITenantMembershipInboxWriter
{
    private readonly IInboxService _inbox;
    private readonly TenantDbContext _db;
    private readonly ILogger<TenantMembershipInboxWriter> _logger;

    /// <summary>Initialises a new <see cref="TenantMembershipInboxWriter"/>.</summary>
    public TenantMembershipInboxWriter(
        IInboxService inbox,
        TenantDbContext db,
        ILogger<TenantMembershipInboxWriter> logger)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task WriteOrgMembershipAddedAsync(
        Guid platformUserId,
        Guid organizationId,
        string role,
        CancellationToken ct = default)
    {
        try
        {
            var orgName = await _db.Organizations
                .AsNoTracking()
                .Where(o => o.Id == organizationId)
                .Select(o => o.Name)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false)
                ?? "your new organisation";

            var sourceEventId = DeterministicSourceEventId(platformUserId, organizationId);
            var request = new InboxWriteRequest(
                PlatformUserId: platformUserId,
                Category: InboxCategory.Membership,
                Severity: InboxSeverity.Info,
                CorrelationKey: $"membership:{organizationId:N}",
                DetailHref: $"/api/me/organizations/{organizationId:D}",
                SourceEventId: sourceEventId,
                OccurredAt: DateTimeOffset.UtcNow,
                Title: $"Welcome to {orgName}",
                Summary: string.IsNullOrWhiteSpace(role) ? null : $"Role: {role}",
                IconKey: "membership.added");

            var result = await _inbox.WriteAsync(request, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Inbox entry {Outcome} for org membership added — PlatformUserId={UserId} OrgId={OrgId} EntryId={EntryId}",
                result.IsIdempotent ? "idempotent" : "created",
                platformUserId, organizationId, result.Entry.Id);
        }
        catch (Exception ex)
        {
            // Inbox-write failure must not block the membership operation.
            _logger.LogWarning(ex,
                "Inbox-write failed for org membership added — PlatformUserId={UserId} OrgId={OrgId}",
                platformUserId, organizationId);
        }
    }

    private static Guid DeterministicSourceEventId(Guid platformUserId, Guid organizationId)
    {
        var input = $"sorcha.inbox.org-membership-added:{platformUserId:N}:{organizationId:N}";
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
