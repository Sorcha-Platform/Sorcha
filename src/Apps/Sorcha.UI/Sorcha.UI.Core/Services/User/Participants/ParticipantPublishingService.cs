// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Sorcha.UI.Core.Models.Participants;

namespace Sorcha.UI.Core.Services.Participants;

/// <summary>
/// HTTP implementation for publishing participant records to registers via Tenant Service API.
/// </summary>
public class ParticipantPublishingService : IParticipantPublishingService
{
    private readonly HttpClient _httpClient;

    public ParticipantPublishingService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ParticipantPublishResultViewModel> PublishAsync(
        Guid organizationId,
        PublishParticipantViewModel request,
        CancellationToken ct = default)
    {
        var url = $"/api/organizations/{organizationId}/participants/publish";
        var response = await _httpClient.PostAsJsonAsync(url, request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ParticipantPublishResultViewModel>(ct);
        return result ?? throw new InvalidOperationException("Failed to parse publish response");
    }

    public async Task<ParticipantPublishResultViewModel> UpdatePublishedAsync(
        Guid organizationId,
        Guid participantId,
        PublishParticipantViewModel request,
        CancellationToken ct = default)
    {
        var url = $"/api/organizations/{organizationId}/participants/publish/{participantId}";
        var response = await _httpClient.PutAsJsonAsync(url, request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ParticipantPublishResultViewModel>(ct);
        return result ?? throw new InvalidOperationException("Failed to parse update response");
    }

    public async Task<bool> RevokeAsync(
        Guid organizationId,
        Guid participantId,
        CancellationToken ct = default)
    {
        var url = $"/api/organizations/{organizationId}/participants/publish/{participantId}";
        var response = await _httpClient.DeleteAsync(url, ct);
        return response.IsSuccessStatusCode;
    }
}
