// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Pages.Auth;

/// <summary>
/// Server-rendered Social OAuth callback page model.
/// Receives the authorization code and state from the OAuth provider, exchanges the code
/// for user claims, creates or links a public user account, and issues a JWT.
/// On success, redirects to the main application with the token in the URL fragment.
/// On failure, displays an error message with a link back to the sign-up page.
/// </summary>
public class SocialCallbackModel : PageModel
{
    private readonly ISocialLoginService _socialLoginService;
    private readonly IPublicUserService _publicUserService;
    private readonly ITokenService _tokenService;
    private readonly ILogger<SocialCallbackModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SocialCallbackModel"/> class.
    /// </summary>
    public SocialCallbackModel(
        ISocialLoginService socialLoginService,
        IPublicUserService publicUserService,
        ITokenService tokenService,
        ILogger<SocialCallbackModel> logger)
    {
        _socialLoginService = socialLoginService;
        _publicUserService = publicUserService;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// Error message to display when the social login flow fails.
    /// Null when processing is successful.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// True during the initial page render while the OAuth exchange is in progress.
    /// Becomes false after processing completes (with success redirect or error).
    /// </summary>
    public bool IsProcessing { get; set; } = true;

    /// <summary>
    /// Handles GET requests from the OAuth provider redirect.
    /// Receives the authorization code and state parameters, exchanges the code for user
    /// claims, creates or links the public user account, and redirects to the application.
    /// </summary>
    /// <param name="provider">The social provider name (e.g., "Google", "GitHub").</param>
    /// <param name="code">The authorization code from the provider.</param>
    /// <param name="state">The state parameter for CSRF protection and flow correlation.</param>
    /// <param name="error">Optional error parameter from the provider if the user denied access.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Redirect to app on success, or page with error message on failure.</returns>
    public async Task<IActionResult> OnGetAsync(
        string? provider,
        string? code,
        string? state,
        string? error,
        CancellationToken ct)
    {
        IsProcessing = false;

        // Handle provider-side errors (e.g., user denied access)
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogInformation("Social login denied by user or provider error: {Error}", error);
            ErrorMessage = "Sign-in was cancelled or denied. Please try again.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            _logger.LogWarning("Social callback received with missing provider parameter");
            ErrorMessage = "Invalid callback request: missing provider.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            _logger.LogWarning("Social callback received with missing authorization code for provider {Provider}", provider);
            ErrorMessage = "Invalid callback request: missing authorization code.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(state))
        {
            _logger.LogWarning("Social callback received with missing state parameter for provider {Provider}", provider);
            ErrorMessage = "Invalid callback request: missing state parameter.";
            return Page();
        }

        try
        {
            // Exchange the authorization code for user claims via the social provider
            var authResult = await _socialLoginService.ExchangeCodeAsync(provider, code, state, ct);

            if (!authResult.Success)
            {
                _logger.LogWarning(
                    "Social login code exchange failed for provider {Provider}: {Error}",
                    provider, authResult.Error);
                ErrorMessage = "Sign-in failed. The authorization code may have expired. Please try again.";
                return Page();
            }

            if (string.IsNullOrEmpty(authResult.Subject))
            {
                _logger.LogWarning("Social login returned no subject for provider {Provider}", provider);
                ErrorMessage = "Sign-in failed: could not retrieve your identity from the provider.";
                return Page();
            }

            // Create or link the social login to a public user account
            var socialLoginLink = new SocialLoginLink
            {
                ProviderType = authResult.Provider,
                ExternalSubjectId = authResult.Subject,
                LinkedEmail = authResult.Email,
                DisplayName = authResult.DisplayName,
                LastUsedAt = DateTimeOffset.UtcNow
            };

            var userResult = await _publicUserService.CreatePublicUserFromSocialAsync(
                authResult.DisplayName ?? authResult.Email ?? "Social User",
                authResult.Email,
                socialLoginLink,
                ct);

            if (!userResult.Success)
            {
                _logger.LogWarning(
                    "Social login user creation/linking failed for provider {Provider}: {Reason}",
                    provider, userResult.ConflictReason);
                ErrorMessage = "Sign-in failed: could not create or link your account. Please try again.";
                return Page();
            }

            // Issue JWT for the public user
            var tokenResponse = await _tokenService.GeneratePublicUserTokenAsync(userResult.Identity!, ct);

            _logger.LogInformation(
                "Social login completed for provider {Provider}: userId={UserId}, isNewUser={IsNewUser}",
                provider, userResult.Identity!.Id, userResult.IsNewUser);

            // Redirect to the application with tokens in the URL fragment
            var fragment = $"token={Uri.EscapeDataString(tokenResponse.AccessToken)}" +
                           $"&refresh={Uri.EscapeDataString(tokenResponse.RefreshToken)}";
            return Redirect($"/app/#{fragment}");
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Social callback failed: provider '{Provider}' not configured", provider);
            ErrorMessage = $"Sign-in failed: the '{provider}' provider is not configured.";
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during social callback for provider {Provider}", provider);
            ErrorMessage = "An unexpected error occurred. Please try again.";
            return Page();
        }
    }
}
