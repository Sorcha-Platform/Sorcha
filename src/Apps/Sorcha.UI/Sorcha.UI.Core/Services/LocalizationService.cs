// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Sorcha.UI.Core.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for managing UI localization and translation.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// The currently active language code (e.g. "en", "fr").
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Raised when the language changes and translations are reloaded.
    /// </summary>
    event Action? OnLanguageChanged;

    /// <summary>
    /// Loads saved language preference from the server, falling back to browser language.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Sets the language, reloads translations, and persists the preference.
    /// </summary>
    /// <param name="languageCode">ISO language code (e.g. "en", "fr").</param>
    Task SetLanguageAsync(string languageCode);

    /// <summary>
    /// Loads default (English) translations eagerly so keys resolve before full initialization.
    /// Call this at app startup before the first render to avoid showing raw translation keys.
    /// </summary>
    Task LoadDefaultTranslationsAsync();

    /// <summary>
    /// Translates a dot-notation key (e.g. "nav.home"). Returns the key itself if not found.
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <returns>The translated string, or the key if not found.</returns>
    string T(string key);

    /// <summary>
    /// Translates a key with format arguments (uses <see cref="string.Format(string, object[])"/>).
    /// </summary>
    /// <param name="key">The translation key.</param>
    /// <param name="args">Format arguments.</param>
    /// <returns>The formatted translated string, or the key if not found.</returns>
    string T(string key, params object[] args);
}

/// <summary>
/// Implementation of localization with JSON translation files and browser language detection.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private readonly HttpClient _httpClient;
    private readonly IUserPreferencesService _userPreferencesService;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<LocalizationService> _logger;

    private Dictionary<string, string> _translations = new();
    private string _currentLanguage = "en";

    public string CurrentLanguage => _currentLanguage;

    public event Action? OnLanguageChanged;

    public LocalizationService(
        HttpClient httpClient,
        IUserPreferencesService userPreferencesService,
        IJSRuntime jsRuntime,
        ILogger<LocalizationService> logger)
    {
        _httpClient = httpClient;
        _userPreferencesService = userPreferencesService;
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task LoadDefaultTranslationsAsync()
    {
        if (_translations.Count == 0)
        {
            await LoadTranslationsAsync("en");
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Load saved preference from server
            var prefs = await _userPreferencesService.GetUserPreferencesAsync();
            var savedLanguage = prefs.Language;

            if (!string.IsNullOrWhiteSpace(savedLanguage))
            {
                _currentLanguage = savedLanguage;
            }
            else
            {
                // Fall back to browser language
                _currentLanguage = await DetectBrowserLanguageAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load language preference, defaulting to 'en'");
            _currentLanguage = "en";
        }

        await LoadTranslationsAsync(_currentLanguage);
        OnLanguageChanged?.Invoke();
    }

    public async Task SetLanguageAsync(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return;

        if (string.Equals(_currentLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
            return;

        _currentLanguage = languageCode;

        await LoadTranslationsAsync(languageCode);

        try
        {
            await _userPreferencesService.UpdateUserPreferencesAsync(new UpdateUserPreferencesRequest
            {
                Language = languageCode
            });

            _logger.LogDebug("Language updated to {Language}", languageCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist language preference");
        }

        OnLanguageChanged?.Invoke();
    }

    public string T(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        return _translations.TryGetValue(key, out var value) ? value : key;
    }

    public string T(string key, params object[] args)
    {
        var template = T(key);

        if (template == key || args.Length == 0)
            return template;

        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            _logger.LogWarning("Invalid format string for key '{Key}': {Template}", key, template);
            return template;
        }
    }

    private async Task LoadTranslationsAsync(string languageCode)
    {
        try
        {
            var url = $"i18n/{languageCode}.json";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Translation file not found for '{Language}', falling back to 'en'", languageCode);

                if (!string.Equals(languageCode, "en", StringComparison.OrdinalIgnoreCase))
                {
                    await LoadTranslationsAsync("en");
                }
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var flatMap = new Dictionary<string, string>();
            FlattenJsonElement(doc.RootElement, string.Empty, flatMap);
            _translations = flatMap;

            _logger.LogDebug("Loaded {Count} translations for '{Language}'", flatMap.Count, languageCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load translations for '{Language}'", languageCode);

            // Keep existing translations if reload fails
            if (_translations.Count == 0)
            {
                _translations = new Dictionary<string, string>();
            }
        }
    }

    private static void FlattenJsonElement(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                    FlattenJsonElement(property.Value, key, result);
                }
                break;

            case JsonValueKind.String:
                result[prefix] = element.GetString() ?? string.Empty;
                break;

            default:
                result[prefix] = element.ToString();
                break;
        }
    }

    private async Task<string> DetectBrowserLanguageAsync()
    {
        try
        {
            var language = await _jsRuntime.InvokeAsync<string>(
                "eval",
                "navigator.language || navigator.userLanguage || 'en'");

            // Extract primary language code (e.g. "en-US" -> "en")
            if (!string.IsNullOrWhiteSpace(language) && language.Contains('-'))
            {
                language = language.Split('-')[0];
            }

            return string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect browser language");
            return "en";
        }
    }
}
