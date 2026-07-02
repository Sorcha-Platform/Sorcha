// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Sorcha.UI.Core.Extensions;
using Sorcha.Wallet.Pwa.Services.Actions.Models;

namespace Sorcha.Wallet.Pwa.Services.Actions;

/// <summary>
/// Default <see cref="IMyActionsClient"/> — reads the Blueprint Service's existing
/// <c>GET /api/actions/pending</c> and <c>GET /api/actions/pending/count</c> through the
/// gateway-routed, bearer-authed PWA <see cref="HttpClient"/>. No backend change: these endpoints
/// already resolve the citizen's wallet(s) from the consumer-tier token. Transient failures return
/// empty so the inbox can retain its last-known list and surface a non-blocking notice.
/// </summary>
public sealed class HttpMyActionsClient : IMyActionsClient
{
    private readonly HttpClient _http;

    /// <summary>Initialises a new instance.</summary>
    public HttpMyActionsClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <inheritdoc />
    /// <remarks>
    /// Transient failures (network / non-success / malformed body) are NOT swallowed — they
    /// propagate so the inbox can retain its last-known list and surface a non-blocking notice
    /// (FR-010), distinct from a genuinely empty inbox.
    /// </remarks>
    public async Task<IReadOnlyList<PendingActionItem>> GetPendingAsync(
        int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _http.GetFromJsonAsync<PendingPage>(
            $"api/actions/pending?page={page}&pageSize={pageSize}", JsonDefaults.Api, ct).ConfigureAwait(false);

        var items = result?.Items;
        if (items is null || items.Count == 0)
        {
            return Array.Empty<PendingActionItem>();
        }

        var mapped = new List<PendingActionItem>(items.Count);
        foreach (var dto in items)
        {
            mapped.Add(Map(dto));
        }
        return mapped;
    }

    /// <inheritdoc />
    /// <remarks>Transient failures propagate; the badge owner retains its last-known count.</remarks>
    public async Task<PendingActionsCount> GetCountAsync(CancellationToken ct = default)
    {
        var dto = await _http.GetFromJsonAsync<CountDto>(
            "api/actions/pending/count", JsonDefaults.Api, ct).ConfigureAwait(false);
        return dto is null ? PendingActionsCount.Empty : new PendingActionsCount(dto.Count, dto.UrgentCount);
    }

    private static PendingActionItem Map(PendingActionDto dto)
    {
        var title = !string.IsNullOrWhiteSpace(dto.ActionTitle)
            ? dto.ActionTitle!
            : !string.IsNullOrWhiteSpace(dto.BlueprintTitle)
                ? dto.BlueprintTitle!
                : $"Action {dto.ActionId}";

        return new PendingActionItem(
            InstanceId: dto.InstanceId ?? string.Empty,
            ActionId: dto.ActionId,
            Title: title,
            WorkflowTitle: dto.BlueprintTitle ?? string.Empty,
            Reference: string.IsNullOrWhiteSpace(dto.InstanceReference) ? null : dto.InstanceReference,
            Summary: string.IsNullOrWhiteSpace(dto.Summary) ? null : dto.Summary,
            Urgency: PendingActionItem.ParseUrgency(dto.Urgency),
            Deadline: dto.Deadline,
            ReceivedAt: dto.ReceivedAt,
            NavigationPath: string.IsNullOrWhiteSpace(dto.NavigationPath) ? null : dto.NavigationPath);
    }

    private sealed class PendingPage
    {
        [JsonPropertyName("items")] public List<PendingActionDto>? Items { get; set; }
        [JsonPropertyName("totalCount")] public int TotalCount { get; set; }
    }

    private sealed class PendingActionDto
    {
        public string? InstanceId { get; set; }
        public int ActionId { get; set; }
        public string? ActionTitle { get; set; }
        public string? BlueprintTitle { get; set; }
        public string? InstanceReference { get; set; }
        public string? Summary { get; set; }
        public string? Urgency { get; set; }
        public DateTimeOffset? Deadline { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }
        public string? NavigationPath { get; set; }
    }

    private sealed class CountDto
    {
        public int Count { get; set; }
        public int UrgentCount { get; set; }
    }
}
