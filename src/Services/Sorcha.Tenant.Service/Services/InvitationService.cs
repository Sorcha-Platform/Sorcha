// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Manages organization invitations — create, list, revoke.
/// Generates cryptographic tokens and sends invitation emails.
/// </summary>
public class InvitationService : IInvitationService
{
    private readonly IInvitationRepository _invitationRepository;
    private readonly IIdentityRepository _identityRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ITransactionalEmailService _transactional;
    private readonly EmailSettings _emailSettings;
    private readonly TenantDbContext _dbContext;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(
        IInvitationRepository invitationRepository,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITransactionalEmailService transactional,
        IOptions<EmailSettings> emailSettings,
        TenantDbContext dbContext,
        ILogger<InvitationService> logger)
    {
        _invitationRepository = invitationRepository;
        _identityRepository = identityRepository;
        _organizationRepository = organizationRepository;
        _transactional = transactional;
        _emailSettings = emailSettings.Value;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OrgInvitationResponse> CreateInvitationAsync(
        Guid organizationId,
        CreateOrgInvitationRequest request,
        Guid invitedByUserId,
        CancellationToken cancellationToken = default)
    {
        // Validate role — cannot assign SystemAdmin via invitation
        if (request.Role == UserRole.SystemAdmin)
        {
            throw new ArgumentException("Cannot assign SystemAdmin role via invitation.", nameof(request));
        }

        // Fail-fast lookups BEFORE we persist anything. Previously the invitation row
        // was committed before the org lookup — a missing org would leave an orphaned
        // row with a valid token and no email ever sent (reviewer M-1).
        var invitingOrg = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Inviting organisation {organizationId} not found when preparing invitation email.");

        var inviter = await _identityRepository.GetUserByIdAsync(invitedByUserId, cancellationToken);
        var inviterName = inviter?.DisplayName ?? "An administrator";

        // Check for duplicate active invitation
        if (await _invitationRepository.HasActiveInvitationAsync(organizationId, request.Email, cancellationToken))
        {
            throw new InvalidOperationException($"An active invitation already exists for {request.Email}.");
        }

        // Generate 32-byte cryptographic token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var invitation = new OrgInvitation
        {
            OrganizationId = organizationId,
            Email = request.Email,
            AssignedRole = request.Role,
            Token = token,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(request.ExpiryDays),
            InvitedByUserId = invitedByUserId
        };

        await _invitationRepository.CreateAsync(invitation, cancellationToken);

        var acceptUrl = $"{_emailSettings.BaseUrl.TrimEnd('/')}/invitations/accept?token={Uri.EscapeDataString(token)}";

        await _transactional.SendInvitationAsync(
            new InviteEmailDispatch(
                ToEmail: request.Email,
                InviterName: inviterName,
                InvitingOrganization: invitingOrg,
                RoleDisplayName: request.Role.ToString(),
                AcceptUrl: acceptUrl,
                ExpiresInDays: request.ExpiryDays),
            cancellationToken);

        // Audit
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            OrganizationId = organizationId,
            IdentityId = invitedByUserId,
            EventType = AuditEventType.InvitationSent,
            Timestamp = DateTimeOffset.UtcNow,
            Success = true,
            Details = new Dictionary<string, object>
            {
                ["email"] = request.Email,
                ["role"] = request.Role.ToString(),
                ["expiresAt"] = invitation.ExpiresAt.ToString("O")
            }
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Invitation sent to {Email} for org {OrgId} with role {Role}",
            request.Email, organizationId, request.Role);

        return MapToResponse(invitation, inviterName);
    }

