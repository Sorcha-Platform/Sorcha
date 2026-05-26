// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Shared execution scaffold for the four credential-lifecycle MCP tools (revoke / suspend /
/// reinstate / refresh). They differ only by the Blueprint client call and the operation verb,
/// so the auth gate, argument validation, availability check, timing, and error handling are
/// factored here. Each tool supplies the actual call via <paramref name="invoke"/>.
/// </summary>
internal static class CredentialLifecycleRunner
{
    public static async Task<CredentialLifecycleToolResult> RunAsync(
        string toolName,
        string serviceName,
        string operationVerb,
        string credentialId,
        string issuerWallet,
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ILogger logger,
        Func<CancellationToken, Task<string?>> invoke,
        CancellationToken cancellationToken)
    {
        if (!authService.CanInvokeTool(toolName))
        {
            return new CredentialLifecycleToolResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(credentialId) || string.IsNullOrWhiteSpace(issuerWallet))
        {
            return new CredentialLifecycleToolResult
            {
                Status = "Error",
                Message = "Both credentialId and issuerWallet are required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!availabilityTracker.IsServiceAvailable(serviceName))
        {
            return new CredentialLifecycleToolResult
            {
                Status = "Unavailable",
                Message = "Blueprint service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        logger.LogInformation("Credential {Operation} requested for {CredentialId} by {Issuer}",
            operationVerb, credentialId, issuerWallet);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var body = await invoke(cancellationToken);
            stopwatch.Stop();
            availabilityTracker.RecordSuccess(serviceName);

            if (string.IsNullOrWhiteSpace(body))
            {
                return new CredentialLifecycleToolResult
                {
                    Status = "Error",
                    Message = $"Credential {operationVerb} for '{credentialId}' was not accepted "
                        + "(it may not exist, may be in a state that disallows this operation, or the caller is not the issuer).",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            return new CredentialLifecycleToolResult
            {
                Status = "Success",
                Message = $"Credential '{credentialId}' {operationVerb} accepted.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                ResultJson = body
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            availabilityTracker.RecordFailure(serviceName);
            logger.LogWarning("Credential {Operation} timed out for {CredentialId}", operationVerb, credentialId);
            return new CredentialLifecycleToolResult
            {
                Status = "Timeout",
                Message = "Request to blueprint service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            availabilityTracker.RecordFailure(serviceName, ex);
            logger.LogError(ex, "Failed credential {Operation} for {CredentialId}", operationVerb, credentialId);
            return new CredentialLifecycleToolResult
            {
                Status = "Error",
                Message = $"Failed to {operationVerb} credential: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>
/// Result of a credential-lifecycle operation (revoke / suspend / reinstate / refresh).
/// </summary>
public sealed record CredentialLifecycleToolResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The raw operation-result JSON body on success.</summary>
    public string? ResultJson { get; init; }
}
