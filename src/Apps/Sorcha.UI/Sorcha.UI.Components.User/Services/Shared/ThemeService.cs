// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Sorcha.UI.Core.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Theme preference options.
/// </summary>
public enum ThemePreference
{
    Light = 0,
    Dark = 1,
    System = 2
}

/// <summary>
/// Service for managing the application theme (light/dark/system).
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// The currently active theme preference.
    /// </summary>
    ThemePreference CurrentTheme { get; }

    /// <summary>
    /// Whether the resolved theme is dark mode (accounting for System preference).
    /// </summary>
    bool IsDarkMode { get; }

    /// <summary>
    /// Raised when the theme changes.
    /// </summary>
    event Action? OnThemeChanged;

    /// <summary>
    /// Loads saved preference from the server, falling back to OS detection.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Sets the theme preference and persists it via the user preferences API.
    /// </summary>
    /// <param name="theme">The desired theme preference.</param>
    Task SetThemeAsync(ThemePreference theme);
}

/// <summary>
/// Implementation of theme management with OS dark-mode detection.
/// </summary>
public class ThemeService : IThemeService
{
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<ThemeService> _logger;

    private ThemePreference _currentTheme = ThemePreference.System;
    private bool _osDarkMode;

    public ThemePreference CurrentTheme => _currentTheme;

    public bool IsDarkMode => _currentTheme switch
    {
        ThemePreference.Dark => true,
        ThemePreference.Light => false,
        ThemePreference.System => _osDarkMode,
        _ => false
    };

    public event Action? OnThemeChanged;

    public ThemeService(
        IUserPreferencesService userPreferencesService,
        IJSRuntime jsRuntime,
        ILogger<ThemeService> logger)
    {
        _userPreferencesService = userPreferencesService;
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Detect OS preference via JS interop
            _osDarkMode = await DetectOsDarkModeAsync();

            // Load saved preference from server
            var prefs = await _userPreferencesService.GetUserPreferencesAsync();
            _currentTheme = ParseThemePreference(prefs.Theme);

            _logger.LogDebug("Theme initialized: {Theme}, OS dark mode: {OsDark}", _currentTheme, _osDarkMode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize theme, defaulting to System");
            _currentTheme = ThemePreference.System;
        }

        OnThemeChanged?.Invoke();
    }

    public async Task SetThemeAsync(ThemePreference theme)
    {
        if (_currentTheme == theme)
            return;

        _currentTheme = theme;

        try
        {
            await _userPreferencesService.UpdateUserPreferencesAsync(new UpdateUserPreferencesRequest
            {
                Theme = theme.ToString()
            });

            _logger.LogDebug("Theme updated to {Theme}", theme);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist theme preference");
        }

        OnThemeChanged?.Invoke();
    }

    private async Task<bool> DetectOsDarkModeAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>(
                "eval",
                "window.matchMedia('(prefers-color-scheme: dark)').matches");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect OS dark mode preference");
            return false;
        }
    }

    private static ThemePreference ParseThemePreference(string? value) => value?.ToLowerInvariant() switch
    {
        "light" => ThemePreference.Light,
        "dark" => ThemePreference.Dark,
        "system" => ThemePreference.System,
        _ => ThemePreference.System
    };
}
