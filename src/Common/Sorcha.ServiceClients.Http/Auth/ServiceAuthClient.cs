// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Serialization;
using Sorcha.ServiceClients.Configuration;
using Sorcha.Tenant.Models.Auth;
using Sorcha.WorkloadIdentity;

namespace Sorcha.ServiceClients.Auth;

/// <summary>
/// OAuth2 client_credentials implementation for service-to-service JWT token acquisition
/// </summary>
/// <remarks>
/// Acquires tokens from the Tenant Service POST /api/internal/service-auth/token endpoint — the
/// internal-only client_credentials route (not routed by the public API Gateway, per #1397).
/// <para>
/// F191 (#1420): when <c>ServiceAuth:ClientCertificate</c> is configured, the client runs in
/// CERTIFICATE MODE — token requests go to the Tenant workload mTLS listener
/// (<c>ServiceAuth:MtlsTokenAddress</c>) presenting the workload certificate as the client
/// credential, with the server certificate pinned to the workload trust bundle. No shared secret
/// is transmitted or required. Configured-but-unreadable material throws at construction —
/// certificate mode NEVER silently falls back to the secret path (FR-009). With no certificate
/// configured, the legacy secret path is byte-for-byte unchanged.
/// </para>
/// Tokens are cached in-memory and refreshed when within 5 minutes of expiry.
/// Thread-safe via SemaphoreSlim.
/// </remarks>
public class ServiceAuthClient : IServiceAuthClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServiceAuthClient> _logger;
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly string _scopes;
    private readonly bool _certificateMode;
    private readonly bool _hasNoCredentialsConfigured;
    private readonly HttpClient? _mtlsHttpClient;
    private readonly X509Certificate2? _clientCertificate;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);
    private const string DefaultScopes = "wallets:sign";

    public ServiceAuthClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ServiceAuthClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Deliberately NOT validated here — see RequireClientId(). A host that authorises by
        // forwarding the caller's bearer (the MCP server) resolves this client through
        // AddServiceClients but never acquires a service token, so construction must not demand
        // credentials it will never use.
        _clientId = configuration["ServiceAuth:ClientId"];
        _scopes = configuration["ServiceAuth:Scopes"] ?? DefaultScopes;

        var certificateSource = configuration[WorkloadIdentityConfig.ClientCertificate];
        _certificateMode = !string.IsNullOrWhiteSpace(certificateSource);

        if (_certificateMode)
        {
            // Fail-fast on any broken material: throwing here (startup) is deliberate.
            _clientCertificate = WorkloadCertificateLoader.Load(
                certificateSource!,
                configuration[WorkloadIdentityConfig.ClientCertificatePassword]);

            var bundleSource = configuration[WorkloadIdentityConfig.TrustBundle];
            if (string.IsNullOrWhiteSpace(bundleSource))
            {
                throw new WorkloadCertificateLoadException(
                    $"Certificate mode requires '{WorkloadIdentityConfig.TrustBundle}' (workload CA bundle) to pin the token endpoint's server certificate.");
            }

            var trustBundle = WorkloadTrustBundle.Resolve(bundleSource);
            var mtlsAddress = configuration[WorkloadIdentityConfig.MtlsTokenAddress]
                ?? WorkloadIdentityConfig.DefaultMtlsTokenAddress;

            var handler = new SocketsHttpHandler();
            handler.SslOptions.ClientCertificates = new X509CertificateCollection { _clientCertificate };
            // Explicit selection: auto-selection matches against the server's advertised CA list,
            // which a private workload CA is typically not on — always present OUR certificate.
            handler.SslOptions.LocalCertificateSelectionCallback = (_, _, _, _, _) => _clientCertificate;
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                certificate is X509Certificate2 serverCertificate && trustBundle.Validate(serverCertificate, out _);

            _mtlsHttpClient = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = new Uri(mtlsAddress),
            };

            _clientSecret = configuration["ServiceAuth:ClientSecret"];
            if (!string.IsNullOrEmpty(_clientSecret))
            {
                _logger.LogInformation(
                    "ServiceAuthClient for {ClientId} is in certificate mode; the configured ServiceAuth:ClientSecret is ignored for token acquisition",
                    _clientId);
            }

            _logger.LogInformation(
                "ServiceAuthClient for {ClientId} using workload-certificate credential against {MtlsAddress}",
                _clientId, mtlsAddress);
        }
        else
        {
            // Legacy secret mode. Resolved lazily via RequireClientSecret() at the point a token
            // is actually requested — see RequireClientId() for why the fail-fast moved out of
            // the constructor.
            _clientSecret = configuration["ServiceAuth:ClientSecret"];
        }

        // Whether this host holds ANY service-principal credential material. Computed once, after
        // both branches above have resolved _clientSecret. A host with none of it never
        // authenticates as itself — it forwards the caller's bearer instead (the MCP server) — and
        // ServiceClientAuthHelper skips the token demand for it. A host with SOME of it is
        // configured, so RequireClientId/RequireClientSecret still fail loudly on the missing half.
        _hasNoCredentialsConfigured =
            !_certificateMode
            && string.IsNullOrWhiteSpace(_clientId)
            && string.IsNullOrWhiteSpace(_clientSecret);

        if (_hasNoCredentialsConfigured)
        {
            _logger.LogInformation(
                "ServiceAuthClient has no ServiceAuth credentials configured; this host authorises "
                + "by forwarding the caller's bearer and will not acquire a service token.");
        }

        // Set base address for Tenant Service (JWT issuer)
        if (_httpClient.BaseAddress is null)
        {
            var tenantAddress = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Tenant)
                ?? "http://tenant-service";
            _httpClient.BaseAddress = new Uri(tenantAddress);
            _logger.LogInformation("ServiceAuthClient targeting Tenant Service at {Address}", tenantAddress);
        }
    }

    /// <inheritdoc />
    public bool HasNoCredentialsConfigured => _hasNoCredentialsConfigured;

    /// <inheritdoc />
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: return cached token if still valid
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry - RefreshBuffer)
        {
            _logger.LogDebug(
                "Service token cache hit for {ClientId}, expires at {Expiry}",
                _clientId, _tokenExpiry);
            return _cachedToken;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry - RefreshBuffer)
            {
                _logger.LogDebug(
                    "Service token cache hit for {ClientId} after lock acquisition",
                    _clientId);
                return _cachedToken;
            }

            return await RefreshTokenAsync(cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Resolves the client id at the point a service token is actually needed. Deliberately not
    /// in the constructor: hosts that authorise by forwarding the caller's bearer (the MCP server)
    /// resolve this client through AddServiceClients but never acquire a service token, and a
    /// throwing constructor made that impossible.
    /// </summary>
    private string RequireClientId() =>
        _clientId
        ?? throw new InvalidOperationException("ServiceAuth:ClientId not configured");

    /// <summary>
    /// Resolves the legacy client secret at the point a service token is actually needed via the
    /// secret path. Never called in certificate mode. Deliberately not in the constructor — see
    /// <see cref="RequireClientId"/> for why.
    /// </summary>
    private string RequireClientSecret() =>
        _clientSecret
        ?? throw new InvalidOperationException("ServiceAuth:ClientSecret not configured");

    private async Task<string?> RefreshTokenAsync(CancellationToken cancellationToken)
    {
        // Resolved before the try block, deliberately: a host that never configured
        // credentials (the MCP server, which forwards the caller's bearer instead) must see
        // this fail loudly, not be swallowed by the generic catch below and returned as null.
        var clientId = RequireClientId();
        var clientSecret = _certificateMode ? null : RequireClientSecret();

        try
        {
            _logger.LogInformation(
                "Service token refresh triggered for {ClientId} with scopes [{Scopes}] via {CredentialMode}",
                clientId, _scopes, _certificateMode ? "workload certificate" : "client secret");

            var formFields = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["scope"] = _scopes
            };
            if (!_certificateMode)
            {
                // Legacy path only — certificate mode never transmits a secret.
                formFields["client_secret"] = clientSecret!;
            }

            using var formData = new FormUrlEncodedContent(formFields);

            var tokenHttpClient = _certificateMode ? _mtlsHttpClient! : _httpClient;
            var response = await tokenHttpClient.PostAsync("/api/internal/service-auth/token", formData, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Service token acquisition failed for {ClientId}: HTTP {StatusCode}",
                    clientId, (int)response.StatusCode);
                _cachedToken = null;
                return null;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(SorchaJson.Options, cancellationToken);
            if (tokenResponse?.AccessToken is null)
            {
                _logger.LogWarning(
                    "Service token response for {ClientId} was null or missing access_token",
                    clientId);
                _cachedToken = null;
                return null;
            }

            _cachedToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            _logger.LogInformation(
                "Service token acquired for {ClientId}, expires at {Expiry} (in {ExpiresInSeconds}s)",
                clientId, _tokenExpiry, tokenResponse.ExpiresIn);

            return _cachedToken;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Service token acquisition failed for {ClientId}: network error communicating with Tenant Service",
                clientId);
            _cachedToken = null;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Service token acquisition failed for {ClientId}: unexpected error",
                clientId);
            _cachedToken = null;
            return null;
        }
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
        _mtlsHttpClient?.Dispose();
        _clientCertificate?.Dispose();
        GC.SuppressFinalize(this);
    }

}
