// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc.RazorPages;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Pages.Auth;

/// <summary>
/// Server-rendered Email Verification page model.
/// Processes the verification token from the link sent to the user's email.
/// </summary>
public class VerifyEmailModel : PageModel
{
    private readonly IEmailVerificationService _emailVerificationService;
    private readonly ILogger<VerifyEmailModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifyEmailModel"/> class.
    /// </summary>
    public VerifyEmailModel(
        IEmailVerificationService emailVerificationService,
        ILogger<VerifyEmailModel> logger)
    {
        _emailVerificationService = emailVerificationService;
        _logger = logger;
    }

    /// <summary>
    /// Whether the email was successfully verified.
    /// </summary>
    public bool IsVerified { get; set; }

    /// <summary>
    /// Error message to display if verification failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Handles GET requests — verifies the token from the query string.
    /// </summary>
    /// <param name="token">The verification token from the email link.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task OnGetAsync(string? token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ErrorMessage = "No verification token was provided.";
            return;
        }

        try
        {
            var (success, error) = await _emailVerificationService.VerifyTokenAsync(token, cancellationToken);

            if (success)
            {
                IsVerified = true;
                _logger.LogInformation("Email verification succeeded for token (first 8 chars): {TokenPrefix}", token[..Math.Min(8, token.Length)]);
            }
            else
            {
                ErrorMessage = error ?? "The verification link is invalid or has expired.";
                _logger.LogWarning("Email verification failed: {Error}", ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during email verification");
            ErrorMessage = "An unexpected error occurred. Please try again or request a new verification email.";
        }
    }
}
