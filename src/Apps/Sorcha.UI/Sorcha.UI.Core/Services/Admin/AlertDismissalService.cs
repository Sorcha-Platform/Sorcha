// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.JSInterop;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Stores dismissed alert IDs in browser localStorage, scoped per user.
/// </summary>
public class AlertDismissalService : IAlertDismissalService
{
    private const string StorageKeyPrefix = "sorcha_dismissed_alerts";
    private readonly IJSRuntime _jsRuntime;
    private HashSet<string>? _cache;

    public AlertDismissalService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task DismissAlertAsync(string alertId)
    {
        var dismissed = await GetDismissedSetAsync();
        dismissed.Add(alertId);
        await SaveDismissedSetAsync(dismissed);
    }

    public async Task<bool> IsAlertDismissedAsync(string alertId)
    {
        var dismissed = await GetDismissedSetAsync();
        return dismissed.Contains(alertId);
    }

    public async Task<IReadOnlyList<ServiceAlert>> FilterDismissedAsync(IReadOnlyList<ServiceAlert> alerts)
    {
        if (alerts.Count == 0)
            return alerts;

        var dismissed = await GetDismissedSetAsync();
        if (dismissed.Count == 0)
            return alerts;

        return alerts.Where(a => !dismissed.Contains(a.Id)).ToList();
    }

    public async Task ClearDismissedAlertsAsync()
    {
        _cache = [];
        await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", StorageKeyPrefix);
    }

    private async Task<HashSet<string>> GetDismissedSetAsync()
    {
        if (_cache != null)
            return _cache;

        try
        {
            var json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKeyPrefix);
            if (!string.IsNullOrEmpty(json))
            {
                var ids = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                _cache = ids != null ? new HashSet<string>(ids) : [];
            }
            else
            {
                _cache = [];
            }
        }
        catch
        {
            _cache = [];
        }

        return _cache;
    }

    private async Task SaveDismissedSetAsync(HashSet<string> dismissed)
    {
        _cache = dismissed;
        var json = System.Text.Json.JsonSerializer.Serialize(dismissed.ToList());
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKeyPrefix, json);
    }
}
