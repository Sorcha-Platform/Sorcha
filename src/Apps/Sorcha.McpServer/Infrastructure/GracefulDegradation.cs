// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Sorcha.McpServer.Infrastructure;

/// <summary>
/// Tracks backend service availability for graceful degradation.
/// </summary>
public interface IServiceAvailabilityTracker
{
    /// <summary>
    /// Checks if a service is currently available.
    /// </summary>
    /// <param name="serviceName">The name of the service to check.</param>
    /// <returns>True if the service is available, false otherwise.</returns>
    bool IsServiceAvailable(string serviceName);

    /// <summary>
    /// Records a successful service call.
    /// </summary>
    /// <param name="serviceName">The name of the service.</param>
    void RecordSuccess(string serviceName);

    /// <summary>
    /// Records a failed service call.
    /// </summary>
    /// <param name="serviceName">The name of the service.</param>
    /// <param name="exception">The exception that occurred.</param>
    void RecordFailure(string serviceName, Exception? exception = null);

    /// <summary>
    /// Gets the current availability status of all services.
    /// </summary>
    /// <returns>A dictionary of service names to their availability status.</returns>
    IReadOnlyDictionary<string, ServiceAvailabilityStatus> GetAllServiceStatus();

    /// <summary>
    /// Gets the list of services required by a specific tool.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <returns>List of required service names.</returns>
    IReadOnlyList<string> GetRequiredServices(string toolName);

    /// <summary>
    /// Checks if all services required by a tool are available.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <returns>True if all required services are available.</returns>
    bool AreToolServicesAvailable(string toolName);

    /// <summary>
    /// Gets the names of unavailable services for a tool.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <returns>List of unavailable service names.</returns>
    IReadOnlyList<string> GetUnavailableServicesForTool(string toolName);
}

/// <summary>
/// Status of a service's availability.
/// </summary>
public sealed record ServiceAvailabilityStatus
{
    /// <summary>
    /// Whether the service is currently considered available.
    /// </summary>
    public required bool IsAvailable { get; init; }

    /// <summary>
    /// Number of consecutive failures.
    /// </summary>
    public required int ConsecutiveFailures { get; init; }

    /// <summary>
    /// Time of the last successful call.
    /// </summary>
    public DateTimeOffset? LastSuccessAt { get; init; }

    /// <summary>
    /// Time of the last failure.
    /// </summary>
    public DateTimeOffset? LastFailureAt { get; init; }

    /// <summary>
    /// Error message from the last failure.
    /// </summary>
    public string? LastErrorMessage { get; init; }

    /// <summary>
    /// When the service will be considered for availability again (if in cooldown).
    /// </summary>
    public DateTimeOffset? CooldownEndsAt { get; init; }
}

/// <summary>
/// Tracks backend service availability for graceful degradation.
/// </summary>
public sealed class ServiceAvailabilityTracker : IServiceAvailabilityTracker
{
    private readonly ILogger<ServiceAvailabilityTracker> _logger;
    private readonly ConcurrentDictionary<string, ServiceState> _serviceStates = new();

    /// <summary>
    /// Number of consecutive failures before marking service as unavailable.
    /// </summary>
    private const int FailureThreshold = 3;

    /// <summary>
    /// Duration to wait before retrying a failed service.
    /// </summary>
    private static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(1);

    // Tool to required services mapping
    private static readonly Dictionary<string, string[]> ToolServiceMap = new()
    {
        // Admin tools
        ["sorcha_health_check"] = ["Blueprint", "Register", "Wallet", "Tenant", "Validator", "Peer", "ApiGateway"],
        ["sorcha_log_query"] = ["ApiGateway"],
        ["sorcha_metrics"] = ["ApiGateway"],
        ["sorcha_tenant_list"] = ["Tenant"],
        ["sorcha_tenant_create"] = ["Tenant"],
        ["sorcha_tenant_update"] = ["Tenant"],
        ["sorcha_user_list"] = ["Tenant"],
        ["sorcha_user_manage"] = ["Tenant"],
        ["sorcha_peer_status"] = ["Peer"],
        ["sorcha_validator_status"] = ["Validator"],
        ["sorcha_register_stats"] = ["Register"],
        ["sorcha_audit_query"] = ["Tenant"],
        ["sorcha_token_revoke"] = ["Tenant"],

        // Designer tools
        ["sorcha_blueprint_list"] = ["Blueprint"],
        ["sorcha_blueprint_get"] = ["Blueprint"],
        ["sorcha_blueprint_create"] = ["Blueprint"],
        ["sorcha_blueprint_update"] = ["Blueprint"],
        ["sorcha_blueprint_validate"] = ["Blueprint"],
        ["sorcha_blueprint_simulate"] = ["Blueprint"],
        ["sorcha_disclosure_analysis"] = ["Blueprint"],
        ["sorcha_blueprint_export"] = ["Blueprint"],
        ["sorcha_schema_validate"] = [],       // Local operation, no service dependency
        ["sorcha_schema_generate"] = [],       // Local operation, no service dependency
        ["sorcha_jsonlogic_test"] = [],        // Local operation, no service dependency
        ["sorcha_workflow_instances"] = ["Blueprint"],

        // Participant tools
        ["sorcha_inbox_list"] = ["Blueprint"],
        ["sorcha_action_details"] = ["Blueprint", "Register"],
        ["sorcha_action_submit"] = ["Blueprint", "Register", "Wallet"],
        ["sorcha_action_validate"] = ["Blueprint"],
        ["sorcha_transaction_history"] = ["Register"],
        ["sorcha_workflow_status"] = ["Blueprint", "Register"],
        ["sorcha_disclosed_data"] = ["Register"],
        ["sorcha_wallet_info"] = ["Wallet"],
        ["sorcha_wallet_sign"] = ["Wallet"],
        ["sorcha_register_query"] = ["Register"]
    };

