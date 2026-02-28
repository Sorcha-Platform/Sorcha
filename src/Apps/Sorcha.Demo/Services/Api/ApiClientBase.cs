// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace Sorcha.Demo.Services.Api;

/// <summary>
/// Base class for API clients with common HTTP functionality and logging
/// </summary>
public abstract class ApiClientBase
{
    protected readonly HttpClient HttpClient;
    protected readonly ILogger Logger;
    protected readonly JsonSerializerOptions JsonOptions;

    /// <summary>
    /// JWT Bearer token for authentication
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>
    /// Delegation token for orchestration endpoints
    /// </summary>
    public string? DelegationToken { get; set; }

    protected ApiClientBase(HttpClient httpClient, ILogger logger)
    {
        HttpClient = httpClient;
        Logger = logger;
        JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }

    /// <summary>
    /// Adds authentication headers to request message
    /// </summary>
    protected void AddAuthHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(AuthToken))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AuthToken);
        }

        if (!string.IsNullOrEmpty(DelegationToken))
        {
            request.Headers.Add("X-Delegation-Token", DelegationToken);
        }
    }

    protected async Task<T?> GetAsync<T>(string url, CancellationToken ct = default)
    {
        try
        {
            Logger.LogInformation("GET {Url}", url);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeaders(request);
            var response = await HttpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
            Logger.LogDebug("GET {Url} -> Success", url);
            return result;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "GET {Url} failed", url);
            throw;
        }
    }

    protected async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string url,
        TRequest requestBody,
        CancellationToken ct = default)
    {
        try
        {
            Logger.LogInformation("POST {Url}", url);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            AddAuthHeaders(request);
            request.Content = JsonContent.Create(requestBody, options: JsonOptions);
            var response = await HttpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                Logger.LogError("POST {Url} failed with {StatusCode}: {ErrorBody}",
                    url, response.StatusCode, errorBody);
                response.EnsureSuccessStatusCode(); // Will throw with proper status code
            }

            var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
            Logger.LogDebug("POST {Url} -> Success", url);
            return result;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "POST {Url} failed", url);
            throw;
        }
    }

    protected async Task<HttpResponseMessage> PostAsync<TRequest>(
        string url,
        TRequest requestBody,
        CancellationToken ct = default)
    {
        Logger.LogInformation("POST {Url}", url);
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        AddAuthHeaders(request);
        request.Content = JsonContent.Create(requestBody, options: JsonOptions);
        return await HttpClient.SendAsync(request, ct);
    }

    protected async Task<TResponse?> PutAsync<TRequest, TResponse>(
        string url,
        TRequest requestBody,
        CancellationToken ct = default)
    {
        try
        {
            Logger.LogInformation("PUT {Url}", url);
            using var request = new HttpRequestMessage(HttpMethod.Put, url);
            AddAuthHeaders(request);
            request.Content = JsonContent.Create(requestBody, options: JsonOptions);
            var response = await HttpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, ct);
            Logger.LogDebug("PUT {Url} -> Success", url);
            return result;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "PUT {Url} failed", url);
            throw;
        }
    }

    protected async Task<HttpResponseMessage> DeleteAsync(string url, CancellationToken ct = default)
    {
        Logger.LogInformation("DELETE {Url}", url);
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        AddAuthHeaders(request);
        return await HttpClient.SendAsync(request, ct);
    }

    protected async Task<bool> CheckHealthAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var response = await HttpClient.GetAsync($"{url}/health", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Health check failed for {url}: {ex.Message}");
            return false;
        }
    }
}
