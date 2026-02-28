// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.Participant;

/// <summary>
/// HTTP client for Participant Service operations.
/// </summary>
public class ParticipantServiceClient : IParticipantServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<ParticipantServiceClient> _logger;
    private readonly string _baseAddress;

    public ParticipantServiceClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        ILogger<ParticipantServiceClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _baseAddress = configuration["ServiceClients:TenantService:Address"]
            ?? configuration["Services:TenantService:BaseAddress"]
            ?? "http://tenant-service";

        _httpClient.BaseAddress = new Uri(_baseAddress);

        _logger.LogInformation("ParticipantServiceClient initialized (Address: {Address})", _baseAddress);
    }

    /// <inheritdoc />
    public async Task<ParticipantInfo?> GetByIdAsync(
        Guid participantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting participant {ParticipantId}", participantId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"/api/participants/{participantId}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Participant {ParticipantId} not found", participantId);
                    return null;
                }

                _logger.LogWarning(
                    "Failed to get participant {ParticipantId}: {StatusCode}",
                    participantId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ParticipantInfo>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting participant {ParticipantId}", participantId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ParticipantInfo?> GetByUserAndOrgAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Getting participant for user {UserId} in org {OrganizationId}",
                userId, organizationId);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"/api/organizations/{organizationId}/participants/by-user/{userId}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }

                _logger.LogWarning(
                    "Failed to get participant for user {UserId} in org {OrganizationId}: {StatusCode}",
                    userId, organizationId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ParticipantInfo>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error getting participant for user {UserId} in org {OrganizationId}",
                userId, organizationId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ParticipantInfo?> GetByWalletAddressAsync(
        string walletAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting participant by wallet address {WalletAddress}", walletAddress);

            await SetAuthHeaderAsync(cancellationToken);

            var response = await _httpClient.GetAsync(
                $"/api/participants/by-wallet/{Uri.EscapeDataString(walletAddress)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }

                _logger.LogWarning(
                    "Failed to get participant by wallet {WalletAddress}: {StatusCode}",
                    walletAddress, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ParticipantInfo>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting participant by wallet {WalletAddress}", walletAddress);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<SigningCapabilityResult> ValidateSigningCapabilityAsync(
        Guid participantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating signing capability for participant {ParticipantId}", participantId);

            // First get the participant
            var participant = await GetByIdAsync(participantId, cancellationToken);

            if (participant == null)
            {
                return new SigningCapabilityResult
                {
                    CanSign = false,
                    ParticipantStatus = "NotFound",
                    Error = $"Participant {participantId} not found"
                };
            }

            // Check participant status
            if (participant.Status != "Active")
            {
                return new SigningCapabilityResult
                {
                    CanSign = false,
                    ParticipantStatus = participant.Status,
                    Warnings = [$"Participant status is {participant.Status}, not Active"]
                };
            }

            // Get linked wallets
            var wallets = await GetLinkedWalletsAsync(participantId, activeOnly: true, cancellationToken);
            var warnings = new List<string>();

            if (wallets.Count == 0)
            {
                warnings.Add("Participant has no linked wallet address. Transaction signing will not be possible.");
            }

            return new SigningCapabilityResult
            {
                CanSign = wallets.Count > 0,
                ParticipantStatus = participant.Status,
                ActiveWalletCount = wallets.Count,
                Warnings = warnings
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating signing capability for participant {ParticipantId}", participantId);
            return new SigningCapabilityResult
            {
                CanSign = false,
                ParticipantStatus = "Error",
                Error = ex.Message
            };
        }
    }

    /// <inheritdoc />
    public async Task<List<LinkedWalletInfo>> GetLinkedWalletsAsync(
        Guid participantId,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug(
                "Getting linked wallets for participant {ParticipantId} (activeOnly: {ActiveOnly})",
                participantId, activeOnly);

            await SetAuthHeaderAsync(cancellationToken);

            var includeRevoked = !activeOnly;
            var response = await _httpClient.GetAsync(
                $"/api/participants/{participantId}/wallet-links?includeRevoked={includeRevoked}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to get linked wallets for participant {ParticipantId}: {StatusCode}",
                    participantId, response.StatusCode);
                return [];
            }

            var wallets = await response.Content.ReadFromJsonAsync<List<LinkedWalletInfo>>(cancellationToken);
            return wallets ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting linked wallets for participant {ParticipantId}", participantId);
            return [];
        }
    }

    private Task SetAuthHeaderAsync(CancellationToken cancellationToken) =>
        ServiceClientAuthHelper.SetAuthHeaderAsync(
            _httpClient, _serviceAuth, _logger, "Participant Service", cancellationToken);
}
