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
    private readonly ITenantMembershipInboxWriter _membershipInbox;
    private readonly ILogger<OrganizationService> _logger;
    private readonly bool _allowAdminVerifiedUserCreation;
    // Feature 181 US5 — optional so existing test constructions of OrganizationService keep compiling
    // (the F143 optional-ctor-param pattern); production DI always supplies it.
    private readonly Sorcha.Tenant.Service.Trust.IOrgCertificateService? _orgCertService;

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
        ITenantMembershipInboxWriter membershipInbox,
        ILogger<OrganizationService> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        Sorcha.Tenant.Service.Trust.IOrgCertificateService? orgCertService = null)
    {
        _organizationRepository = organizationRepository ?? throw new ArgumentNullException(nameof(organizationRepository));
        _identityRepository = identityRepository ?? throw new ArgumentNullException(nameof(identityRepository));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _membershipInbox = membershipInbox ?? throw new ArgumentNullException(nameof(membershipInbox));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _orgCertService = orgCertService;
        // Same deployment-level gate as OrgProvisioningService: emailVerified bypass is off by
        // default (incl. production). See Platform:AllowAdminVerifiedUserCreation (spec 136 follow-up).
        _allowAdminVerifiedUserCreation =
            configuration?.GetValue("Platform:AllowAdminVerifiedUserCreation", false) ?? false;
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

        // The organisation's wallet is DELIBERATELY not created here (#1525).
        //
        // Creating it server-side means generating a BIP39 recovery phrase with no human present to
        // receive it. The phrase is shown once at creation and never stored — that is the design —
        // so a service-to-service create silently destroys it, and the organisation can never be
        // recovered. Every org wallet in existence before this change was minted that way.
        //
        // It is also not the platform's secret to hold: the recovery phrase belongs to the ORG
        // ADMIN. So the org is created without a wallet and the org admin creates it themselves via
        // POST /api/organizations/{id}/wallet, having taken the phrase from the Wallet Service
        // directly. A null WalletAddress IS the "awaiting its wallet" state.

        return OrganizationResponse.FromEntity(created);
    }

    /// <inheritdoc />
    public async Task<OrganizationResponse?> LinkOrganizationWalletAsync(
        Guid organizationId,
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);

        var org = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken);
        if (org is null)
        {
            return null;
        }

        // Never silently re-link. The canonical wallet anchors the org's issuer DID and its
        // governance roster identity, so replacing it orphans every credential issued under the old
        // one and every roster entry matched against it. Changing it is a deliberate migration, not
        // a side effect of calling this twice.
        if (!string.IsNullOrWhiteSpace(org.WalletAddress))
        {
            throw new InvalidOperationException(
                $"Organisation {organizationId} already has a wallet ({org.WalletAddress}).");
        }

        // Prove the wallet belongs to this organisation. Without this an admin could adopt any
        // wallet whose address they happen to know — addresses are public — and the org's issuer
        // DID would then anchor on a key they do not control.
        var wallet = await _walletClient.GetWalletAsync(walletAddress, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Wallet '{walletAddress}' does not exist.");

        if (!string.Equals(wallet.Owner, organizationId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Wallet '{walletAddress}' is not owned by organisation {organizationId}.");
        }

        org.WalletAddress = wallet.Address;
        org.PublicKey = wallet.PublicKey;
        org.SigningAlgorithm = wallet.Algorithm;
        await _organizationRepository.UpdateAsync(org, cancellationToken);

        _logger.LogInformation(
            "Organisation wallet linked: {OrganizationId} -> {WalletAddress} (created by its org admin)",
            org.Id, wallet.Address);

        // Feature 181 US5 — the X.509 auto-enrol ride-along used to hang off server-side wallet
        // provisioning. That is gone, so it rides here instead: this is now the moment an org first
        // has a wallet, and without it orgs would silently stop getting certificates.
        await TryAutoEnrolCertificateAsync(org, cancellationToken);

        return OrganizationResponse.FromEntity(org);
    }

    /// <summary>
    /// Feature 181 US5 (T049) — best-effort auto-enrol of an org's internal X.509 certificate immediately
    /// after wallet provisioning. Never throws: an eligibility miss or transient failure is logged and left
    /// to the reconciliation ride-along (FR-022). Uses the org id as the tenant (org-as-tenant model) and
    /// the system principal (<see cref="Guid.Empty"/>) as the creator.
    /// </summary>
    private async Task TryAutoEnrolCertificateAsync(Organization org, CancellationToken ct)
    {
        if (_orgCertService is null || string.IsNullOrWhiteSpace(org.WalletAddress))
        {
            return;
        }

        try
        {
            var result = await _orgCertService.EnrolInternalAsync(
                org.Id.ToString(), org.WalletAddress!, org.Name, Guid.Empty, ct);
            if (result.Success)
            {
                _logger.LogInformation(
                    "Auto-enrolled internal certificate for organization {OrganizationId} ({Reissued})",
                    org.Id, result.Reissued ? "reissued" : "new");
            }
            else
            {
                _logger.LogWarning(
                    "Auto-enrol skipped for organization {OrganizationId}: {Reason} (X.509 rail not eligible)",
                    org.Id, result.ErrorCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Auto-enrol failed for organization {OrganizationId}. The X.509 rail is best-effort and nothing retries it now the reconciliation sweep is gone (#1525) — re-run enrolment explicitly if the org needs a certificate.",
                org.Id);
        }
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
                var newMembershipRole = request.Roles.Any(r => r == UserRole.Administrator)
                    ? UserRole.Administrator.ToString() : UserRole.Consumer.ToString();
                _dbContext.PlatformUserOrgMemberships.Add(new PlatformUserOrgMembership
                {
                    PlatformUserId = platformUser.Id,
                    OrganizationId = organizationId,
                    Role = newMembershipRole,
                    JoinedAt = DateTimeOffset.UtcNow
                });
                await _dbContext.SaveChangesAsync(cancellationToken);

                // Feature 118 — drop a "welcome to {org}" inbox entry once the membership
                // is committed. Writer is fail-safe (try/log/swallow internally).
                await _membershipInbox.WriteOrgMembershipAddedAsync(
                    platformUser.Id, organizationId, newMembershipRole, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation(
            "Added user {UserId} ({Email}) to organization {OrganizationId} (PlatformUser: {HasPlatformUser})",
            created.Id, created.Email, organizationId, platformUser != null);

        return UserResponse.FromEntity(created, platformUser);
    }

    /// <inheritdoc />
    public async Task<UserResponse> ProvisionOrgUserAsync(
        Guid organizationId,
        ProvisionOrgUserRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var organization = await _organizationRepository.GetByIdAsync(organizationId, cancellationToken)
            ?? throw new ArgumentException($"Organization {organizationId} not found", nameof(organizationId));

        // Verified bypass gate (off by default, incl. production) — spec 136 follow-up.
        if (request.EmailVerified && !_allowAdminVerifiedUserCreation)
        {
            throw new InvalidOperationException(
                "Creating pre-verified users is not enabled on this installation (Platform:AllowAdminVerifiedUserCreation).");
        }

        // This provisions a NEW org-scoped user. If a PlatformUser already exists for the email,
        // refuse — adding an existing (possibly public) user here would create the very multi-org
        // situation this endpoint exists to avoid. Use AddUserToOrganization for existing users.
        var existingPlatform = await _dbContext.PlatformUsers
            .FirstOrDefaultAsync(p => p.Email.ToLower() == request.Email.ToLower(), cancellationToken);
        if (existingPlatform is not null)
        {
            throw new InvalidOperationException(
                $"A platform user with email {request.Email} already exists; provision creates a NEW org-scoped user.");
        }

        var platformUser = new PlatformUser
        {
            Email = request.Email,
            DisplayName = request.DisplayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            EmailVerified = request.EmailVerified,
            EmailVerifiedAt = request.EmailVerified ? DateTimeOffset.UtcNow : null,
            Status = PlatformUserStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _dbContext.PlatformUsers.Add(platformUser);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var user = new UserIdentity
        {
            OrganizationId = organizationId,
            PlatformUserId = platformUser.Id,
            Email = request.Email,
            DisplayName = request.DisplayName,
            Roles = request.Roles is { Length: > 0 } ? request.Roles : new[] { UserRole.Consumer },
            Status = IdentityStatus.Active,
            ProvisionedVia = ProvisioningMethod.AdminCreated,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var created = await _identityRepository.CreateUserAsync(user, cancellationToken);

        var membershipRole = user.Roles.Any(r => r == UserRole.Administrator)
            ? UserRole.Administrator.ToString() : UserRole.Consumer.ToString();
        _dbContext.PlatformUserOrgMemberships.Add(new PlatformUserOrgMembership
        {
            PlatformUserId = platformUser.Id,
            OrganizationId = organizationId,
            Role = membershipRole,
            JoinedAt = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _membershipInbox.WriteOrgMembershipAddedAsync(
            platformUser.Id, organizationId, membershipRole, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Provisioned org-scoped user {UserId} ({Email}) in organization {OrganizationId} (verified={Verified})",
            created.Id, created.Email, organizationId, request.EmailVerified);

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
