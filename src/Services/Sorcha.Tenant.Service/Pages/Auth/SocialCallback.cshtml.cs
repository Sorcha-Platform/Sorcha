// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Sorcha.Tenant.Service.Pages.Auth;

/// <summary>
/// Server-rendered Social OAuth callback page model.
/// TODO: Reimplemented in US2 (T036) — social login flow with PlatformSocialLogin model.
/// Currently a minimal stub that redirects to the login page.
/// </summary>
public class SocialCallbackModel : PageModel
{
    private readonly ILogger<SocialCallbackModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SocialCallbackModel"/> class.
    /// </summary>
    public SocialCallbackModel(ILogger<SocialCallbackModel> logger)
    {
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
    /// TODO: Reimplemented in US2 (T036) — full social login exchange, user provisioning, and JWT issuance.
    /// </summary>
    public Task<IActionResult> OnGetAsync(
        string? provider,
        string? code,
        string? state,
        string? error,
        CancellationToken ct)
    {
        IsProcessing = false;

        _logger.LogWarning(
            "Social callback received for provider {Provider} but social login is not yet reimplemented (US2 T036)",
            provider);

        ErrorMessage = "Social login is temporarily unavailable. Please use another sign-in method.";
        return Task.FromResult<IActionResult>(Page());
    }
}
