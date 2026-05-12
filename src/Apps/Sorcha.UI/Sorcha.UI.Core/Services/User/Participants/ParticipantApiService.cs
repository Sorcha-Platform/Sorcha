// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Sorcha.UI.Core.Models.Participants;

namespace Sorcha.UI.Core.Services.Participants;

/// <summary>
/// HTTP client implementation for the Participant API.
/// </summary>
public class ParticipantApiService : IParticipantApiService
{
    private readonly HttpClient _httpClient;

    public ParticipantApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<ParticipantListViewModel> ListParticipantsAsync(
        Guid organizationId,
        int page = 1,
        int pageSize = 20,
        string? status = null,
        CancellationToken ct = default)
    {
        var url = $"/api/organizations/{organizationId}/participants?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrEmpty(status))
        {
            url += $"&status={status}";
        }

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ParticipantListViewModel>(ct);
        return result ?? new ParticipantListViewModel();
    }

    /// <inheritdoc />
    public async Task<ParticipantDetailViewModel?> GetParticipantAsync(
        Guid organizationId,
        Guid participantId,
        CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/organizations/{organizationId}/participants/{participantId}", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ParticipantDetailViewModel>(ct);
    }

    /// <inheritdoc />
    public async Task<ParticipantDetailViewModel> CreateParticipantAsync(
        Guid organizationId,
        CreateParticipantViewModel request,
        CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/participants", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ParticipantDetailViewModel>(ct);
        return result ?? throw new InvalidOperationException("Failed to deserialize participant creation response");
    }

    /// <inheritdoc />
    public async Task<ParticipantDetailViewModel?> UpdateParticipantAsync(
        Guid organizationId,
        Guid participantId,
        UpdateParticipantViewModel request,
        CancellationToken ct = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"/api/organizations/{organizationId}/participants/{participantId}", request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ParticipantDetailViewModel>(ct);
    }

    /// <inheritdoc />
    public async Task<bool> DeactivateParticipantAsync(
        Guid organizationId,
        Guid participantId,
        CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"/api/organizations/{organizationId}/participants/{participantId}", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> SuspendParticipantAsync(
        Guid organizationId,
        Guid participantId,
        CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync(
            $"/api/organizations/{organizationId}/participants/{participantId}/suspend", null, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ReactivateParticipantAsync(
        Guid organizationId,
        Guid participantId,
        CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync(
            $"/api/organizations/{organizationId}/participants/{participantId}/reactivate", null, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <inheritdoc />
    public async Task<ParticipantSearchResultsViewModel> SearchParticipantsAsync(
        ParticipantSearchViewModel request,
        CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/participants/search", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ParticipantSearchResultsViewModel>(ct);
        return result ?? new ParticipantSearchResultsViewModel();
    }

    /// <inheritdoc />
    public async Task<ParticipantListItemViewModel?> GetParticipantByWalletAsync(
        string walletAddress,
        CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(
            $"/api/participants/by-wallet/{Uri.EscapeDataString(walletAddress)}", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ParticipantListItemViewModel>(ct);
    }

    /// <inheritdoc />
    public async Task<List<ParticipantDetailViewModel>> GetMyProfilesAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync("/api/me/participant-profiles", ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<List<ParticipantDetailViewModel>>(ct);
        return result ?? new List<ParticipantDetailViewModel>();
    }

    /// <inheritdoc />
    public async Task<ParticipantDetailViewModel> SelfRegisterAsync(
        Guid organizationId,
        string? displayName = null,
        CancellationToken ct = default)
    {
        var url = $"/api/me/organizations/{organizationId}/self-register";
        if (!string.IsNullOrEmpty(displayName))
        {
            url += $"?displayName={Uri.EscapeDataString(displayName)}";
        }

        var response = await _httpClient.PostAsync(url, null, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ParticipantDetailViewModel>(ct);
        return result ?? throw new InvalidOperationException("Failed to deserialize self-registration response");
    }

    /// <inheritdoc />
    public async Task<WalletLinkChallengeViewModel> InitiateWalletLinkAsync(
        Guid organizationId,
        Guid participantId,
        InitiateWalletLinkViewModel request,
        CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/participants/{participantId}/wallet-links", request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<WalletLinkChallengeViewModel>(ct);
        return result ?? throw new InvalidOperationException("Failed to deserialize wallet link challenge response");
    }

    /// <inheritdoc />
    public async Task<LinkedWalletViewModel> VerifyWalletLinkAsync(
        Guid organizationId,
        Guid participantId,
        Guid challengeId,
        VerifyWalletLinkViewModel request,
        CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/organizations/{organizationId}/participants/{participantId}/wallet-links/{challengeId}/verify",
            request, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LinkedWalletViewModel>(ct);
        return result ?? throw new InvalidOperationException("Failed to deserialize wallet link verification response");
    }

    /// <inheritdoc />
    public async Task<List<LinkedWalletViewModel>> ListWalletLinksAsync(
        Guid organizationId,
        Guid participantId,
        bool includeRevoked = false,
        CancellationToken ct = default)
    {
        var url = $"/api/organizations/{organizationId}/participants/{participantId}/wallet-links";
        if (includeRevoked)
        {
            url += "?includeRevoked=true";
        }

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<List<LinkedWalletViewModel>>(ct);
        return result ?? new List<LinkedWalletViewModel>();
    }

    /// <inheritdoc />
    public async Task<bool> RevokeWalletLinkAsync(
        Guid organizationId,
        Guid participantId,
        Guid linkId,
        CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync(
            $"/api/organizations/{organizationId}/participants/{participantId}/wallet-links/{linkId}", ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }
}
