// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Pages.Auth;

/// <summary>
/// Server-rendered OIDC callback page model.
/// Receives the authorization code and state from the organization's external IDP,
/// exchanges the code for user claims via PKCE, provisions or matches the user account,
/// and issues a Sorcha JWT. Supports an optional profile completion step when the
/// IDP does not return all required claims (email, display name).
/// On success, redirects to the main application with the token in the URL fragment.
/// On failure, renders an error message with a link back to the login page.
/// </summary>
public class OidcCallbackModel : PageModel
{
    private readonly IOidcExchangeService _oidcExchangeService;
    private readonly IOidcProvisioningService _oidcProvisioningService;
    private readonly ITokenService _tokenService;
    private readonly ITotpService _totpService;
    private readonly IIdentityRepository _identityRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly TenantDbContext _dbContext;
    private readonly ILogger<OidcCallbackModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcCallbackModel"/> class.
    /// </summary>
    public OidcCallbackModel(
        IOidcExchangeService oidcExchangeService,
        IOidcProvisioningService oidcProvisioningService,
        ITokenService tokenService,
        ITotpService totpService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        TenantDbContext dbContext,
        ILogger<OidcCallbackModel> logger)
    {
        _oidcExchangeService = oidcExchangeService;
        _oidcProvisioningService = oidcProvisioningService;
        _tokenService = tokenService;
        _totpService = totpService;
        _identityRepository = identityRepository;
        _organizationRepository = organizationRepository;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Error message to display when the OIDC callback flow fails.
    /// Null when processing is successful or a profile form is shown.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// True when the IDP did not return all required claims and the user must
    /// provide their display name and/or email before a token can be issued.
    /// </summary>
    public bool RequiresProfileCompletion { get; set; }

    /// <summary>
    /// Display name entered on the profile completion form.
    /// </summary>
    [BindProperty]
    [MaxLength(100)]
    public string ProfileDisplayName { get; set; } = "";

    /// <summary>
    /// Email address entered on the profile completion form.
    /// </summary>
    [BindProperty]
    [EmailAddress]
    [MaxLength(256)]
    public string ProfileEmail { get; set; } = "";

    /// <summary>
    /// Opaque short-lived login token preserving the authenticated user identity
    /// between the GET exchange step and the profile completion POST.
    /// Validated by <see cref="ITotpService.ValidateLoginTokenAsync"/> on POST.
    /// </summary>
    [BindProperty]
    public string OidcState { get; set; } = "";

    /// <summary>
    /// Handles GET requests from the organization IDP redirect.
    /// Validates the <paramref name="code"/> and <paramref name="state"/> query parameters,
    /// exchanges the authorization code, provisions or matches the user account,
    /// and either redirects to the app with a JWT fragment or renders the profile completion form.
    /// </summary>
    /// <param name="code">Authorization code received from the external IDP.</param>
    /// <param name="state">CSRF state parameter generated during OIDC initiation.</param>
    /// <param name="orgSubdomain">Organization subdomain used to resolve the IDP configuration.</param>
    /// <param name="error">Optional error parameter set by the IDP if the user denied access.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Redirect to app on success, page with profile form or error message on failure.</returns>
    public async Task<IActionResult> OnGetAsync(
        string? code,
        string? state,
        string? orgSubdomain,
        string? error,
        CancellationToken ct)
    {
        // Handle provider-side errors (e.g., user denied access)
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogInformation("OIDC login denied by user or IDP error: {Error}", error);
            ErrorMessage = "Sign-in was cancelled or denied. Please try again.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            _logger.LogWarning("OIDC callback received with missing authorization code");
            ErrorMessage = "Invalid callback request: missing authorization code.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            _logger.LogWarning("OIDC callback received with missing state parameter");
            ErrorMessage = "Invalid callback request: missing state parameter.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(orgSubdomain))
        {
            _logger.LogWarning("OIDC callback received with missing orgSubdomain parameter");
            ErrorMessage = "Invalid callback request: missing organization identifier.";
            return Page();
        }

        try
        {
            // Exchange the authorization code for validated claims via PKCE
            var exchangeResult = await _oidcExchangeService.ExchangeCodeAsync(code, state, orgSubdomain, ct);

            if (!exchangeResult.Success)
            {
                _logger.LogWarning(
                    "OIDC code exchange failed for org {OrgSubdomain}: {Error}",
                    orgSubdomain, exchangeResult.Error);
                ErrorMessage = "Sign-in failed. The authorization code may have expired. Please try again.";
                return Page();
            }

            // Check email domain restrictions before provisioning
            var email = exchangeResult.Claims!.Email;
            if (!string.IsNullOrEmpty(email))
            {
                var domainAllowed = await _oidcProvisioningService.CheckDomainRestrictionsAsync(
                    exchangeResult.OrgId, email, ct);

                if (!domainAllowed)
                {
                    _logger.LogWarning(
                        "OIDC login blocked: email domain not allowed for org {OrgId}, email {Email}",
                        exchangeResult.OrgId, email);
                    ErrorMessage = "Your email domain is not allowed for this organization.";
                    return Page();
                }
            }

            // Provision or match the user account
            var (user, isFirstLogin) = await _oidcProvisioningService.ProvisionOrMatchUserAsync(
                exchangeResult.OrgId, exchangeResult.Claims!, ct);

            // Check if profile completion is required (missing email or display name)
            var needsProfile = await _oidcProvisioningService.DetermineProfileCompletionAsync(user);
            if (needsProfile)
            {
                // Generate a short-lived opaque token to safely carry the userId across the POST
                var partialToken = await _totpService.GenerateLoginTokenAsync(user.Id, ct);
                OidcState = partialToken;
                RequiresProfileCompletion = true;

                // Pre-populate with any claims the IDP did provide
                ProfileDisplayName = exchangeResult.Claims!.DisplayName ?? "";
                ProfileEmail = exchangeResult.Claims!.Email ?? "";

                _logger.LogInformation(
                    "OIDC callback requires profile completion for user {UserId}", user.Id);
                return Page();
            }

            // Log audit event
            _dbContext.AuditLogEntries.Add(new AuditLogEntry
            {
                EventType = isFirstLogin ? AuditEventType.OidcFirstLogin : AuditEventType.Login,
                IdentityId = user.Id,
                OrganizationId = exchangeResult.OrgId,
                Timestamp = DateTimeOffset.UtcNow
            });
            await _dbContext.SaveChangesAsync(ct);

            // Resolve org and issue full JWT
            var org = await _organizationRepository.GetByIdAsync(exchangeResult.OrgId, ct);
            if (org is null)
            {
                _logger.LogError("Organization {OrgId} not found after successful OIDC exchange", exchangeResult.OrgId);
                ErrorMessage = "Organization not found. Please contact your administrator.";
                return Page();
            }

            var tokenResponse = await _tokenService.GenerateUserTokenAsync(user, org, ct);

            _logger.LogInformation(
                "OIDC login completed for org {OrgSubdomain}: userId={UserId}, isFirstLogin={IsFirstLogin}",
                orgSubdomain, user.Id, isFirstLogin);

            return RedirectToApp(tokenResponse);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "OIDC callback flow error for org {OrgSubdomain}", orgSubdomain);
            ErrorMessage = "Sign-in failed. The login session may have expired. Please try again.";
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during OIDC callback for org {OrgSubdomain}", orgSubdomain);
            ErrorMessage = "An unexpected error occurred. Please try again.";
            return Page();
        }
    }