    public ServiceAvailabilityTracker(ILogger<ServiceAvailabilityTracker> logger)
    {
        _logger = logger;

        // Initialize all known services as available
        var allServices = ToolServiceMap.Values.SelectMany(s => s).Distinct();
        foreach (var service in allServices)
        {
            _serviceStates[service] = new ServiceState();
        }
    }

    /// <inheritdoc />
    public bool IsServiceAvailable(string serviceName)
    {
        var state = _serviceStates.GetOrAdd(serviceName, _ => new ServiceState());

        // If in cooldown, check if cooldown has expired
        if (!state.IsAvailable && state.CooldownEndsAt.HasValue)
        {
            if (DateTimeOffset.UtcNow >= state.CooldownEndsAt.Value)
            {
                // Cooldown expired, allow retry
                _logger.LogInformation("Service {ServiceName} cooldown expired, allowing retry", serviceName);
                return true;
            }
            return false;
        }

        return state.IsAvailable;
    }

    /// <inheritdoc />
    public void RecordSuccess(string serviceName)
    {
        var state = _serviceStates.GetOrAdd(serviceName, _ => new ServiceState());

        state.IsAvailable = true;
        state.ConsecutiveFailures = 0;
        state.LastSuccessAt = DateTimeOffset.UtcNow;
        state.CooldownEndsAt = null;

        _logger.LogDebug("Service {ServiceName} call succeeded", serviceName);
    }

    /// <inheritdoc />
    public void RecordFailure(string serviceName, Exception? exception = null)
    {
        var state = _serviceStates.GetOrAdd(serviceName, _ => new ServiceState());

        state.ConsecutiveFailures++;
        state.LastFailureAt = DateTimeOffset.UtcNow;
        state.LastErrorMessage = exception?.Message;

        if (state.ConsecutiveFailures >= FailureThreshold)
        {
            state.IsAvailable = false;
            state.CooldownEndsAt = DateTimeOffset.UtcNow.Add(CooldownDuration);

            _logger.LogWarning(
                "Service {ServiceName} marked unavailable after {FailureCount} consecutive failures. Cooldown until {CooldownEnds}",
                serviceName,
                state.ConsecutiveFailures,
                state.CooldownEndsAt);
        }
        else
        {
            _logger.LogDebug(
                "Service {ServiceName} call failed ({FailureCount}/{Threshold})",
                serviceName,
                state.ConsecutiveFailures,
                FailureThreshold);
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ServiceAvailabilityStatus> GetAllServiceStatus()
    {
        return _serviceStates.ToDictionary(
            kvp => kvp.Key,
            kvp => new ServiceAvailabilityStatus
            {
                IsAvailable = kvp.Value.IsAvailable,
                ConsecutiveFailures = kvp.Value.ConsecutiveFailures,
                LastSuccessAt = kvp.Value.LastSuccessAt,
                LastFailureAt = kvp.Value.LastFailureAt,
                LastErrorMessage = kvp.Value.LastErrorMessage,
                CooldownEndsAt = kvp.Value.CooldownEndsAt
            });
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetRequiredServices(string toolName)
    {
        return ToolServiceMap.TryGetValue(toolName, out var services)
            ? services
            : [];
    }

    /// <inheritdoc />
    public bool AreToolServicesAvailable(string toolName)
    {
        var required = GetRequiredServices(toolName);
        return required.All(IsServiceAvailable);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetUnavailableServicesForTool(string toolName)
    {
        var required = GetRequiredServices(toolName);
        return required.Where(s => !IsServiceAvailable(s)).ToList();
    }

    private class ServiceState
    {
        public bool IsAvailable { get; set; } = true;
        public int ConsecutiveFailures { get; set; }
        public DateTimeOffset? LastSuccessAt { get; set; }
        public DateTimeOffset? LastFailureAt { get; set; }
        public string? LastErrorMessage { get; set; }
        public DateTimeOffset? CooldownEndsAt { get; set; }
    }
}
