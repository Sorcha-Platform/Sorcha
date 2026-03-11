// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sorcha.Tenant.Service.Data.Repositories;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginModel"/> class.
    /// </summary>
    public LoginModel(
        ILoginService loginService,
        ITotpService totpService,
        ITokenService tokenService,
        IIdentityRepository identityRepository,
        IOrganizationRepository organizationRepository,
        ILogger<LoginModel> logger)
    {
        _loginService = loginService;
        _totpService = totpService;
        _tokenService = tokenService;
        _identityRepository = identityRepository;
        _organizationRepository = organizationRepository;
        _logger = logger;
    }

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
    /// Whether to show the 2FA verification form.
    /// </summary>
    public bool ShowTwoFactor { get; set; }

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

        // Primary login flow
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _loginService.LoginAsync(Email, Password, ct);

        if (!result.Success && !result.TwoFactorRequired)
        {
            ErrorMessage = result.Error ?? "Login failed.";
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

        var organization = await _organizationRepository.GetByIdAsync(user.OrganizationId, ct);
        if (organization is null)
        {
            ErrorMessage = "Organization not found.";
            return Page();
        }

        var tokens = await _tokenService.GenerateUserTokenAsync(user, organization, ct);
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _identityRepository.UpdateUserAsync(user, ct);

        return RedirectToApp(tokens);
    }

    private IActionResult RedirectToApp(TokenResponse tokens)
    {
        var returnUrl = IsValidReturnUrl(ReturnUrl) ? ReturnUrl : "";
        var fragment = $"token={Uri.EscapeDataString(tokens.AccessToken)}" +
                       $"&refresh={Uri.EscapeDataString(tokens.RefreshToken)}";
        if (!string.IsNullOrEmpty(returnUrl))
        {
            fragment += $"&returnUrl={Uri.EscapeDataString(returnUrl!)}";
        }
        return Redirect($"/app/#{fragment}");
    }

    private static bool IsValidReturnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        return url.StartsWith('/') && !url.StartsWith("//");
    }
}
