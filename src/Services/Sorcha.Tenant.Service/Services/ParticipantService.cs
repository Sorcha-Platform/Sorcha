// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Tenant.Models;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service implementation for participant identity management operations.
/// </summary>
public class ParticipantService : IParticipantService
{
    private readonly IParticipantRepository _participantRepository;
    private readonly IIdentityRepository _identityRepository;
    private readonly ILogger<ParticipantService> _logger;

    public ParticipantService(
        IParticipantRepository participantRepository,
        IIdentityRepository identityRepository,
        ILogger<ParticipantService> logger)
    {
        _participantRepository = participantRepository ?? throw new ArgumentNullException(nameof(participantRepository));
        _identityRepository = identityRepository ?? throw new ArgumentNullException(nameof(identityRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }


    /// <inheritdoc />
    public async Task<ParticipantDetailResponse> RegisterAsync(
        Guid organizationId,
        CreateParticipantRequest request,
        string actorId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Verify user exists
        var user = await _identityRepository.GetUserByIdAsync(request.UserId, cancellationToken);
        if (user == null)
        {
            throw new ArgumentException($"User {request.UserId} not found", nameof(request.UserId));
        }

        // Verify user belongs to the organization
        if (user.OrganizationId != organizationId)
        {
            throw new ArgumentException($"User {request.UserId} does not belong to organization {organizationId}", nameof(request.UserId));
        }

        // Check if already registered
        if (await _participantRepository.ExistsAsync(request.UserId, organizationId, cancellationToken))
        {
            throw new InvalidOperationException($"User {request.UserId} is already registered as a participant in organization {organizationId}");
        }

        // Create participant identity
        var participant = new ParticipantIdentity
        {
            UserId = request.UserId,
            OrganizationId = organizationId,
            DisplayName = request.DisplayName ?? user.DisplayName,
            Email = user.Email,
            Status = ParticipantIdentityStatus.Active
        };

        var created = await _participantRepository.CreateAsync(participant, cancellationToken);

        // Create audit entry
        await CreateAuditEntryAsync(
            created.Id,
            ParticipantAuditAction.Created,
            actorId,
            "Admin",
            ipAddress,
            null,
            participant,
            cancellationToken);

        _logger.LogInformation(
            "Registered participant {ParticipantId} for user {UserId} in organization {OrganizationId} by {ActorId}",
            created.Id, created.UserId, created.OrganizationId, actorId);

        return await MapToDetailResponseAsync(created, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ParticipantDetailResponse> SelfRegisterAsync(
        Guid organizationId,
        Guid userId,
        string? displayName = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        // Verify user exists
        var user = await _identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            throw new ArgumentException($"User {userId} not found", nameof(userId));
        }

        // Verify user belongs to the organization
        if (user.OrganizationId != organizationId)
        {
            throw new ArgumentException($"User {userId} does not belong to organization {organizationId}", nameof(userId));
        }

        // Check if already registered
        if (await _participantRepository.ExistsAsync(userId, organizationId, cancellationToken))
        {
            throw new InvalidOperationException($"User {userId} is already registered as a participant in organization {organizationId}");
        }

        // Create participant identity
        var participant = new ParticipantIdentity
        {
            UserId = userId,
            OrganizationId = organizationId,
            DisplayName = displayName ?? user.DisplayName,
            Email = user.Email,
            Status = ParticipantIdentityStatus.Active
        };

        var created = await _participantRepository.CreateAsync(participant, cancellationToken);

        // Create audit entry
        await CreateAuditEntryAsync(
            created.Id,
            ParticipantAuditAction.Created,
            userId.ToString(),
            "User",
            ipAddress,
            null,
            participant,
            cancellationToken);

        _logger.LogInformation(
            "Self-registered participant {ParticipantId} for user {UserId} in organization {OrganizationId}",
            created.Id, created.UserId, created.OrganizationId);

        return await MapToDetailResponseAsync(created, cancellationToken);
    }



    /// <inheritdoc />
    public async Task<ParticipantDetailResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var participant = await _participantRepository.GetByIdWithWalletsAsync(id, cancellationToken);
        if (participant == null)
        {
            return null;
        }

        return await MapToDetailResponseAsync(participant, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ParticipantListResponse> ListAsync(
        Guid organizationId,
        int page = 1,
        int pageSize = 20,
        ParticipantIdentityStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var (participants, totalCount) = await _participantRepository.GetByOrganizationAsync(
            organizationId, page, pageSize, status, cancellationToken);

        var responses = new List<ParticipantResponse>();
        foreach (var participant in participants)
        {
            responses.Add(await MapToResponseAsync(participant, cancellationToken));
        }

        return new ParticipantListResponse
        {
            Participants = responses,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <inheritdoc />
    public async Task<ParticipantSearchResponse> SearchAsync(
        ParticipantSearchRequest request,
        IReadOnlyList<Guid>? accessibleOrganizations,
        bool isSystemAdmin = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var criteria = new ParticipantSearchCriteria
        {
            Query = request.Query,
            OrganizationId = request.OrganizationId,
            Status = request.Status,
            HasLinkedWallet = request.HasLinkedWallet,
            Page = request.Page,
            PageSize = request.PageSize,
            AccessibleOrganizations = accessibleOrganizations,
            IsSystemAdmin = isSystemAdmin
        };

        var (participants, totalCount) = await _participantRepository.SearchAsync(criteria, cancellationToken);

        var responses = new List<ParticipantResponse>();
        foreach (var participant in participants)
        {
            responses.Add(await MapToResponseAsync(participant, cancellationToken));
        }

        return new ParticipantSearchResponse
        {
            Results = responses,
            TotalCount = totalCount,
            Page = request.Page,
            PageSize = request.PageSize,
            Query = request.Query
        };
    }

    /// <inheritdoc />
    public async Task<ParticipantResponse?> GetByWalletAddressAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            return null;
        }

        var participant = await _participantRepository.GetParticipantByWalletAddressAsync(walletAddress, cancellationToken);
        if (participant == null)
        {
            return null;
        }

        return await MapToResponseAsync(participant, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<ParticipantDetailResponse>> GetMyProfilesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var participants = await _participantRepository.GetByUserIdAsync(userId, cancellationToken);

        var responses = new List<ParticipantDetailResponse>();
        foreach (var participant in participants)
        {
            responses.Add(await MapToDetailResponseAsync(participant, cancellationToken));
        }

        return responses;
    }

    /// <inheritdoc />
    public async Task<ParticipantDetailResponse?> GetByUserAndOrgAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var participant = await _participantRepository.GetByUserAndOrgAsync(userId, organizationId, cancellationToken);
        if (participant == null)
        {
            return null;
        }

        return await MapToDetailResponseAsync(participant, cancellationToken);
    }



    /// <inheritdoc />
    public async Task<ParticipantDetailResponse?> UpdateAsync(
        Guid id,
        UpdateParticipantRequest request,
        string actorId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var participant = await _participantRepository.GetByIdAsync(id, cancellationToken);
        if (participant == null)
        {
            return null;
        }

        var oldValues = CaptureState(participant);

        // Apply updates
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            participant.DisplayName = request.DisplayName;
        }

        if (request.Status.HasValue)
        {
            participant.Status = request.Status.Value;
            if (request.Status.Value == ParticipantIdentityStatus.Inactive)
            {
                participant.DeactivatedAt = DateTimeOffset.UtcNow;
            }
        }

        var updated = await _participantRepository.UpdateAsync(participant, cancellationToken);

        // Create audit entry
        await CreateAuditEntryAsync(
            updated.Id,
            ParticipantAuditAction.Updated,
            actorId,
            "Admin",
            ipAddress,
            oldValues,
            updated,
            cancellationToken);

        _logger.LogInformation(
            "Updated participant {ParticipantId} by {ActorId}",
            updated.Id, actorId);

        return await MapToDetailResponseAsync(updated, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DeactivateAsync(
        Guid id,
        string actorId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var participant = await _participantRepository.GetByIdAsync(id, cancellationToken);
        if (participant == null)
        {
            return false;
        }

        var oldValues = CaptureState(participant);

        participant.Status = ParticipantIdentityStatus.Inactive;
        participant.DeactivatedAt = DateTimeOffset.UtcNow;

        await _participantRepository.UpdateAsync(participant, cancellationToken);

        // Create audit entry
        await CreateAuditEntryAsync(
            participant.Id,
            ParticipantAuditAction.Deactivated,
            actorId,
            "Admin",
            ipAddress,
            oldValues,
            participant,
            cancellationToken);

        _logger.LogInformation(
            "Deactivated participant {ParticipantId} by {ActorId}",
            participant.Id, actorId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SuspendAsync(
        Guid id,
        string actorId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var participant = await _participantRepository.GetByIdAsync(id, cancellationToken);
        if (participant == null)
        {
            return false;
        }

        var oldValues = CaptureState(participant);

        participant.Status = ParticipantIdentityStatus.Suspended;

        await _participantRepository.UpdateAsync(participant, cancellationToken);

        // Create audit entry
        await CreateAuditEntryAsync(
            participant.Id,
            ParticipantAuditAction.Suspended,
            actorId,
            "Admin",
            ipAddress,
            oldValues,
            participant,
            cancellationToken);

        _logger.LogInformation(
            "Suspended participant {ParticipantId} by {ActorId}",
            participant.Id, actorId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ReactivateAsync(
        Guid id,
        string actorId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var participant = await _participantRepository.GetByIdAsync(id, cancellationToken);
        if (participant == null)
        {
            return false;
        }

        var oldValues = CaptureState(participant);

        participant.Status = ParticipantIdentityStatus.Active;
        participant.DeactivatedAt = null;

        await _participantRepository.UpdateAsync(participant, cancellationToken);

        // Create audit entry
        await CreateAuditEntryAsync(
            participant.Id,
            ParticipantAuditAction.Activated,
            actorId,
            "Admin",
            ipAddress,
            oldValues,
            participant,
            cancellationToken);

        _logger.LogInformation(
            "Reactivated participant {ParticipantId} by {ActorId}",
            participant.Id, actorId);

        return true;
    }



    /// <inheritdoc />
    public async Task<bool> ValidateSigningCapabilityAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var count = await _participantRepository.GetActiveWalletLinkCountAsync(id, cancellationToken);
        return count > 0;
    }

    /// <inheritdoc />
    public async Task<bool> IsRegisteredAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return await _participantRepository.ExistsAsync(userId, organizationId, cancellationToken);
    }



    private async Task<ParticipantResponse> MapToResponseAsync(
        ParticipantIdentity participant,
        CancellationToken cancellationToken)
    {
        var hasLinkedWallet = await _participantRepository.GetActiveWalletLinkCountAsync(participant.Id, cancellationToken) > 0;

        return new ParticipantResponse
        {
            Id = participant.Id,
            UserId = participant.UserId,
            OrganizationId = participant.OrganizationId,
            DisplayName = participant.DisplayName,
            Email = participant.Email,
            Status = participant.Status,
            HasLinkedWallet = hasLinkedWallet,
            CreatedAt = participant.CreatedAt
        };
    }

    private async Task<ParticipantDetailResponse> MapToDetailResponseAsync(
        ParticipantIdentity participant,
        CancellationToken cancellationToken)
    {
        var walletLinks = await _participantRepository.GetWalletLinksAsync(participant.Id, false, cancellationToken);

        return new ParticipantDetailResponse
        {
            Id = participant.Id,
            UserId = participant.UserId,
            OrganizationId = participant.OrganizationId,
            DisplayName = participant.DisplayName,
            Email = participant.Email,
            Status = participant.Status,
            CreatedAt = participant.CreatedAt,
            UpdatedAt = participant.UpdatedAt,
            DeactivatedAt = participant.DeactivatedAt,
            LinkedWallets = walletLinks.Select(w => new LinkedWalletAddressResponse
            {
                Id = w.Id,
                WalletAddress = w.WalletAddress,
                Algorithm = w.Algorithm,
                Status = w.Status,
                LinkedAt = w.LinkedAt,
                RevokedAt = w.RevokedAt
            }).ToList()
        };
    }

    private async Task CreateAuditEntryAsync(
        Guid participantId,
        string action,
        string actorId,
        string actorType,
        string? ipAddress,
        object? oldValues,
        object? newValues,
        CancellationToken cancellationToken)
    {
        var entry = new ParticipantAuditEntry
        {
            ParticipantId = participantId,
            Action = action,
            ActorId = actorId,
            ActorType = actorType,
            IpAddress = ipAddress,
            OldValues = oldValues != null ? JsonDocument.Parse(JsonSerializer.Serialize(oldValues)) : null,
            NewValues = newValues != null ? JsonDocument.Parse(JsonSerializer.Serialize(newValues)) : null
        };

        await _participantRepository.CreateAuditEntryAsync(entry, cancellationToken);
    }

    private static object CaptureState(ParticipantIdentity participant)
    {
        return new
        {
            participant.DisplayName,
            participant.Email,
            Status = participant.Status.ToString(),
            participant.DeactivatedAt
        };
    }

}
