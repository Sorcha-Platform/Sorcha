// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Models.Blueprints;
using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// HTTP client implementation for Register API operations.
/// </summary>
public class RegisterService : IRegisterService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RegisterService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public RegisterService(HttpClient httpClient, ILogger<RegisterService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegisterViewModel>> GetRegistersAsync(
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = "/api/registers";
            if (!string.IsNullOrEmpty(tenantId))
            {
                url += $"?tenantId={Uri.EscapeDataString(tenantId)}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch registers: {StatusCode}", response.StatusCode);
                return [];
            }

            var registers = await response.Content.ReadFromJsonAsync<List<Register.Models.Register>>(
                JsonOptions, cancellationToken);

            return registers?.Select(MapToViewModel).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching registers");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<RegisterViewModel?> GetRegisterAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/registers/{Uri.EscapeDataString(registerId)}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }

                _logger.LogWarning("Failed to fetch register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return null;
            }

            var register = await response.Content.ReadFromJsonAsync<Register.Models.Register>(
                JsonOptions, cancellationToken);

            return register != null ? MapToViewModel(register) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching register {RegisterId}", registerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<GovernanceRosterViewModel?> GetGovernanceRosterAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/registers/{Uri.EscapeDataString(registerId)}/governance/roster",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }

                _logger.LogWarning("Failed to fetch governance roster for register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<GovernanceRosterViewModel>(
                JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching governance roster for register {RegisterId}", registerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<InitiateRegisterResponse?> InitiateRegisterAsync(
        CreateRegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Initiating register creation for '{Name}' with {OwnerCount} owner(s)",
                request.Name, request.Owners.Count);

            var response = await _httpClient.PostAsJsonAsync(
                "/api/registers/initiate",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Failed to initiate register creation: {StatusCode} - {Error}",
                    response.StatusCode, error);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<InitiateRegisterResponse>(
                JsonOptions, cancellationToken);

            _logger.LogInformation(
                "Register initiation successful: {RegisterId}, {AttestationCount} attestation(s) to sign",
                result?.RegisterId, result?.AttestationsToSign.Count ?? 0);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating register creation");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<FinalizeRegisterResponse?> FinalizeRegisterAsync(
        FinalizeRegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Finalizing register creation for {RegisterId} with {AttestationCount} signed attestation(s)",
                request.RegisterId, request.SignedAttestations.Count);

            var response = await _httpClient.PostAsJsonAsync(
                "/api/registers/finalize",
                request,
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Failed to finalize register creation: {StatusCode} - {Error}",
                    response.StatusCode, error);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<FinalizeRegisterResponse>(
                JsonOptions, cancellationToken);

            _logger.LogInformation(
                "Register finalized successfully: {RegisterId}, status: {Status}",
                result?.RegisterId, result?.Status);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finalizing register creation");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<RegisterPolicyViewModel?> GetPolicyAsync(string registerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/registers/{Uri.EscapeDataString(registerId)}/policy", ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<RegisterPolicyViewModel>(JsonOptions, ct);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching policy for register {RegisterId}", registerId); return null; }
    }

    /// <inheritdoc />
    public async Task<PolicyHistoryViewModel> GetPolicyHistoryAsync(string registerId, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/registers/{Uri.EscapeDataString(registerId)}/policy/history?page={page}&pageSize={pageSize}", ct);
            if (!response.IsSuccessStatusCode) return new PolicyHistoryViewModel { RegisterId = registerId };
            return await response.Content.ReadFromJsonAsync<PolicyHistoryViewModel>(JsonOptions, ct) ?? new PolicyHistoryViewModel { RegisterId = registerId };
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching policy history for register {RegisterId}", registerId); return new PolicyHistoryViewModel { RegisterId = registerId }; }
    }

    /// <inheritdoc />
    public async Task<PolicyUpdateProposalViewModel?> ProposePolicyUpdateAsync(string registerId, RegisterPolicyFields policy, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"/api/registers/{Uri.EscapeDataString(registerId)}/policy/update", policy, JsonOptions, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<PolicyUpdateProposalViewModel>(JsonOptions, ct);
        }
        catch (Exception ex) { _logger.LogError(ex, "Error proposing policy update for register {RegisterId}", registerId); return null; }
    }

    private static RegisterViewModel MapToViewModel(Register.Models.Register register)
    {
        return new RegisterViewModel
        {
            Id = register.Id,
            Name = register.Name,
            Description = register.Description,
            Height = register.Height,
            Status = register.Status,
            Advertise = register.Advertise,
            IsFullReplica = register.IsFullReplica,
            TenantId = register.TenantId,
            CreatedAt = register.CreatedAt,
            UpdatedAt = register.UpdatedAt
        };
    }
}
