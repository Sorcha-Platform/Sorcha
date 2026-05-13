// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Navigation;

/// <summary>
/// Provides authentication-aware navigation with return URL support.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Gets the current navigation URI.
    /// </summary>
    string CurrentUri { get; }

    /// <summary>
    /// Redirects to the login page with an optional return URL.
    /// Does nothing if already on the login page to prevent redirect loops.
    /// </summary>
    /// <param name="returnUrl">Optional URL to return to after successful login. Validated for security.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task RedirectToLoginAsync(string? returnUrl = null);

    /// <summary>
    /// Navigates to the specified URL if it passes security validation,
    /// otherwise navigates to the default destination.
    /// </summary>
    /// <param name="url">Target URL to navigate to. Must pass security validation.</param>
    /// <param name="defaultDestination">Fallback destination if URL is invalid. Defaults to "dashboard".</param>
    void NavigateToValidatedUrl(string? url, string defaultDestination = "dashboard");
}
