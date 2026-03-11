// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Pages.Auth;

/// <summary>
/// Server-rendered Password Reset page model.
/// Operates in two modes: request mode (no token) and reset mode (with token).
/// </summary>
public class ResetPasswordModel : PageModel
{
    private readonly IPasswordResetService _passwordResetService;
    private readonly ILogger<ResetPasswordModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResetPasswordModel"/> class.
    /// </summary>
    public ResetPasswordModel(
        IPasswordResetService passwordResetService,
        ILogger<ResetPasswordModel> logger)
    {
        _passwordResetService = passwordResetService;
        _logger = logger;
    }

    /// <summary>
    /// User email address for requesting a reset link.
    /// </summary>
    [BindProperty]
    [EmailAddress(ErrorMessage = "Invalid email address.")]
    public string Email { get; set; } = "";

    /// <summary>
    /// The reset token from the email link.
    /// </summary>
    [BindProperty]
    public string? Token { get; set; }

    /// <summary>
    /// New password to set.
    /// </summary>
    [BindProperty]
    public string NewPassword { get; set; } = "";

    /// <summary>
    /// Confirmation of the new password.
    /// </summary>
    [BindProperty]
    public string ConfirmPassword { get; set; } = "";

    /// <summary>
    /// Current page mode: "request" or "reset".
    /// </summary>
    public string Mode { get; set; } = "request";

    /// <summary>
    /// General error message.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Field-level validation errors from the password policy service.
    /// </summary>
    public Dictionary<string, string[]>? ValidationErrors { get; set; }

    /// <summary>
    /// Whether the reset request email was sent (always shown to prevent enumeration).
    /// </summary>
    public bool RequestSent { get; set; }

    /// <summary>
    /// Whether the password was successfully reset.
    /// </summary>
    public bool ResetSuccess { get; set; }

    /// <summary>
    /// Handles GET requests — determines mode from presence of token query param.
    /// </summary>
    /// <param name="token">Optional reset token from the email link.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task OnGetAsync(string? token, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            Token = token;
            Mode = "reset";

            var validation = await _passwordResetService.ValidateTokenAsync(token, cancellationToken);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.Error ?? "The reset link is invalid or has expired.";
                _logger.LogWarning("Password reset token validation failed: {Error}", ErrorMessage);
            }
        }
        else
        {
            Mode = "request";
        }
    }

    /// <summary>
    /// Handles POST for the request-mode form — sends a reset email.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Page result.</returns>
    public async Task<IActionResult> OnPostRequestAsync(CancellationToken cancellationToken)
    {
        Mode = "request";

        if (string.IsNullOrWhiteSpace(Email) || !new EmailAddressAttribute().IsValid(Email))
        {
            ErrorMessage = "Please enter a valid email address.";
            return Page();
        }

        try
        {
            var resetBaseUrl = $"{Request.Scheme}://{Request.Host}/auth/reset-password";
            await _passwordResetService.RequestResetAsync(Email, resetBaseUrl, cancellationToken);

            // Always show "check your email" to prevent enumeration
            RequestSent = true;
            _logger.LogInformation("Password reset requested for email: {Email}", Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error requesting password reset for {Email}", Email);
            // Still show the "sent" message to prevent enumeration
            RequestSent = true;
        }

        return Page();
    }

    /// <summary>
    /// Handles POST for the reset-mode form — applies the new password.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Page result.</returns>
    public async Task<IActionResult> OnPostResetAsync(CancellationToken cancellationToken)
    {
        Mode = "reset";

        if (string.IsNullOrWhiteSpace(Token))
        {
            ErrorMessage = "No reset token found. Please use the link from your email.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            ErrorMessage = "Please enter a new password.";
            return Page();
        }

        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "Passwords do not match.";
            return Page();
        }

        try
        {
            var result = await _passwordResetService.ResetPasswordAsync(Token, NewPassword, cancellationToken);

            if (result.Success)
            {
                ResetSuccess = true;
                _logger.LogInformation("Password reset succeeded");
            }
            else
            {
                if (result.ValidationErrors is { Count: > 0 })
                {
                    ValidationErrors = result.ValidationErrors;
                }

                ErrorMessage = result.Error ?? "Password reset failed. Please try again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during password reset");
            ErrorMessage = "An unexpected error occurred. Please try again.";
        }

        return Page();
    }
}
