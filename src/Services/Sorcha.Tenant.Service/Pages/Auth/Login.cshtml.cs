// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Pages.Auth;

/// <summary>
/// Server-rendered Login page model.
/// Handles email/password authentication, 2FA verification, and token fragment redirect.
/// </summary>
public class LoginModel : PageModel
{
    private readonly ILoginService _loginService;
    private readonly ITotpService _totpService;
    private readonly ITokenService _tokenService;
    private readonly IIdentityRepository _identityRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ILogger<LoginModel> _logger;
    private readonly DemoEnvironmentSettings _demoSettings;
    private readonly ISocialLoginService _socialLoginService;
    private readonly ReturnToAllowlistOptions _returnToAllowlist;
    private readonly IPlatformUserDeviceService _deviceService;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginModel"/> class.
    /// </summary>
    public LoginModel(
        ILoginService loginService,
        ITotpService totpService,
        ITokenService tokenService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ILogger<LoginModel> logger,
        IOptions<DemoEnvironmentSettings> demoSettings,
        ISocialLoginService socialLoginService,
        IOptions<ReturnToAllowlistOptions> returnToAllowlist,
        IPlatformUserDeviceService deviceService)
    {
        _loginService = loginService;
        _totpService = totpService;
        _tokenService = tokenService;
        _identityRepository = identityRepository;
        _organizationRepository = organizationRepository;
        _logger = logger;
        _demoSettings = demoSettings.Value;
        _socialLoginService = socialLoginService;
        _returnToAllowlist = returnToAllowlist?.Value ?? new ReturnToAllowlistOptions();
        _deviceService = deviceService;
    }

    /// <summary>Demo-environment banner flag — when true the page shows the warning notice.</summary>
    public bool DemoBannerEnabled => _demoSettings.Enabled;

    /// <summary>Demo-environment banner copy — rendered HTML-encoded.</summary>
    public string DemoBannerMessage => _demoSettings.Message;

    /// <summary>
    /// Social-login providers configured with non-empty credentials. Drives
    /// conditional rendering of "Continue with..." buttons on the login page.
    /// Feature 115 FR-001/FR-002.
    /// </summary>
    public IReadOnlyList<string> AvailableProviders { get; private set; } = [];

    /// <summary>
    /// User email address.
    /// </summary>
    [BindProperty]
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    /// <summary>
    /// User password.
    /// </summary>
    [BindProperty]
    [Required]
    public string Password { get; set; } = "";

    /// <summary>
    /// TOTP verification code for 2FA flow.
    /// </summary>
    [BindProperty]
    public string? TotpCode { get; set; }

    /// <summary>
    /// Short-lived login token for 2FA verification step.
    /// </summary>
    [BindProperty]
    public string? LoginToken { get; set; }

    /// <summary>
    /// URL to redirect to after successful authentication.
    /// </summary>
    [BindProperty]
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Platform login token for org selection flow.
    /// </summary>
    [BindProperty]
    public string? PlatformLoginToken { get; set; }

    /// <summary>
    /// Selected organisation ID from the org picker.
    /// </summary>
    [BindProperty]
    public Guid? SelectedOrgId { get; set; }

    /// <summary>
    /// Whether to show the 2FA verification form.
    /// </summary>
    public bool ShowTwoFactor { get; set; }

    /// <summary>
    /// Whether to show the org selection form.
    /// </summary>
    public bool ShowOrgSelection { get; set; }

    /// <summary>
    /// Available organisations for the org picker.
    /// </summary>
    public List<OrgChoice>? AvailableOrganizations { get; set; }

    /// <summary>
    /// Available 2FA methods (e.g., "totp", "passkey").
    /// </summary>
    public List<string>? AvailableMethods { get; set; }

