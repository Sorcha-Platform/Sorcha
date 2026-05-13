// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Sorcha.UI.Core.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Time format display preference.
/// </summary>
public enum TimeFormatPreference
{
    UTC = 0,
    Local = 1
}

/// <summary>
/// Service for managing time display format preferences and formatting dates.
/// </summary>
public interface ITimeFormatService
{
    /// <summary>
    /// The currently active time format preference.
    /// </summary>
    TimeFormatPreference CurrentFormat { get; }

    /// <summary>
    /// Raised when the time format preference changes.
    /// </summary>
    event Action? OnFormatChanged;

    /// <summary>
    /// Loads saved preference from the server.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Sets the time format preference and persists it via the user preferences API.
    /// </summary>
    /// <param name="format">The desired time format preference.</param>
    Task SetFormatAsync(TimeFormatPreference format);

    /// <summary>
    /// Formats a UTC datetime according to the current preference.
    /// </summary>
    /// <param name="utcDateTime">A DateTime in UTC.</param>
    /// <returns>A formatted date/time string.</returns>
    string FormatDateTime(DateTime utcDateTime);

    /// <summary>
    /// Formats a UTC datetime as a relative time string (e.g. "2 hours ago", "yesterday").
    /// </summary>
    /// <param name="utcDateTime">A DateTime in UTC.</param>
    /// <returns>A human-readable relative time string.</returns>
    string FormatRelativeTime(DateTime utcDateTime);
}

/// <summary>
/// Implementation of time format management with user timezone detection.
/// </summary>
public class TimeFormatService : ITimeFormatService
{
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<TimeFormatService> _logger;

    private TimeFormatPreference _currentFormat = TimeFormatPreference.UTC;
    private TimeZoneInfo _userTimeZone = TimeZoneInfo.Utc;

    public TimeFormatPreference CurrentFormat => _currentFormat;

    public event Action? OnFormatChanged;

    public TimeFormatService(
        IUserPreferencesService userPreferencesService,
        IJSRuntime jsRuntime,
        ILogger<TimeFormatService> logger)
    {
        _userPreferencesService = userPreferencesService;
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Detect user timezone via JS interop
            _userTimeZone = await DetectUserTimeZoneAsync();

            // Load saved preference from server
            var prefs = await _userPreferencesService.GetUserPreferencesAsync();
            _currentFormat = ParseTimeFormatPreference(prefs.TimeFormat);

            _logger.LogDebug("Time format initialized: {Format}, timezone: {Tz}",
                _currentFormat, _userTimeZone.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize time format, defaulting to Local");
            _currentFormat = TimeFormatPreference.Local;
        }

        OnFormatChanged?.Invoke();
    }

    public async Task SetFormatAsync(TimeFormatPreference format)
    {
        if (_currentFormat == format)
            return;

        _currentFormat = format;

        try
        {
            await _userPreferencesService.UpdateUserPreferencesAsync(new UpdateUserPreferencesRequest
            {
                TimeFormat = format.ToString()
            });

            _logger.LogDebug("Time format updated to {Format}", format);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist time format preference");
        }

        OnFormatChanged?.Invoke();
    }

    public string FormatDateTime(DateTime utcDateTime)
    {
        var dt = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        return _currentFormat switch
        {
            TimeFormatPreference.UTC => dt.ToString("yyyy-MM-dd HH:mm:ss") + " UTC",
            TimeFormatPreference.Local => FormatAsLocal(dt),
            _ => dt.ToString("yyyy-MM-dd HH:mm:ss") + " UTC"
        };
    }

    public string FormatRelativeTime(DateTime utcDateTime)
    {
        var dt = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        var now = DateTime.UtcNow;
        var diff = now - dt;

        if (diff.TotalSeconds < 0)
            return "just now";

        if (diff.TotalSeconds < 60)
            return "just now";

        if (diff.TotalMinutes < 2)
            return "1 minute ago";

        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes} minutes ago";

        if (diff.TotalHours < 2)
            return "1 hour ago";

        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours} hours ago";

        if (diff.TotalDays < 2)
            return "yesterday";

        if (diff.TotalDays < 7)
            return $"{(int)diff.TotalDays} days ago";

        if (diff.TotalDays < 14)
            return "1 week ago";

        if (diff.TotalDays < 30)
            return $"{(int)(diff.TotalDays / 7)} weeks ago";

        if (diff.TotalDays < 60)
            return "1 month ago";

        if (diff.TotalDays < 365)
            return $"{(int)(diff.TotalDays / 30)} months ago";

        if (diff.TotalDays < 730)
            return "1 year ago";

        return $"{(int)(diff.TotalDays / 365)} years ago";
    }

    private string FormatAsLocal(DateTime utcDateTime)
    {
        try
        {
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, _userTimeZone);
            return localTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert to local time, falling back to UTC");
            return utcDateTime.ToString("yyyy-MM-dd HH:mm:ss") + " UTC";
        }
    }

    private async Task<TimeZoneInfo> DetectUserTimeZoneAsync()
    {
        try
        {
            var ianaId = await _jsRuntime.InvokeAsync<string>(
                "eval",
                "Intl.DateTimeFormat().resolvedOptions().timeZone");

            if (string.IsNullOrWhiteSpace(ianaId))
                return TimeZoneInfo.Utc;

            // Try direct lookup (works on Linux/macOS and .NET 8+)
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
            }
            catch (TimeZoneNotFoundException)
            {
                // On Windows, try converting IANA to Windows timezone ID
                if (TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaId, out var windowsId))
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
                }

                _logger.LogWarning("Could not resolve timezone '{TimezoneId}', defaulting to UTC", ianaId);
                return TimeZoneInfo.Utc;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect user timezone");
            return TimeZoneInfo.Utc;
        }
    }

    private static TimeFormatPreference ParseTimeFormatPreference(string? value) => value?.ToLowerInvariant() switch
    {
        "utc" => TimeFormatPreference.UTC,
        "local" => TimeFormatPreference.Local,
        _ => TimeFormatPreference.Local
    };
}
