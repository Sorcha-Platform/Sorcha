// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Endpoints;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service implementation for organization management operations.
/// </summary>
public partial class OrganizationService : IOrganizationService
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IIdentityRepository _identityRepository;
    private readonly TenantDbContext _dbContext;
    private readonly IWalletServiceClient _walletClient;
    private readonly ILogger<OrganizationService> _logger;

    // Reserved subdomains that cannot be used
    private static readonly HashSet<string> ReservedSubdomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "www", "api", "app", "admin", "auth", "login", "signup", "register",
        "dashboard", "portal", "help", "support", "docs", "mail", "email",
        "ftp", "cdn", "static", "assets", "images", "files", "download",
        "blog", "news", "status", "health", "test", "dev", "staging", "prod",
        "sorcha", "system", "internal", "public", "private", "secure"
    };

    public OrganizationService(
        IOrganizationRepository organizationRepository,
        IIdentityRepository identityRepository,
        TenantDbContext dbContext,
        IWalletServiceClient walletClient,
        ILogger<OrganizationService> logger)
    {
        _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
        _identityRepository = identityRepository ?? throw new ArgumentNullException(nameof(identityRepository));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<OrganizationResponse> CreateOrganizationAsync(
        CreateOrganizationRequest request,
        Guid creatorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Validate subdomain
        var (isValid, errorMessage) = await ValidateSubdomainAsync(request.Subdomain, cancellationToken);
        if (!isValid)
        {
            throw new ArgumentException(errorMessage, nameof(request.Subdomain));
        }

        var organization = new Organization
        {
            Name = request.Name,
            Subdomain = request.Subdomain.ToLowerInvariant(),
            Status = OrganizationStatus.Active,
            CreatorIdentityId = creatorUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            Branding = request.Branding != null ? new BrandingConfiguration
            {
                LogoUrl = request.Branding.LogoUrl,
                PrimaryColor = request.Branding.PrimaryColor,
                SecondaryColor = request.Branding.SecondaryColor,
                CompanyTagline = request.Branding.CompanyTagline
            } : null
        };

        var created = await _organizationRepository.CreateAsync(organization, cancellationToken);

        _logger.LogInformation(
            "Created organization {OrganizationId} ({Subdomain}) by user {CreatorUserId}",
            created.Id, created.Subdomain, creatorUserId);

        // Provision organization wallet for signing operations
        try
        {
            var walletName = $"org-{created.Subdomain}-signing";
            var walletInfo = await _walletClient.CreateWalletAsync(
                walletName,
                "ED25519",
                created.Id.ToString(),
                created.Id.ToString(),
                cancellationToken);

            created.WalletAddress = walletInfo.Address;
            created.PublicKey = walletInfo.PublicKey;
            created.SigningAlgorithm = walletInfo.Algorithm;
            await _organizationRepository.UpdateAsync(created, cancellationToken);

            _logger.LogInformation(
                "Organization wallet provisioned: {OrganizationId} -> {WalletAddress}",
                created.Id, walletInfo.Address);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to provision wallet for organization {OrganizationId}. " +
                "Wallet will be provisioned by the reconciliation service.",
                created.Id);
        }

        return OrganizationResponse.FromEntity(created);
    }

    /// <inheritdoc />
    public async Task<OrganizationResponse?> GetOrganizationAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var organization = await _organizationRepository.GetByIdAsync(id, cancellationToken);
        return organization != null ? OrganizationResponse.FromEntity(organization) : null;
    }

    /// <inheritdoc />
    public async Task<OrganizationResponse?> GetOrganizationBySubdomainAsync(
        string subdomain,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subdomain))
        {
            return null;
        }

        var organization = await _organizationRepository.GetBySubdomainAsync(
            subdomain.ToLowerInvariant(), cancellationToken);
        return organization != null ? OrganizationResponse.FromEntity(organization) : null;
    }

    /// <inheritdoc />
    public async Task<OrganizationListResponse> ListOrganizationsAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var organizations = includeInactive
            ? await _organizationRepository.GetAllAsync(cancellationToken)
            : await _organizationRepository.GetAllActiveAsync(cancellationToken);

        return new OrganizationListResponse
        {
            Organizations = organizations.Select(OrganizationResponse.FromEntity).ToList(),
            TotalCount = organizations.Count
        };
    }

    /// <inheritdoc />
    public async Task<OrganizationResponse?> UpdateOrganizationAsync(
        Guid id,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var organization = await _organizationRepository.GetByIdAsync(id, cancellationToken);
        if (organization == null)
        {
            return null;
        }

        // Apply updates
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            organization.Name = request.Name;
        }

        if (request.Status.HasValue)
        {
            organization.Status = request.Status.Value;
        }

        if (request.Branding != null)
        {
            organization.Branding = new BrandingConfiguration
            {
                LogoUrl = request.Branding.LogoUrl,
                PrimaryColor = request.Branding.PrimaryColor,
                SecondaryColor = request.Branding.SecondaryColor,
                CompanyTagline = request.Branding.CompanyTagline
            };
        }

        var updated = await _organizationRepository.UpdateAsync(organization, cancellationToken);

        _logger.LogInformation(
            "Updated organization {OrganizationId} ({Subdomain})",
            updated.Id, updated.Subdomain);

        return OrganizationResponse.FromEntity(updated);
    }

    /// <inheritdoc />
    public async Task<bool> DeactivateOrganizationAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var organization = await _organizationRepository.GetByIdAsync(id, cancellationToken);
        if (organization == null)
        {
            return false;
        }

        await _organizationRepository.DeleteAsync(id, cancellationToken);

        _logger.LogInformation(
            "Deactivated organization {OrganizationId} ({Subdomain})",
            id, organization.Subdomain);

        return true;
    }

    /// <inheritdoc />
    public async Task<UserResponse> AddUserToOrganizationAsync(
        Guid organizationId,
        AddUserToOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Verify organization exists
        var organization = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
        {
            throw new ArgumentException($"Organization {organizationId} not found", nameof(organizationId));
        }

        // Check if user already exists in the target org (scoped check)
        var existingUser = await _dbContext.UserIdentities
            .FirstOrDefaultAsync(u => u.OrganizationId == organizationId
                && u.Email.ToLower() == request.Email.ToLower(), cancellationToken);
        if (existingUser != null)
        {
            throw new InvalidOperationException($"User with email {request.Email} already exists in this organization");
        }

        // Look up existing PlatformUser by email to link identities
        var platformUser = await _dbContext.PlatformUsers
            .FirstOrDefaultAsync(p => p.Email.ToLower() == request.Email.ToLower(), cancellationToken);

        var user = new UserIdentity
        {
            OrganizationId = organizationId,
            PlatformUserId = platformUser?.Id ?? Guid.Empty,
            Email = request.Email,
            DisplayName = request.DisplayName,
            Roles = request.Roles,
            Status = IdentityStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var created = await _identityRepository.CreateUserAsync(user, cancellationToken);

        // Create PlatformUserOrgMembership so the user can switch-org to this org
        if (platformUser != null)
        {
            var existingMembership = await _dbContext.PlatformUserOrgMemberships
                .FirstOrDefaultAsync(m => m.PlatformUserId == platformUser.Id && m.OrganizationId == organizationId, cancellationToken);

            if (existingMembership == null)
            {
                _dbContext.PlatformUserOrgMemberships.Add(new PlatformUserOrgMembership
                {
                    PlatformUserId = platformUser.Id,
                    OrganizationId = organizationId,
                    Role = request.Roles.Any(r => r == UserRole.Administrator)
                        ? UserRole.Administrator.ToString() : UserRole.Member.ToString(),
                    JoinedAt = DateTimeOffset.UtcNow
                });
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        _logger.LogInformation(
            "Added user {UserId} ({Email}) to organization {OrganizationId} (PlatformUser: {HasPlatformUser})",
            created.Id, created.Email, organizationId, platformUser != null);

        return UserResponse.FromEntity(created, platformUser);
    }

    /// <inheritdoc />
    public async Task<UserListResponse> GetOrganizationUsersAsync(
        Guid organizationId,
        bool includeInactive = false,
        bool? emailVerified = null,
        string? provisionedVia = null,
        bool includePending = false,
        CancellationToken cancellationToken = default)
    {
        // Fetch users with basic filters (status, provisioning method)
        var users = await _identityRepository.GetUsersWithFiltersAsync(
            organizationId, includeInactive, provisionedVia, cancellationToken);

        // Fetch PlatformUser data for email verification status
        var platformUserIds = users.Select(u => u.PlatformUserId).Distinct().ToList();
        var platformUsers = await _dbContext.PlatformUsers
            .Where(p => platformUserIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        // Fetch latest OrgInvitation per user email for invitation status
        var userEmails = users.Select(u => u.Email).Distinct().ToList();
        var invitations = await _dbContext.OrgInvitations
            .Where(i => i.OrganizationId == organizationId && userEmails.Contains(i.Email))
            .GroupBy(i => i.Email)
            .Select(g => g.OrderByDescending(i => i.CreatedAt).First())
            .ToDictionaryAsync(i => i.Email, cancellationToken);

        // Build enhanced responses
        var userResponses = users.Select(u =>
        {
            platformUsers.TryGetValue(u.PlatformUserId, out var platformUser);
            invitations.TryGetValue(u.Email, out var invitation);
            return UserResponse.FromEntity(u, platformUser, invitation);
        }).ToList();

        // Apply email verification filter (post-join, since it comes from PlatformUser)
        if (emailVerified.HasValue)
        {
            userResponses = userResponses
                .Where(u => u.EmailVerified == emailVerified.Value)
                .ToList();
        }

        // Fetch pending invitations if requested
        var pendingInvitations = new List<PendingInvitationResponse>();
        var pendingCount = 0;
        if (includePending)
        {
            var pending = await _dbContext.OrgInvitations
                .Where(i => i.OrganizationId == organizationId &&
                            (i.Status == InvitationStatus.Pending || i.Status == InvitationStatus.Expired))
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync(cancellationToken);

            // Exclude invitations for users who already have a UserIdentity
            var existingEmails = users.Select(u => u.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
            pending = pending.Where(i => !existingEmails.Contains(i.Email)).ToList();

            pendingInvitations = pending.Select(PendingInvitationResponse.FromEntity).ToList();
            pendingCount = pendingInvitations.Count;
        }

        return new UserListResponse
        {
            Users = userResponses,
            TotalCount = userResponses.Count,
            PendingInvitations = pendingInvitations,
            PendingInvitationCount = pendingCount
        };
    }

    /// <inheritdoc />
    public async Task<bool> AdminVerifyEmailAsync(
        Guid organizationId,
        Guid userId,
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        // Verify user exists in this organisation
        var user = await _identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user == null || user.OrganizationId != organizationId)
        {
            throw new KeyNotFoundException($"User {userId} not found in organization {organizationId}.");
        }

        // Get PlatformUser to update email verification
        var platformUser = await _dbContext.PlatformUsers
            .FirstOrDefaultAsync(p => p.Id == user.PlatformUserId, cancellationToken);

        if (platformUser == null)
        {
            throw new KeyNotFoundException($"Platform user not found for user {userId}.");
        }

        if (platformUser.EmailVerified)
        {
            return false; // Already verified
        }

        // Set email as verified + record audit event in a single save
        platformUser.EmailVerified = true;
        platformUser.EmailVerifiedAt = DateTimeOffset.UtcNow;
        platformUser.VerificationToken = null;
        platformUser.VerificationTokenExpiresAt = null;

        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = AuditEventType.EmailVerifiedByAdmin,
            IdentityId = adminUserId,
            OrganizationId = organizationId,
            Success = true,
            Details = new Dictionary<string, object>
            {
                ["targetUserId"] = userId.ToString(),
                ["targetEmail"] = user.Email
            }
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Admin {AdminUserId} verified email for user {UserId} in org {OrganizationId}",
            adminUserId, userId, organizationId);

        return true;
    }

    /// <inheritdoc />
    public async Task<UserResponse?> GetOrganizationUserAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user == null || user.OrganizationId != organizationId)
        {
            return null;
        }

        return UserResponse.FromEntity(user);
    }

    /// <inheritdoc />
    public async Task<UserResponse?> UpdateOrganizationUserAsync(
        Guid organizationId,
        Guid userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var user = await _identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user == null || user.OrganizationId != organizationId)
        {
            return null;
        }

        // Apply updates
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            user.DisplayName = request.DisplayName;
        }

        if (request.Roles != null && request.Roles.Length > 0)
        {
            user.Roles = request.Roles;
        }

        if (request.Status.HasValue)
        {
            user.Status = request.Status.Value;
        }

        var updated = await _identityRepository.UpdateUserAsync(user, cancellationToken);

        _logger.LogInformation(
            "Updated user {UserId} ({Email}) in organization {OrganizationId}",
            updated.Id, updated.Email, organizationId);

        return UserResponse.FromEntity(updated);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveUserFromOrganizationAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user == null || user.OrganizationId != organizationId)
        {
            return false;
        }

        await _identityRepository.DeactivateUserAsync(userId, cancellationToken);

        _logger.LogInformation(
            "Removed user {UserId} ({Email}) from organization {OrganizationId}",
            userId, user.Email, organizationId);

        return true;
    }

    /// <inheritdoc />
    public async Task<DomainRestrictionsResponse?> GetDomainRestrictionsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var org = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (org is null)
            return null;

        return new DomainRestrictionsResponse
        {
            AllowedDomains = org.AllowedEmailDomains ?? [],
            RestrictionsActive = org.AllowedEmailDomains is { Length: > 0 }
        };
    }

    /// <inheritdoc />
    public async Task<DomainRestrictionsResponse?> UpdateDomainRestrictionsAsync(
        Guid organizationId,
        string[] allowedDomains,
        Guid updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        var org = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

        if (org is null)
            return null;

        // Normalize domains to lowercase and trim whitespace
        var normalizedDomains = allowedDomains
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();

        var previousDomains = org.AllowedEmailDomains ?? [];
        org.AllowedEmailDomains = normalizedDomains;

        // Write DomainRestrictionUpdated audit event
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = AuditEventType.DomainRestrictionUpdated,
            IdentityId = updatedByUserId,
            OrganizationId = organizationId,
            Details = new Dictionary<string, object>
            {
                ["previousDomains"] = previousDomains,
                ["newDomains"] = normalizedDomains
            }
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Domain restrictions updated for org {OrgId}: {DomainCount} domains configured by user {UserId}",
            organizationId, normalizedDomains.Length, updatedByUserId);

        return new DomainRestrictionsResponse
        {
            AllowedDomains = normalizedDomains,
            RestrictionsActive = normalizedDomains.Length > 0
        };
    }

    /// <inheritdoc />
    public async Task<(bool IsValid, string? ErrorMessage)> ValidateSubdomainAsync(
        string subdomain,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subdomain))
        {
            return (false, "Subdomain is required");
        }

        // Normalize
        subdomain = subdomain.ToLowerInvariant().Trim();

        // Length check (3-50 characters)
        if (subdomain.Length < 3)
        {
            return (false, "Subdomain must be at least 3 characters");
        }

        if (subdomain.Length > 50)
        {
            return (false, "Subdomain cannot exceed 50 characters");
        }

        // Format check (alphanumeric + hyphens, no leading/trailing hyphens)
        if (!SubdomainRegex().IsMatch(subdomain))
        {
            return (false, "Subdomain must contain only lowercase letters, numbers, and hyphens, and cannot start or end with a hyphen");
        }

        // Reserved subdomain check
        if (ReservedSubdomains.Contains(subdomain))
        {
            return (false, $"Subdomain '{subdomain}' is reserved");
        }

        // Availability check
        var exists = await _organizationRepository.SubdomainExistsAsync(subdomain, cancellationToken);
        if (exists)
        {
            return (false, $"Subdomain '{subdomain}' is already taken");
        }

        return (true, null);
    }

    /// <inheritdoc />
    public async Task<OrganizationStatsResponse> GetOrganizationStatsAsync(
        CancellationToken cancellationToken = default)
    {
        var organizations = await _organizationRepository.GetAllActiveAsync(cancellationToken);

        var totalUsers = await _identityRepository.GetTotalActiveUserCountAsync(cancellationToken);

        return new OrganizationStatsResponse
        {
            TotalOrganizations = organizations.Count,
            TotalUsers = totalUsers
        };
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*[a-z0-9]$|^[a-z0-9]$")]
    private static partial Regex SubdomainRegex();
}
