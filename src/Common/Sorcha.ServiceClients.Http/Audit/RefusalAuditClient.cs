// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Configuration;
using Sorcha.ServiceClients.Helpers;

namespace Sorcha.ServiceClients.Audit;

/// <summary>
/// Reports a refusal to the refused caller's organisation audit log (#1648).
/// </summary>
public interface IRefusalAuditClient
{
    /// <summary>
    /// Records <paramref name="report"/> via the Tenant Service. Best-effort: it never throws and
    /// never blocks for long, because the request being refused must be answered regardless.
    /// </summary>
    /// <returns>True when the Tenant Service accepted the entry.</returns>
    Task<bool> RecordAsync(RefusalAuditReport report, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IRefusalAuditClient"/> — POSTs to Tenant's <c>/api/internal/audit/refusals</c>
/// behind a <c>RequireService</c> service-principal token.
/// </summary>
public sealed class RefusalAuditClient : IRefusalAuditClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuth;
    private readonly ILogger<RefusalAuditClient> _logger;

    /// <summary>DI-friendly constructor.</summary>
    public RefusalAuditClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuth,
        IConfiguration configuration,
        ILogger<RefusalAuditClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuth = serviceAuth ?? throw new ArgumentNullException(nameof(serviceAuth));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var serviceAddress = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Tenant)
            ?? "https+http://tenant-service";

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(serviceAddress.TrimEnd('/') + "/");
        }
    }

    /// <summary>
    /// How long a report may take. It runs on a refusal path, so a slow or hung Tenant Service must
    /// never hold the refused request open.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <inheritdoc />
    public async Task<bool> RecordAsync(RefusalAuditReport report, CancellationToken cancellationToken = default)
    {
        if (report is null)
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            await ServiceClientAuthHelper.SetAuthHeaderAsync(
                _httpClient, _serviceAuth, _logger, "Tenant Service (refusal audit)", timeout.Token)
                .ConfigureAwait(false);

            using var response = await _httpClient
                .PostAsJsonAsync("api/internal/audit/refusals", report, JsonOptions, timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Refusal audit for {Action} in org {OrgId} was not recorded: Tenant returned {Status}",
                    report.Action, report.OrganizationId, response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Never throw: the refusal itself must still be returned to the caller.
            _logger.LogWarning(ex,
                "Refusal audit for {Action} in org {OrgId} was not recorded", report.Action, report.OrganizationId);
            return false;
        }
    }
}
