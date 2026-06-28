// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// HttpClient implementation of <see cref="IInboxApiService"/>. Talks to
/// the API gateway routes <c>/api/me/inbox/*</c>; the gateway proxies to
/// Tenant Service. Phase 5 of Feature 118.
/// </summary>
public sealed class InboxApiService : IInboxApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<InboxApiService> _logger;

    /// <summary>Initialises a new <see cref="InboxApiService"/>.</summary>
    public InboxApiService(HttpClient httpClient, ILogger<InboxApiService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InboxPageDto?> ListAsync(
        int page = 1,
        int pageSize = 20,
        string? category = null,
        bool unreadOnly = false,
        bool includeDismissed = false,
        bool actionableOnly = false,
        CancellationToken ct = default)
    {
        try
        {
            var query = $"?page={page}&pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(category))
            {
                query += $"&category={Uri.EscapeDataString(category)}";
            }
            if (unreadOnly) query += "&unreadOnly=true";
            if (includeDismissed) query += "&includeDismissed=true";
            if (actionableOnly) query += "&actionableOnly=true";

            using var resp = await _httpClient.GetAsync($"/api/me/inbox{query}", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("InboxApiService.ListAsync failed: {StatusCode}", resp.StatusCode);
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<InboxPageDto>(JsonDefaults.Api, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "InboxApiService.ListAsync transport error");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<InboxEntryDto?> GetByIdAsync(Guid entryId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _httpClient.GetAsync($"/api/me/inbox/{entryId:D}", ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("InboxApiService.GetByIdAsync failed: {StatusCode}", resp.StatusCode);
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<InboxEntryDto>(JsonDefaults.Api, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "InboxApiService.GetByIdAsync transport error");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<int> GetUnreadCountAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _httpClient.GetAsync("/api/me/inbox/unread-count", ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("InboxApiService.GetUnreadCountAsync failed: {StatusCode}", resp.StatusCode);
                return 0;
            }
            var body = await resp.Content.ReadFromJsonAsync<UnreadCountShape>(JsonDefaults.Api, ct).ConfigureAwait(false);
            return body?.Unread ?? 0;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "InboxApiService.GetUnreadCountAsync transport error");
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<bool> MarkReadAsync(Guid entryId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _httpClient.PostAsync($"/api/me/inbox/{entryId:D}/read", content: null, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "InboxApiService.MarkReadAsync transport error");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DismissAsync(Guid entryId, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _httpClient.PostAsync($"/api/me/inbox/{entryId:D}/dismiss", content: null, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "InboxApiService.DismissAsync transport error");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<int> MarkAllReadAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _httpClient.PostAsync("/api/me/inbox/mark-all-read", content: null, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return 0;
            }
            var body = await resp.Content.ReadFromJsonAsync<MarkAllReadShape>(JsonDefaults.Api, ct).ConfigureAwait(false);
            return body?.Marked ?? 0;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "InboxApiService.MarkAllReadAsync transport error");
            return 0;
        }
    }

    private sealed record UnreadCountShape(int Unread);
    private sealed record MarkAllReadShape(int Marked);
}
