// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Pages.Auth;

/// <summary>
/// Server-rendered Logout page model.
/// Handles sign-out confirmation and refresh token revocation.
/// The refresh token is passed via URL fragment (never query string) to avoid server logs.
/// </summary>
public class LogoutModel : PageModel
{
    private readonly ITokenService _tokenService;
    private readonly ILogger<LogoutModel> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogoutModel"/> class.
    /// </summary>
    public LogoutModel(ITokenService tokenService, ILogger<LogoutModel> logger)
    {
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// The refresh token to revoke, populated from the URL fragment by client-side JS.
    /// </summary>
    [BindProperty]
    public string? RefreshToken { get; set; }

    /// <summary>
    /// Whether the user has been successfully signed out.
    /// </summary>
    public bool IsSignedOut { get; set; }

    /// <summary>
    /// Handles GET requests — displays the sign-out confirmation page.
    /// </summary>
    public void OnGet()
    {
        // Render confirmation view; JS will extract refresh token from fragment
    }

    /// <summary>
    /// Handles POST requests — revokes the refresh token and marks the user as signed out.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Page result showing signed-out state.</returns>
    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(RefreshToken))
        {
            try
            {
                await _tokenService.RevokeTokenAsync(RefreshToken, ct);
                _logger.LogInformation("Refresh token revoked via logout page");
            }
            catch (Exception ex)
            {
                // Log but don't fail the logout — the user should always be able to sign out
                _logger.LogWarning(ex, "Failed to revoke refresh token during logout");
            }
        }

        IsSignedOut = true;
        return Page();
    }
}
