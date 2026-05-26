// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace Sorcha.McpServer.Infrastructure;

/// <summary>
/// Stamps the caller's bearer token onto every outbound backend request, so backend
/// services (via the API Gateway) authorize the operation as the calling identity rather
/// than anonymously. This is the centralised fix for the "tools call backends without
/// credentials" defect — no tool sets an <c>Authorization</c> header itself.
/// <para>
/// If the request already carries an <c>Authorization</c> header it is left untouched; if
/// the caller has no token the request proceeds unmodified (the backend will reject it).
/// The token is never logged. See spec 139 (MCP Server Foundation).
/// </para>
/// </summary>
public sealed class CallerTokenForwardingHandler : DelegatingHandler
{
    private readonly ICallerContext _caller;
    private readonly ILogger<CallerTokenForwardingHandler> _logger;

    public CallerTokenForwardingHandler(
        ICallerContext caller,
        ILogger<CallerTokenForwardingHandler> logger)
    {
        _caller = caller;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var token = _caller.RawToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                _logger.LogDebug(
                    "Outbound request to {Uri} has no caller token to forward.",
                    request.RequestUri);
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
