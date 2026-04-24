// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Pages.Auth;

/// <summary>
/// Server-rendered Social OAuth callback page model.
/// Handles the OAuth provider redirect, exchanges the code for user claims,
/// resolves/creates PlatformUser, and issues a JWT via fragment redirect.
/// </summary>
public class SocialCallbackModel : PageModel
{
    private readonly ISocialLoginService _socialLoginService;
    private readonly IPlatformUserService _platformUserService;
    private readonly IIdentityRepository _identityRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ITokenService _tokenService;
    private readonly WelcomeEmailDispatcher _welcomeDispatcher;
    private readonly TenantDbContext _db;
    private readonly ILogger<SocialCallbackModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SocialCallbackModel"/> class.
    /// </summary>
    public SocialCallbackModel(
        ISocialLoginService socialLoginService,
        IPlatformUserService platformUserService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ITokenService tokenService,
        WelcomeEmailDispatcher welcomeDispatcher,
        TenantDbContext db,
        ILogger<SocialCallbackModel> logger)
    {
        _socialLoginService = socialLoginService;
        _platformUserService = platformUserService;
        _identityRepository = identityRepository;
        _organizationRepository = organizationRepository;
        _tokenService = tokenService;
        _welcomeDispatcher = welcomeDispatcher;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Error message to display when the social login flow fails.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// True during the initial page render while the OAuth exchange is in progress.
    /// </summary>
    public bool IsProcessing { get; set; } = true;

    /// <summary>
    /// Handles GET requests from the OAuth provider redirect.
    /// Exchanges the authorization code, resolves/creates user, and redirects with JWT.
    /// </summary>
    public async Task<IActionResult> OnGetAsync(
        string? provider,
        string? code,
        string? state,
        string? error,
        CancellationToken ct)
    {
        IsProcessing = false;

        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Social login callback received error from provider {Provider}: {Error}", provider, error);
            ErrorMessage = "The sign-in was cancelled or failed. Please try again.";
            return Page();
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state) || string.IsNullOrEmpty(provider))
        {
            ErrorMessage = "Invalid callback parameters. Please try signing in again.";
            return Page();
        }

        // Exchange code for claims
        var callbackResult = await _socialLoginService.ExchangeCodeAsync(provider, code, state, ct);
        if (!callbackResult.Success || string.IsNullOrEmpty(callbackResult.Subject))
        {
            _logger.LogWarning("Social login exchange failed for {Provider}: {Error}", provider, callbackResult.Error);
            ErrorMessage = callbackResult.Error ?? "Authentication failed. Please try again.";
            return Page();
        }

        // Resolve or create PlatformUser
        var (platformUser, isNew) = await _platformUserService.ResolveOrCreateSocialUserAsync(
            provider, callbackResult.Subject, callbackResult.Email, callbackResult.DisplayName, ct);

        // Ensure UserIdentity in public org
        var publicOrgId = WellKnownIds.PublicOrgId;
        var userIdentity = await _db.UserIdentities
            .FirstOrDefaultAsync(u => u.PlatformUserId == platformUser.Id && u.OrganizationId == publicOrgId, ct);

        if (userIdentity is null)
        {
            userIdentity = new UserIdentity
            {
                OrganizationId = publicOrgId,
                PlatformUserId = platformUser.Id,
                Email = platformUser.Email,
                DisplayName = platformUser.DisplayName,
                Roles = [UserRole.Consumer],
                Status = IdentityStatus.Active,
                ProvisionedVia = ProvisioningMethod.SocialLogin,
                ProfileCompleted = !string.IsNullOrWhiteSpace(platformUser.Email)
                    && !string.IsNullOrWhiteSpace(platformUser.DisplayName)
            };
            await _identityRepository.CreateUserAsync(userIdentity, ct);
        }

        // Ensure org membership
        var memberships = await _platformUserService.GetOrgMembershipsAsync(platformUser.Id, ct);
        if (!memberships.Any(m => m.OrganizationId == publicOrgId))
        {
            await _platformUserService.AddOrgMembershipAsync(
                platformUser.Id, publicOrgId, UserRole.Consumer.ToString(), ct);
        }

        // Update last login
        userIdentity.LastLoginAt = DateTimeOffset.UtcNow;
        await _identityRepository.UpdateUserAsync(userIdentity, ct);

        // Get public org and issue JWT
        var publicOrg = await _organizationRepository.GetByIdAsync(publicOrgId, ct);
        if (publicOrg is null)
        {
            ErrorMessage = "Platform configuration error. Please contact support.";
            return Page();
        }

        var tokens = await _tokenService.GenerateUserTokenAsync(userIdentity, publicOrg, platformUser.Id, ct);

        _logger.LogInformation("Social login completed for PlatformUser {PlatformUserId} via {Provider} (isNew={IsNew})",
            platformUser.Id, provider, isNew);

        // Welcome email — social/passkey paths skip email verification (IdP already
        // asserted the address), so first-login is the natural welcome moment.
        // Idempotent + non-throwing by design.
        await _welcomeDispatcher.SendIfPendingAsync(platformUser, ct);

        // Redirect to app with token in fragment
        var fragment = $"token={Uri.EscapeDataString(tokens.AccessToken)}" +
                       $"&refresh={Uri.EscapeDataString(tokens.RefreshToken)}";
        return Redirect($"/app/#{fragment}");
    }
}
