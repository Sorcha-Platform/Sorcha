// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

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
    private readonly IEmailSender _emailSender;
    private readonly TenantDbContext _dbContext;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(
        IInvitationRepository invitationRepository,
        IIdentityRepository identityRepository,
        IEmailSender emailSender,
        TenantDbContext dbContext,
        ILogger<InvitationService> logger)
    {
        _invitationRepository = invitationRepository;
        _identityRepository = identityRepository;
        _emailSender = emailSender;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<InvitationResponse> CreateInvitationAsync(
        Guid organizationId,
        CreateInvitationRequest request,
        Guid invitedByUserId,
        CancellationToken cancellationToken = default)
    {
        // Validate role — cannot assign SystemAdmin via invitation
        if (request.Role == UserRole.SystemAdmin)
        {
            throw new ArgumentException("Cannot assign SystemAdmin role via invitation.", nameof(request));
        }

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

        // Send invitation email
        var inviter = await _identityRepository.GetUserByIdAsync(invitedByUserId, cancellationToken);
        var inviterName = inviter?.DisplayName ?? "An administrator";

        await _emailSender.SendAsync(
            request.Email,
            "You've been invited to join an organization",
            $"{inviterName} has invited you to join their organization. Use token: {token}",
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

    public async Task<List<InvitationResponse>> ListInvitationsAsync(
        Guid organizationId,
        InvitationStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var invitations = await _invitationRepository.GetByOrganizationAsync(
            organizationId, status, cancellationToken);

        var responses = new List<InvitationResponse>();
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

    private static InvitationResponse MapToResponse(OrgInvitation invitation, string inviterName)
    {
        return new InvitationResponse
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
