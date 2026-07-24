// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Extensions.Http;
using Polly.Timeout;
using Refit;
using Sorcha.Cli.Models;
using Sorcha.ServiceClients.Invitation;

namespace Sorcha.Cli.Services;

/// <summary>
/// Factory for creating HTTP clients with Polly resilience policies.
/// </summary>
public class HttpClientFactory
{
    private readonly IConfigurationService _configService;

    /// <summary>
    /// Refit settings that read enums serialized as strings (the platform default) in addition to
    /// numbers. Needed for responses that carry enum-typed fields reused from the shared models
    /// packages (e.g. TransactionLifecycleStatus, ProofPosition on Merkle proofs).
    /// </summary>
    private static readonly RefitSettings StringEnumRefitSettings = new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter() }
            })
    };

    /// <summary>
    /// An empty configuration for shared service clients that accept <see cref="IConfiguration"/>
    /// solely to discover a base address. The CLI resolves base addresses from its own profile and
    /// sets <see cref="HttpClient.BaseAddress"/> before construction, so the lookup never fires.
    /// </summary>
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    public HttpClientFactory(IConfigurationService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    /// <summary>
    /// Creates a Tenant Service client for the specified profile.
    /// </summary>
    public async Task<ITenantServiceClient> CreateTenantServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetTenantServiceUrl());
        return RestService.For<ITenantServiceClient>(httpClient);
    }

    /// <summary>
    /// Creates a Register Service client for the specified profile.
    /// </summary>
    public async Task<IRegisterServiceClient> CreateRegisterServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetRegisterServiceUrl());
        return RestService.For<IRegisterServiceClient>(httpClient, StringEnumRefitSettings);
    }

    /// <summary>
    /// Creates a Wallet Service client for the specified profile.
    /// </summary>
    public async Task<IWalletServiceClient> CreateWalletServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetWalletServiceUrl());
        return RestService.For<IWalletServiceClient>(httpClient);
    }

    /// <summary>
    /// Creates a Peer Service client for the specified profile.
    /// </summary>
    public async Task<IPeerServiceClient> CreatePeerServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetPeerServiceUrl());
        return RestService.For<IPeerServiceClient>(httpClient);
    }

    /// <summary>
    /// Creates a Blueprint Service client for the specified profile.
    /// </summary>
    public async Task<IBlueprintServiceClient> CreateBlueprintServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetBlueprintServiceUrl());
        return RestService.For<IBlueprintServiceClient>(httpClient);
    }

    /// <summary>
    /// Creates the instance-repair client (Feature 145 US4). Hits the Blueprint Service directly;
    /// the endpoints are service-tier, so the caller must hold a service-principal token.
    /// </summary>
    public async Task<IInstanceServiceClient> CreateInstanceServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetBlueprintServiceUrl());
        return RestService.For<IInstanceServiceClient>(httpClient);
    }

    /// <summary>
    /// Creates a Participant Service client for the specified profile.
    /// Uses the Tenant Service URL since participant endpoints are on the Tenant Service.
    /// </summary>
    public async Task<IParticipantServiceClient> CreateParticipantServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetTenantServiceUrl());
        return RestService.For<IParticipantServiceClient>(httpClient);
    }

    /// <summary>
    /// Creates a Credential Service client for the specified profile.
    /// Uses the Gateway URL since credential endpoints route through the API Gateway.
    /// </summary>
    public async Task<ICredentialServiceClient> CreateCredentialServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetGatewayUrl());
        return RestService.For<ICredentialServiceClient>(httpClient);
    }

    /// <summary>
    /// Creates a Validator Service client for the specified profile.
    /// </summary>
    public async Task<IValidatorServiceClient> CreateValidatorServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetValidatorServiceUrl());
        return RestService.For<IValidatorServiceClient>(httpClient);
    }

    /// <summary>
    /// Creates an Admin Service client for the specified profile.
    /// Uses the Gateway URL since admin endpoints route through the API Gateway.
    /// </summary>
    public async Task<IAdminServiceClient> CreateAdminServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetGatewayUrl());
        return RestService.For<IAdminServiceClient>(httpClient);
    }

    /// <summary>
    /// Creates a register-invitation client for the specified profile, pre-authorised with
    /// <paramref name="accessToken"/>. Uses the Tenant Service URL since invitation endpoints
    /// live on the Tenant Service.
    /// </summary>
    /// <remarks>
    /// This returns the <b>shared</b> <see cref="IRegisterInvitationServiceClient"/> from
    /// Sorcha.ServiceClients.Http rather than a CLI-local Refit interface. The CLI previously kept
    /// its own copy of the interface and its four DTOs; those copies drifted from the Tenant
    /// Service wire contract (camelCase vs the server's snake_case, <c>expiresInHours</c> vs
    /// <c>expires_in_days</c>, a bare array vs the <c>{invitations, total_count}</c> envelope) and
    /// every <c>sorcha invitation</c> subcommand failed against a live server as a result.
    /// The shared client is the same one the Blazor admin UI uses, so there is exactly one
    /// definition of this wire contract to keep correct.
    /// </remarks>
    public async Task<IRegisterInvitationServiceClient> CreateRegisterInvitationClientAsync(
        string profileName,
        string accessToken)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetTenantServiceUrl());

        // The shared client expects auth already attached to the HttpClient pipeline (the Blazor UI
        // supplies a bearer-attaching DelegatingHandler); the CLI holds the token itself, so it sets
        // the header directly.
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        // BaseAddress is already set above, so the client never consults IConfiguration for it.
        return new RegisterInvitationServiceClient(
            httpClient,
            EmptyConfiguration,
            NullLogger<RegisterInvitationServiceClient>.Instance);
    }

    /// <summary>
    /// Creates an Audit Service client for the specified profile.
    /// Uses the Tenant Service URL since audit endpoints are on the Tenant Service.
    /// </summary>
    public async Task<IAuditServiceClient> CreateAuditServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetTenantServiceUrl());
        return RestService.For<IAuditServiceClient>(httpClient);
    }

    /// <summary>
    /// Creates a Verification Service client for the specified profile.
    /// Uses the Register Service URL since verification endpoints are on the Register Service.
    /// </summary>
    public async Task<IVerificationServiceClient> CreateVerificationServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetRegisterServiceUrl());
        return RestService.For<IVerificationServiceClient>(httpClient);
    }

    /// <summary>
    /// Creates a Platform Service client for the specified profile.
    /// Uses the Gateway URL since platform endpoints route through the API Gateway.
    /// </summary>
    public async Task<IPlatformServiceClient> CreatePlatformServiceClientAsync(string profileName)
    {
        var profile = await _configService.GetProfileAsync(profileName);
        if (profile == null)
        {
            throw new InvalidOperationException($"Profile '{profileName}' does not exist.");
        }

        var httpClient = CreateHttpClient(profile, profile.GetGatewayUrl());
        return RestService.For<IPlatformServiceClient>(httpClient);
    }

    /// <summary>
    /// Creates an HTTP client with resilience policies.
    /// </summary>
    private HttpClient CreateHttpClient(Profile profile, string baseUrl)
    {
        var handler = new HttpClientHandler();

        // Disable SSL verification for dev profiles if specified
        if (!profile.VerifySsl)
        {
            Console.Error.WriteLine("[WARN] SSL certificate verification is disabled for this profile — do not use in production.");
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
        }

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(profile.TimeoutSeconds)
        };

        return httpClient;
    }

    /// <summary>
    /// Gets a retry policy for transient HTTP errors.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError() // 408, 5xx errors
            .Or<TimeoutRejectedException>() // Timeout errors
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    Console.Error.WriteLine($"Retry {retryCount} after {timespan.TotalSeconds}s due to: {outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString()}");
                });
    }

    /// <summary>
    /// Gets a circuit breaker policy.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (outcome, timespan) =>
                {
                    Console.Error.WriteLine($"Circuit breaker opened for {timespan.TotalSeconds}s due to: {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                },
                onReset: () =>
                {
                    Console.Error.WriteLine("Circuit breaker reset");
                });
    }

    /// <summary>
    /// Gets a timeout policy.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetTimeoutPolicy(int timeoutSeconds)
    {
        return Policy.TimeoutAsync<HttpResponseMessage>(
            timeout: TimeSpan.FromSeconds(timeoutSeconds),
            timeoutStrategy: TimeoutStrategy.Optimistic);
    }

    /// <summary>
    /// Creates a complete resilience pipeline combining all policies.
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetResiliencePipeline(int timeoutSeconds)
    {
        // Order: Timeout -> Retry -> Circuit Breaker
        return Policy.WrapAsync(
            GetTimeoutPolicy(timeoutSeconds),
            GetRetryPolicy(),
            GetCircuitBreakerPolicy());
    }
}