    /// <summary>
    /// Handles POST requests for the profile completion form.
    /// Validates the opaque OIDC state token, updates the user's missing profile fields,
    /// and issues a full JWT on success.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Redirect to app on success, page with error on failure.</returns>
    public async Task<IActionResult> OnPostProfileAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(OidcState))
        {
            ErrorMessage = "Login session expired. Please sign in again.";
            return Page();
        }

        // Validate the partial token to securely retrieve the user ID
        var userId = await _totpService.ValidateLoginTokenAsync(OidcState, ct);
        if (userId is null)
        {
            _logger.LogWarning("OIDC profile completion: expired or invalid OidcState token");
            ErrorMessage = "Login session expired. Please sign in again.";
            return Page();
        }

        var user = await _identityRepository.GetUserByIdAsync(userId.Value, ct);
        if (user is null)
        {
            _logger.LogWarning("OIDC profile completion: user {UserId} not found", userId.Value);
            ErrorMessage = "User account not found. Please sign in again.";
            return Page();
        }

        var org = await _organizationRepository.GetByIdAsync(user.OrganizationId, ct);
        if (org is null)
        {
            _logger.LogError("OIDC profile completion: organization {OrgId} not found for user {UserId}",
                user.OrganizationId, user.Id);
            ErrorMessage = "Organization not found. Please contact your administrator.";
            return Page();
        }

        // Validate email domain restrictions if a new email is being set
        if (!string.IsNullOrWhiteSpace(ProfileEmail) && org.AllowedEmailDomains is { Length: > 0 })
        {
            var emailDomain = ProfileEmail.Split('@').LastOrDefault();
            if (emailDomain is null || !org.AllowedEmailDomains.Contains(emailDomain, StringComparer.OrdinalIgnoreCase))
            {
                RequiresProfileCompletion = true;
                ErrorMessage = "Registration is restricted to specific email domains.";
                return Page();
            }
        }

        // Apply profile updates for fields that were missing
        if (!string.IsNullOrWhiteSpace(ProfileDisplayName))
            user.DisplayName = ProfileDisplayName.Trim();

        if (!string.IsNullOrWhiteSpace(ProfileEmail))
            user.Email = ProfileEmail.Trim();

        user.ProfileCompleted = !string.IsNullOrWhiteSpace(user.Email)
            && !string.IsNullOrWhiteSpace(user.DisplayName);

        await _identityRepository.UpdateUserAsync(user, ct);

        // Audit profile completion
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            EventType = AuditEventType.Login,
            IdentityId = user.Id,
            OrganizationId = org.Id,
            Timestamp = DateTimeOffset.UtcNow
        });
        await _dbContext.SaveChangesAsync(ct);

        var tokenResponse = await _tokenService.GenerateUserTokenAsync(user, org, ct);

        _logger.LogInformation(
            "OIDC profile completion succeeded for user {UserId}", user.Id);

        return RedirectToApp(tokenResponse);
    }

    private IActionResult RedirectToApp(TokenResponse tokens)
    {
        var fragment = $"token={Uri.EscapeDataString(tokens.AccessToken)}" +
                       $"&refresh={Uri.EscapeDataString(tokens.RefreshToken)}";
        return Redirect($"/app/#{fragment}");
    }
}
