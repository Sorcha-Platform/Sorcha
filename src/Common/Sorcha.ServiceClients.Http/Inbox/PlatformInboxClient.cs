// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.Inbox;

/// <summary>
/// Default <see cref="IPlatformInboxClient"/> — POSTs to Tenant Service's
/// <c>/api/internal/inbox</c> behind a <c>RequireService</c> service principal token.
/// </summary>
public sealed class PlatformInboxClient : IPlatformInboxClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<PlatformInboxClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Initialises a new <see cref="PlatformInboxClient"/>.</summary>
    public PlatformInboxClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        ILogger<PlatformInboxClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var serviceAddress = configuration["ServiceClients:TenantService:Address"]
            ?? "https+http://tenant-service";

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(serviceAddress.TrimEnd('/') + "/");
        }
    }

    /// <inheritdoc />
    public async Task<InboxWriteOutcome> WriteAsync(InboxWritePayload payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        await ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "Tenant Service (Inbox)", ct);

        var body = new
        {
            platformUserId = payload.PlatformUserId,
            category = payload.Category,
            severity = payload.Severity,
            correlationKey = payload.CorrelationKey,
            detailHref = payload.DetailHref,
            sourceEventId = payload.SourceEventId,
            occurredAt = payload.OccurredAt,
            title = payload.Title,
            summary = payload.Summary,
            iconKey = payload.IconKey,
        };

        using var resp = await _httpClient.PostAsJsonAsync("api/internal/inbox", body, JsonOptions, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadFromJsonAsync<InboxResponseEnvelope>(JsonOptions, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Tenant inbox endpoint returned empty body");

        return new InboxWriteOutcome(json.Entry?.Id ?? Guid.Empty, json.Idempotent);
    }

    private sealed record InboxResponseEnvelope(InboxEntryShape? Entry, bool Idempotent);

    private sealed record InboxEntryShape(Guid Id);
}
