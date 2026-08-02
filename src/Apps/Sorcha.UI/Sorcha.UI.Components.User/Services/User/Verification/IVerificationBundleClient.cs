// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

namespace Sorcha.UI.Core.Services.User.Verification;

/// <summary>
/// Issue #1311 — downloads a Feature 079 verification bundle, over a typed client the host wires
/// with <c>AuthenticatedHttpMessageHandler</c>.
///
/// <para><b>Why only this call, and not the whole component.</b>
/// <c>ReceiptProofCard.razor</c> makes two Register Service calls on the ambient
/// <c>@inject HttpClient</c>, and they differ:</para>
/// <list type="bullet">
/// <item><description><c>POST .../receipts/verify</c> is genuinely <c>AllowAnonymous</c> — a verifier
/// with no Sorcha account must be able to check a receipt. Ambient is CORRECT there and it keeps
/// using it.</description></item>
/// <item><description><c>GET .../transactions/{txId}/verification-bundle</c> requires
/// <c>CanReadTransactions</c> (<c>VerificationEndpoints.cs ~:396</c>), so on the ambient client it
/// 401'd — and the component reported it as <i>"Bundle download failed (HTTP 401). The Register
/// Service may be unavailable."</i>, blaming service availability for an auth failure.</description></item>
/// </list>
///
/// <para>Splitting one call onto a typed client rather than converting the component wholesale keeps
/// the anonymous path anonymous. Attaching a bearer to the public verify endpoint would not have
/// been harmless: it is reachable by unauthenticated verifiers by design, and routing it through the
/// auth handler would couple a deliberately-public check to a session.</para>
/// </summary>
public interface IVerificationBundleClient
{
    /// <summary>
    /// The bundle JSON, or <see langword="null"/> when the request failed (the caller renders its own
    /// error). Returns the raw string because the caller hands it straight to a browser download.
    /// </summary>
    Task<string?> DownloadAsync(
        string registerId, string transactionId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class VerificationBundleClient : IVerificationBundleClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<VerificationBundleClient> _logger;

    public VerificationBundleClient(HttpClient httpClient, ILogger<VerificationBundleClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> DownloadAsync(
        string registerId, string transactionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        var url = $"/api/register/registers/{Uri.EscapeDataString(registerId)}"
                  + $"/transactions/{Uri.EscapeDataString(transactionId)}/verification-bundle";

        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Verification-bundle download failed with {Status} for tx {TransactionId}",
                    (int)response.StatusCode, transactionId);
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verification-bundle download threw for tx {TransactionId}", transactionId);
            return null;
        }
    }
}
