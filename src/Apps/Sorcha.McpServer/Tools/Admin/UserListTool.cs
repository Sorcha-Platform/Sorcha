// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Admin tool for listing users within an organisation. Reads via the typed
/// <see cref="ITenantServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the route is contract-pinned.
/// </summary>
[McpServerToolType]
public sealed class UserListTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<UserListTool> _logger;

    public UserListTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<UserListTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>
    /// Lists the users belonging to an organisation.
    /// </summary>
    /// <param name="organizationId">Organisation ID whose users to list (required — GUID).</param>
    /// <param name="includeInactive">Include suspended/deleted users as well as active ones (default: false).</param>
    /// <param name="emailVerified">Filter to users whose email is (or is not) verified (optional).</param>
    /// <param name="provisionedVia">Filter by provisioning method: Local, Oidc, Invitation, SocialLogin, AdminCreated, Passkey (optional).</param>
    /// <param name="includePending">Also return pending (not yet accepted) invitations (default: false).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of users in the organisation.</returns>
    [McpServerTool(Name = "sorcha_user_list")]
    [Description("Returns every user in one organisation — id, email, display name, roles, status, and provisioning metadata — with optional filters on inactive-inclusion, email-verified state, and provisioning method. Call this when you need to discover a user ID, audit role assignments, or confirm a user exists before mutating them; prefer this over sorcha_user_manage when the goal is read-only enquiry rather than mutation, and call before sorcha_user_manage or sorcha_token_revoke so the subsequent mutation targets the correct user ID. NOTE: the underlying endpoint (GET /api/organizations/{organizationId}/users) has no role/status/free-text filter and no pagination — every matching user is returned in one response, so those parameters are not offered.")]
    public async Task<UserListResult> ListUsersAsync(
        [Description("Organisation ID whose users to list (required — GUID)")] string organizationId,
        [Description("Include suspended/deleted users as well as active ones (default: false)")] bool includeInactive = false,
        [Description("Filter to users whose email is (or is not) verified (optional)")] bool? emailVerified = null,
        [Description("Filter by provisioning method: Local, Oidc, Invitation, SocialLogin, AdminCreated, Passkey (optional)")] string? provisionedVia = null,
        [Description("Also return pending (not yet accepted) invitations (default: false)")] bool includePending = false,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_user_list"))
        {
            return new UserListResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return new UserListResult
            {
                Status = "Error",
                Message = "Organization ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!Guid.TryParse(organizationId, out _))
        {
            return new UserListResult
            {
                Status = "Error",
                Message = "Organization ID must be a valid GUID.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Tenant"))
        {
            return new UserListResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Listing users for organization {OrganizationId}. IncludeInactive: {IncludeInactive}",
            organizationId, includeInactive);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Build query string — only fields GetOrganizationUsers actually binds.
            var queryParams = new List<string> { $"includeInactive={includeInactive}" };

            if (emailVerified.HasValue)
                queryParams.Add($"emailVerified={emailVerified.Value}");

            if (!string.IsNullOrWhiteSpace(provisionedVia))
                queryParams.Add($"provisionedVia={Uri.EscapeDataString(provisionedVia)}");

            if (includePending)
                queryParams.Add("includePending=true");

            // Typed client forwards the caller's bearer and pins the route
            // (GET api/organizations/{organizationId}/users).
            var responseContent = await _tenantClient.ListUsersAsync(
                organizationId, string.Join("&", queryParams), cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _availabilityTracker.RecordSuccess("Tenant");

                return new UserListResult
                {
                    Status = "Error",
                    Message = "Failed to retrieve users.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess("Tenant");

            var result = JsonSerializer.Deserialize<OrganizationUserListResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new UserListResult
                {
                    Status = "Error",
                    Message = "Failed to parse user list response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved {Count} users in {ElapsedMs}ms",
                result.Users?.Count ?? 0, stopwatch.ElapsedMilliseconds);

            return new UserListResult
            {
                Status = "Success",
                Message = $"Retrieved {result.Users?.Count ?? 0} user(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Users = result.Users?.Select(u => new UserInfo
                {
                    UserId = u.Id.ToString(),
                    OrganizationId = u.OrganizationId.ToString(),
                    Email = u.Email ?? "",
                    DisplayName = u.DisplayName ?? "",
                    Roles = u.Roles ?? [],
                    Status = u.Status ?? "Active",
                    CreatedAt = u.CreatedAt,
                    LastLoginAt = u.LastLoginAt,
                    EmailVerified = u.EmailVerified,
                    EmailVerifiedAt = u.EmailVerifiedAt,
                    ProvisionedVia = u.ProvisionedVia ?? "",
                    ProfileCompleted = u.ProfileCompleted,
                    InvitationStatus = u.InvitationStatus
                }).ToList() ?? [],
                TotalCount = result.TotalCount,
                PendingInvitations = result.PendingInvitations?.Select(p => new PendingInvitationInfo
                {
                    Email = p.Email ?? "",
                    AssignedRole = p.AssignedRole ?? "",
                    InvitationStatus = p.InvitationStatus ?? "",
                    ExpiresAt = p.ExpiresAt,
                    CreatedAt = p.CreatedAt
                }).ToList() ?? [],
                PendingInvitationCount = result.PendingInvitationCount
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant");

            return new UserListResult
            {
                Status = "Timeout",
                Message = "User list request timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant", ex);

            return new UserListResult
            {
                Status = "Error",
                Message = $"Failed to connect to Tenant service: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant", ex);

            _logger.LogError(ex, "Unexpected error listing users");

            return new UserListResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while listing users.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models — mirror Sorcha.Tenant.Service.Models.Dtos.UserListResponse /
    // UserResponse / PendingInvitationResponse, the actual wire shape of
    // GET /api/organizations/{organizationId}/users. That endpoint has no Page/PageSize/TotalPages
    // (no pagination at all) and its collection property is "Users", not "Items" — both differed
    // from what this tool previously assumed.
    private sealed class OrganizationUserListResponse
    {
        public List<UserResponseDto>? Users { get; set; }
        public int TotalCount { get; set; }
        public List<PendingInvitationResponseDto>? PendingInvitations { get; set; }
        public int PendingInvitationCount { get; set; }
    }

    private sealed class UserResponseDto
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
        public List<string>? Roles { get; set; }
        public string? Status { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastLoginAt { get; set; }
        public bool EmailVerified { get; set; }
        public DateTimeOffset? EmailVerifiedAt { get; set; }
        public string? ProvisionedVia { get; set; }
        public bool ProfileCompleted { get; set; }
        public string? InvitationStatus { get; set; }
    }

    private sealed class PendingInvitationResponseDto
    {
        public string? Email { get; set; }
        public string? AssignedRole { get; set; }
        public string? InvitationStatus { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }
}

/// <summary>
/// Result of listing users.
/// </summary>
public sealed record UserListResult
{
    /// <summary>
    /// Operation status: Success, Error, Unavailable, Timeout, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the operation result.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// When the operation was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// List of users in the organisation.
    /// </summary>
    public IReadOnlyList<UserInfo> Users { get; init; } = [];

    /// <summary>
    /// Total number of users returned (this endpoint does not paginate).
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Pending (not yet accepted) invitations, populated only when includePending was requested.
    /// </summary>
    public IReadOnlyList<PendingInvitationInfo> PendingInvitations { get; init; } = [];

    /// <summary>
    /// Count of pending invitations for this organisation.
    /// </summary>
    public int PendingInvitationCount { get; init; }
}

/// <summary>
/// Information about a user.
/// </summary>
public sealed record UserInfo
{
    /// <summary>
    /// Unique user ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Organisation ID this user belongs to.
    /// </summary>
    public required string OrganizationId { get; init; }

    /// <summary>
    /// User email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// User display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// User roles: SystemAdmin, Administrator, Designer, Auditor, Consumer.
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>
    /// User account status: Active, Suspended, Deleted.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// When the user was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Last login timestamp.
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; init; }

    /// <summary>
    /// Whether the user's email address has been verified.
    /// </summary>
    public bool EmailVerified { get; init; }

    /// <summary>
    /// When the email was verified, if it has been.
    /// </summary>
    public DateTimeOffset? EmailVerifiedAt { get; init; }

    /// <summary>
    /// How the user was provisioned (Local, Oidc, Invitation, SocialLogin, AdminCreated, Passkey).
    /// </summary>
    public string ProvisionedVia { get; init; } = "";

    /// <summary>
    /// Whether the user has completed their profile.
    /// </summary>
    public bool ProfileCompleted { get; init; }

    /// <summary>
    /// Status of the organisation invitation for this user, if any (Pending, Accepted, Expired, Revoked).
    /// </summary>
    public string? InvitationStatus { get; init; }
}

/// <summary>
/// A pending (not yet accepted) organisation invitation.
/// </summary>
public sealed record PendingInvitationInfo
{
    /// <summary>
    /// Email address the invitation was sent to.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// Role that will be assigned upon acceptance.
    /// </summary>
    public required string AssignedRole { get; init; }

    /// <summary>
    /// Current invitation status (Pending or Expired).
    /// </summary>
    public required string InvitationStatus { get; init; }

    /// <summary>
    /// Invitation expiry timestamp.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// When the invitation was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }
}
