// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components;
using Sorcha.UI.Core.Utilities;

namespace Sorcha.UI.Core.Services.Navigation;

/// <summary>
/// Implements authentication-aware navigation with return URL support.
/// Provides secure redirect handling to prevent open redirect vulnerabilities.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly NavigationManager _navigationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationService"/> class.
    /// </summary>
    /// <param name="navigationManager">The Blazor navigation manager.</param>
    public NavigationService(NavigationManager navigationManager)
    {
        _navigationManager = navigationManager;
    }

    /// <inheritdoc />
    public string CurrentUri => _navigationManager.Uri;

    /// <inheritdoc />
    public Task RedirectToLoginAsync(string? returnUrl = null)
    {
        var currentPath = new Uri(_navigationManager.Uri).AbsolutePath;

        // Prevent redirect loops - don't redirect if already on login page
        if (currentPath.Contains("/login", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        // Build login URL with optional return URL parameter
        // Uses forceLoad: true to navigate to server-rendered auth pages
        var loginUrl = "/auth/login";
        if (UrlValidator.IsValidReturnUrl(returnUrl, _navigationManager.BaseUri))
        {
            loginUrl = $"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl!)}";
        }

        _navigationManager.NavigateTo(loginUrl, forceLoad: true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void NavigateToValidatedUrl(string? url, string defaultDestination = "dashboard")
    {
        var destination = UrlValidator.IsValidReturnUrl(url, _navigationManager.BaseUri)
            ? url!
            : defaultDestination;

        _navigationManager.NavigateTo(destination, forceLoad: false);
    }
}