    /// <summary>
    /// Error message to display on the page.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Handles GET requests — displays the login form.
    /// </summary>
    /// <param name="returnUrl">Optional return URL to preserve through the auth flow.</param>
    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        AvailableProviders = _socialLoginService.GetConfiguredProviderNames();
    }

    /// <summary>
    /// Handles POST requests — processes login or 2FA verification.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Page result or redirect to app with token fragment.</returns>
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        // 2FA verification flow
        if (!string.IsNullOrEmpty(LoginToken) && !string.IsNullOrEmpty(TotpCode))
        {
            return await Handle2FaAsync(ct);
        }

        // Org selection completion flow
        if (!string.IsNullOrEmpty(PlatformLoginToken) && SelectedOrgId.HasValue && SelectedOrgId != Guid.Empty)
        {
            return await HandleOrgSelectionAsync(ct);
        }

        // Primary login flow
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _loginService.LoginAsync(Email, Password, ct);

        if (!result.Success && !result.TwoFactorRequired && !result.OrgSelectionRequired)
        {
            ErrorMessage = result.Error ?? "Login failed.";
            return Page();
        }

        // Org selection required — show org picker
        if (result.OrgSelectionRequired)
        {
            ShowOrgSelection = true;
            PlatformLoginToken = result.PlatformLoginToken;
            AvailableOrganizations = result.AvailableOrganizations;
            return Page();
        }

        if (result.TwoFactorRequired)
        {
            ShowTwoFactor = true;
            LoginToken = result.LoginToken;
            AvailableMethods = result.AvailableMethods;
            return Page();
        }

        return RedirectToApp(result.Tokens!);
    }

    private async Task<IActionResult> HandleOrgSelectionAsync(CancellationToken ct)
    {
        var result = await _loginService.CompleteOrgSelectionAsync(
            PlatformLoginToken!, SelectedOrgId!.Value, ct);

        if (!result.Success)
        {
            ErrorMessage = result.Error ?? "Organization selection failed. Please sign in again.";
            return Page();
        }

        if (result.TwoFactorRequired)
        {
            ShowTwoFactor = true;
            LoginToken = result.LoginToken;
            AvailableMethods = result.AvailableMethods;
            // Preserve SelectedOrgId through the 2FA step so Handle2FaAsync
            // can issue the JWT for the correct org
            return Page();
        }

        return RedirectToApp(result.Tokens!);
    }

    private async Task<IActionResult> Handle2FaAsync(CancellationToken ct)
    {
        var userId = await _totpService.ValidateLoginTokenAsync(LoginToken!, ct);
        if (userId is null)
        {
            ErrorMessage = "Login session expired. Please sign in again.";
            return Page();
        }

        var isValid = await _totpService.ValidateCodeAsync(userId.Value, TotpCode!, ct);
        if (!isValid)
        {
            ShowTwoFactor = true;
            LoginToken = LoginToken;
            ErrorMessage = "Invalid verification code.";
            return Page();
        }

        var user = await _identityRepository.GetUserByIdAsync(userId.Value, ct);
        if (user is null)
        {
            ErrorMessage = "User not found.";
            return Page();
        }

        // Use the selected org from the org selection step if available;
        // otherwise fall back to the user's identity org (single-org login).
        var targetOrgId = SelectedOrgId.HasValue && SelectedOrgId != Guid.Empty
            ? SelectedOrgId.Value
            : user.OrganizationId;

        var organization = await _organizationRepository.GetByIdAsync(targetOrgId, ct);
        if (organization is null)
        {
            ErrorMessage = "Organization not found.";
            return Page();
        }

        // For multi-org users, find the UserIdentity in the selected org.
        // Do NOT fall back to the original user — that would create a mismatch
        // between the org claims and the user identity (security issue).
        UserIdentity targetUser;
        if (targetOrgId != user.OrganizationId)
        {
            var orgUser = await _identityRepository.GetUserByPlatformUserAndOrgAsync(
                user.PlatformUserId, targetOrgId, ct);
            if (orgUser is null)
            {
                _logger.LogWarning(
                    "2FA org mismatch: user {UserId} is not a member of org {OrgId}",
                    user.PlatformUserId, targetOrgId);
                ErrorMessage = "You are not a member of the selected organization.";
                return Page();
            }
            targetUser = orgUser;
        }
        else
        {
            targetUser = user;
        }

        var tokens = await _tokenService.GenerateUserTokenAsync(targetUser, organization, targetUser.PlatformUserId, cancellationToken: ct);
        targetUser.LastLoginAt = DateTimeOffset.UtcNow;
        await _identityRepository.UpdateUserAsync(targetUser, ct);

        return RedirectToApp(tokens);
    }

    private IActionResult RedirectToApp(TokenResponse tokens)
    {
        // Feature 128 US2 — when the citizen has no paired device and no
        // explicit ReturnUrl override, route them to /app/setup/add-device so
        // the WASM client lands on the pairing handoff page (FR-020). The
        // routing-gate contract is shared with the OIDC and Social callbacks
        // via SetupAddDeviceRoutingGate; the /app/ prefix rationale lives
        // there. Citizens who already have a paired device follow the
        // existing ReturnUrl / "" behaviour unchanged (FR-026).
        var returnUrl = IsValidReturnUrl(ReturnUrl, _returnToAllowlist) ? ReturnUrl : "";
        if (string.IsNullOrEmpty(returnUrl)
            && SetupAddDeviceRoutingGate.ShouldRoute(tokens.AccessToken, _deviceService, _logger))
        {
            returnUrl = SetupAddDeviceRoutingGate.SetupAddDevicePath;
        }

        var fragment = $"token={Uri.EscapeDataString(tokens.AccessToken)}" +
                       $"&refresh={Uri.EscapeDataString(tokens.RefreshToken)}";
        if (!string.IsNullOrEmpty(returnUrl))
        {
            fragment += $"&returnUrl={Uri.EscapeDataString(returnUrl!)}";
        }
        return Redirect($"/app/#{fragment}");
    }

    /// <summary>
    /// Validates the return URL — accepts (a) internal relative paths and (b)
    /// absolute URLs whose host matches the Feature 126 return-to allowlist
    /// (so council pages can carry the citizen back to e.g. strathcarron.gov
    /// after F116 signup completes).
    /// </summary>
    internal static bool IsValidReturnUrl(string? url, ReturnToAllowlistOptions allowlist)
    {
        if (string.IsNullOrEmpty(url)) return false;

        // Internal relative path — the existing behaviour. Reject "//host" as
        // a protocol-relative URL footgun.
        if (url.StartsWith('/') && !url.StartsWith("//")) return true;

        // Absolute URL — must match the allowlist. Open redirects fail closed.
        return allowlist.IsAllowed(url);
    }
}