    /// <inheritdoc />
    public async Task<List<OrgInvitationResponse>> ListInvitationsAsync(
        Guid organizationId,
        InvitationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var invitations = await _invitationRepository.GetByOrganizationAsync(
            organizationId, status, cancellationToken);

        var responses = new List<OrgInvitationResponse>();
        foreach (var inv in invitations)
        {
            // Check if expired but still marked Pending
            if (inv.Status == InvitationStatus.Pending && inv.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                inv.Status = InvitationStatus.Expired;
                await _invitationRepository.UpdateAsync(inv, cancellationToken);
            }

            var inviter = await _identityRepository.GetUserByIdAsync(inv.InvitedByUserId, cancellationToken);
            responses.Add(MapToResponse(inv, inviter?.DisplayName ?? "Unknown"));
        }

        return responses;
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public async Task<bool> ResendInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        Guid resentByUserId,
        CancellationToken cancellationToken = default)
    {
        var invitation = await _invitationRepository.GetByIdAsync(invitationId, cancellationToken);
        if (invitation == null || invitation.OrganizationId != organizationId)
        {
            return false;
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot resend invitation with status {invitation.Status}. Only Pending invitations can be resent.");
        }

        // Fail-fast lookups BEFORE mutating the row, mirroring CreateInvitationAsync (reviewer M-1
        // there): a missing org must not leave a rotated token whose email was never sent.
        var invitingOrg = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Inviting organisation {organizationId} not found when preparing invitation email.");

        var resender = await _identityRepository.GetUserByIdAsync(resentByUserId, cancellationToken);
        var resenderName = resender?.DisplayName ?? "An administrator";

        // ROTATE the token and reset the expiry window. See IInvitationService for the reasoning:
        // an operator resending is usually recovering from a mail that never arrived or has gone
        // stale, and rotating bounds how long a leaked token stays live. The cost — a link already in
        // flight stops working — is accepted, because the fresh email supersedes it.
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        // Preserve the original validity WINDOW rather than hard-coding a default, so a deliberately
        // short-lived invitation does not silently become a long-lived one on resend.
        var originalWindow = invitation.ExpiresAt - invitation.CreatedAt;
        var expiryDays = (int)Math.Ceiling(originalWindow.TotalDays);
        if (expiryDays < 1) expiryDays = 1;

        invitation.Token = token;
        invitation.ExpiresAt = DateTimeOffset.UtcNow.AddDays(expiryDays);
        await _invitationRepository.UpdateAsync(invitation, cancellationToken);

        var acceptUrl = $"{_emailSettings.BaseUrl.TrimEnd('/')}/invitations/accept?token={Uri.EscapeDataString(token)}";

        // CLAUDE.md pattern #9 — never IEmailSender directly, never an inline body.
        await _transactional.SendInvitationAsync(
            new InviteEmailDispatch(
                ToEmail: invitation.Email,
                InviterName: resenderName,
                InvitingOrganization: invitingOrg,
                RoleDisplayName: invitation.AssignedRole.ToString(),
                AcceptUrl: acceptUrl,
                ExpiresInDays: expiryDays),
            cancellationToken);

        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            OrganizationId = organizationId,
            IdentityId = resentByUserId,
            EventType = AuditEventType.InvitationSent,
            Timestamp = DateTimeOffset.UtcNow,
            Success = true,
            Details = new Dictionary<string, object>
            {
                ["email"] = invitation.Email,
                ["role"] = invitation.AssignedRole.ToString(),
                ["expiresAt"] = invitation.ExpiresAt.ToString("O"),
                ["resend"] = true,
                ["tokenRotated"] = true
            }
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Invitation resent to {Email} for org {OrgId} (token rotated, expires {ExpiresAt:O})",
            invitation.Email, organizationId, invitation.ExpiresAt);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RevokeInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        Guid revokedByUserId,
        CancellationToken cancellationToken = default)
    {
        var invitation = await _invitationRepository.GetByIdAsync(invitationId, cancellationToken);
        if (invitation == null || invitation.OrganizationId != organizationId)
        {
            return false;
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Cannot revoke invitation with status {invitation.Status}. Only Pending invitations can be revoked.");
        }

        invitation.Status = InvitationStatus.Revoked;
        invitation.RevokedAt = DateTimeOffset.UtcNow;
        await _invitationRepository.UpdateAsync(invitation, cancellationToken);

        // Audit
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            OrganizationId = organizationId,
            IdentityId = revokedByUserId,
            EventType = AuditEventType.InvitationRevoked,
            Timestamp = DateTimeOffset.UtcNow,
            Success = true,
            Details = new Dictionary<string, object>
            {
                ["email"] = invitation.Email,
                ["invitationId"] = invitation.Id.ToString()
            }
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Invitation {InvitationId} revoked for {Email} in org {OrgId}",
            invitationId, invitation.Email, organizationId);

        return true;
    }

    private static OrgInvitationResponse MapToResponse(OrgInvitation invitation, string inviterName)
    {
        return new OrgInvitationResponse
        {
            Id = invitation.Id,
            Email = invitation.Email,
            AssignedRole = invitation.AssignedRole.ToString(),
            Status = invitation.Status.ToString(),
            ExpiresAt = invitation.ExpiresAt,
            InvitedBy = inviterName,
            CreatedAt = invitation.CreatedAt
        };
    }
}
