// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Pages.Auth;

/// <summary>
/// Server-rendered Signup page model.
/// Handles email/password registration; passkey and social login are handled client-side via JS.
/// </summary>
public class SignupModel : PageModel
{
    private readonly IRegistrationService _registrationService;
    private readonly ILogger<SignupModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignupModel"/> class.
    /// </summary>
    public SignupModel(
        IRegistrationService registrationService,
        ILogger<SignupModel> logger)
    {
        _registrationService = registrationService;
        _logger = logger;
    }

    /// <summary>
    /// User's display name.
    /// </summary>
    [BindProperty]
    [Required(ErrorMessage = "Display name is required.")]
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// User email address.
    /// </summary>
    [BindProperty]
    [Required(ErrorMessage = "Email is required."), EmailAddress(ErrorMessage = "Invalid email address.")]
    public string Email { get; set; } = "";

    /// <summary>
    /// User password.
    /// </summary>
    [BindProperty]
    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; set; } = "";

    /// <summary>
    /// Password confirmation for server-side validation.
    /// </summary>
    [BindProperty]
    [Required(ErrorMessage = "Please confirm your password.")]
    public string ConfirmPassword { get; set; } = "";

    /// <summary>
    /// Organization subdomain for org-specific registration.
    /// </summary>
    [BindProperty]
    public string? OrgSubdomain { get; set; }

    /// <summary>
    /// URL to redirect to after successful authentication.
    /// </summary>
    [BindProperty]
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Currently active tab: "passkey", "social", or "email".
    /// </summary>
    public string ActiveTab { get; set; } = "passkey";

    /// <summary>
    /// Error message to display on the page.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Field-level validation errors from the registration service.
    /// </summary>
    public Dictionary<string, string[]>? ValidationErrors { get; set; }

    /// <summary>
    /// Whether registration succeeded and a confirmation message should be shown.
    /// </summary>
    public bool RegistrationSuccess { get; set; }

    /// <summary>
    /// Handles GET requests — displays the signup form.
    /// </summary>
    /// <param name="returnUrl">Optional return URL to preserve through the auth flow.</param>
    /// <param name="tab">Optional tab to activate (passkey, social, email).</param>
    public void OnGet(string? returnUrl = null, string? tab = null)
    {
        ReturnUrl = returnUrl;
        if (tab is "passkey" or "social" or "email")
        {
            ActiveTab = tab;
        }
    }

    /// <summary>
    /// Handles POST for email/password registration.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Page result or redirect on success.</returns>
    public async Task<IActionResult> OnPostEmailAsync(CancellationToken ct)
    {
        ActiveTab = "email";

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Password != ConfirmPassword)
        {
            ModelState.AddModelError(nameof(ConfirmPassword), "Passwords do not match.");
            return Page();
        }

        try
        {
            var orgSubdomain = string.IsNullOrWhiteSpace(OrgSubdomain) ? "default" : OrgSubdomain;
            var result = await _registrationService.RegisterAsync(
                orgSubdomain, Email, Password, DisplayName, ct);

            if (!result.Success)
            {
                if (result.ValidationErrors is { Count: > 0 })
                {
                    ValidationErrors = result.ValidationErrors;
                    foreach (var (field, errors) in result.ValidationErrors)
                    {
                        foreach (var error in errors)
                        {
                            ModelState.AddModelError(field, error);
                        }
                    }
                }

                ErrorMessage = result.Error ?? "Registration failed.";
                return Page();
            }

            // Registration succeeded — email verification is required
            _logger.LogInformation(
                "User registered successfully: {Email}, UserId: {UserId}",
                Email, result.UserId);

            RegistrationSuccess = true;
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed for {Email}", Email);
            ErrorMessage = "An unexpected error occurred. Please try again.";
            return Page();
        }
    }

    /// <summary>
    /// Validates a return URL to prevent open redirects.
    /// </summary>
    internal static bool IsValidReturnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        return url.StartsWith('/') && !url.StartsWith("//");
    }
}
