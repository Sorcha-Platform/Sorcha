// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Detects and logs potential bad actor behavior for future analysis.
/// Maintains in-memory incident records with automatic cleanup.
/// </summary>
public class BadActorDetector : IBadActorDetector
{
    private readonly BadActorDetectorConfiguration _config;
    private readonly ILogger<BadActorDetector> _logger;

    // Key: RegisterId:ValidatorId, Value: List of incidents
    private readonly ConcurrentDictionary<string, ConcurrentQueue<BadActorIncident>> _incidents = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastIncidentTime = new();

    // Counters for metrics
    private long _totalIncidents;
    private readonly ConcurrentDictionary<BadActorIncidentType, long> _incidentsByType = new();

    public BadActorDetector(
        IOptions<BadActorDetectorConfiguration> config,
        ILogger<BadActorDetector> logger)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public void LogDocketRejection(
        string registerId,
        string initiatorId,
        string docketId,
        DocketRejectionReason reason,
        string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(initiatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(docketId);

        var severity = DetermineSeverityForDocketRejection(reason);

        var incident = new BadActorIncident
        {
            IncidentId = Guid.NewGuid().ToString(),
            RegisterId = registerId,
            ValidatorId = initiatorId,
            IncidentType = BadActorIncidentType.InvalidDocketProposed,
            Severity = severity,
            OccurredAt = DateTimeOffset.UtcNow,
            DocketId = docketId,
            Details = $"Rejection reason: {reason}. {details}"
        };

        RecordIncident(incident);

        _logger.LogWarning(
            "Bad actor incident: Docket rejection. Register: {RegisterId}, Validator: {ValidatorId}, " +
            "Docket: {DocketId}, Reason: {Reason}, Severity: {Severity}",
            registerId, initiatorId, docketId, reason, severity);

        if (severity == IncidentSeverity.Critical && _config.EnableCriticalAlerts)
        {
            LogCriticalAlert(incident);
        }
    }

    /// <inheritdoc/>
    public void LogTransactionValidationFailure(
        string registerId,
        string senderId,
        string transactionId,
        string errorType,
        string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        var incident = new BadActorIncident
        {
            IncidentId = Guid.NewGuid().ToString(),
            RegisterId = registerId,
            ValidatorId = senderId,
            IncidentType = BadActorIncidentType.InvalidTransactionSubmitted,
            Severity = IncidentSeverity.Info, // Transaction failures are informational
            OccurredAt = DateTimeOffset.UtcNow,
            TransactionId = transactionId,
            Details = $"Error type: {errorType}. {details}"
        };

        RecordIncident(incident);

        _logger.LogInformation(
            "Bad actor incident: Transaction validation failure. Register: {RegisterId}, " +
            "Sender: {SenderId}, Transaction: {TransactionId}, Error: {ErrorType}",
            registerId, senderId, transactionId, errorType);
    }

    /// <inheritdoc/>
    public void LogDoubleVote(
        string registerId,
        string validatorId,
        string docketId,
        long term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(docketId);

        var incident = new BadActorIncident
        {
            IncidentId = Guid.NewGuid().ToString(),
            RegisterId = registerId,
            ValidatorId = validatorId,
            IncidentType = BadActorIncidentType.DoubleVoteAttempt,
            Severity = IncidentSeverity.High, // Double voting is a serious offense
            OccurredAt = DateTimeOffset.UtcNow,
            DocketId = docketId,
            Details = $"Double vote attempt in term {term}"
        };

        RecordIncident(incident);

        _logger.LogError(
            "CRITICAL Bad actor incident: Double vote attempt! Register: {RegisterId}, " +
            "Validator: {ValidatorId}, Docket: {DocketId}, Term: {Term}",
            registerId, validatorId, docketId, term);

        if (_config.EnableCriticalAlerts)
        {
            LogCriticalAlert(incident);
        }
    }

    /// <inheritdoc/>
    public void LogLeaderImpersonation(
        string registerId,
        string fakeLeaderId,
        string actualLeaderId,
        long term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fakeLeaderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actualLeaderId);

        var incident = new BadActorIncident
        {
            IncidentId = Guid.NewGuid().ToString(),
            RegisterId = registerId,
            ValidatorId = fakeLeaderId,
            IncidentType = BadActorIncidentType.LeaderImpersonation,
            Severity = IncidentSeverity.Critical, // Impersonation is critical
            OccurredAt = DateTimeOffset.UtcNow,
            Details = $"Claimed to be leader in term {term}, but actual leader was {actualLeaderId}"
        };

        RecordIncident(incident);

        _logger.LogError(
            "CRITICAL Bad actor incident: Leader impersonation! Register: {RegisterId}, " +
            "Fake leader: {FakeLeaderId}, Actual leader: {ActualLeaderId}, Term: {Term}",
            registerId, fakeLeaderId, actualLeaderId, term);

        if (_config.EnableCriticalAlerts)
        {
            LogCriticalAlert(incident);
        }
    }

    /// <inheritdoc/>
    public async Task<int> GetRejectionCountAsync(
        string registerId,
        string validatorId,
        TimeSpan timeWindow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorId);

        var key = GetKey(registerId, validatorId);
        var cutoff = DateTimeOffset.UtcNow.Subtract(timeWindow);

        if (!_incidents.TryGetValue(key, out var queue))
        {
            return 0;
        }

        return await Task.FromResult(
            queue.Count(i => i.OccurredAt >= cutoff &&
                           (i.IncidentType == BadActorIncidentType.InvalidDocketProposed ||
                            i.IncidentType == BadActorIncidentType.InvalidTransactionSubmitted)));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<BadActorIncident>> GetIncidentsAsync(
        string registerId,
        string validatorId,
        int limit = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorId);

        var key = GetKey(registerId, validatorId);

        if (!_incidents.TryGetValue(key, out var queue))
        {
            return Task.FromResult<IReadOnlyList<BadActorIncident>>([]);
        }

        var incidents = queue
            .OrderByDescending(i => i.OccurredAt)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<BadActorIncident>>(incidents);
    }

    /// <inheritdoc/>
    public async Task<bool> ShouldFlagForReviewAsync(
        string registerId,
        string validatorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorId);

        var key = GetKey(registerId, validatorId);

        if (!_incidents.TryGetValue(key, out var queue))
        {
            return false;
        }

        var cutoff = DateTimeOffset.UtcNow.Subtract(_config.RejectionCountWindow);
        var recentIncidents = queue.Where(i => i.OccurredAt >= cutoff).ToList();

        // Check for critical incidents
        if (recentIncidents.Any(i => i.Severity == IncidentSeverity.Critical))
        {
            return true;
        }

        // Check for high severity pattern
        var highSeverityCount = recentIncidents.Count(i => i.Severity >= IncidentSeverity.High);
        if (highSeverityCount >= 2)
        {
            return true;
        }

        // Check for rejection threshold
        var rejectionCount = await GetRejectionCountAsync(registerId, validatorId, _config.RejectionCountWindow);
        if (rejectionCount >= _config.WarningThreshold)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get detector statistics
    /// </summary>
    public BadActorDetectorStats GetStats()
    {
        return new BadActorDetectorStats
        {
            TotalIncidents = Interlocked.Read(ref _totalIncidents),
            IncidentsByType = new Dictionary<BadActorIncidentType, long>(_incidentsByType),
            TrackedValidators = _incidents.Count,
            OldestIncident = _incidents.Values
                .SelectMany(q => q)
                .OrderBy(i => i.OccurredAt)
                .FirstOrDefault()?.OccurredAt
        };
    }

    /// <summary>
    /// Cleanup expired incidents
    /// </summary>
    public void CleanupExpiredIncidents()
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(_config.IncidentRetentionPeriod);
        var removed = 0;

        foreach (var kvp in _incidents)
        {
            var queue = kvp.Value;
            var validIncidents = new ConcurrentQueue<BadActorIncident>();

            while (queue.TryDequeue(out var incident))
            {
                if (incident.OccurredAt >= cutoff)
                {
                    validIncidents.Enqueue(incident);
                }
                else
                {
                    removed++;
                }
            }

            // Re-add valid incidents
            while (validIncidents.TryDequeue(out var incident))
            {
                queue.Enqueue(incident);
            }

            // Remove empty entries
            if (queue.IsEmpty)
            {
                _incidents.TryRemove(kvp.Key, out _);
            }
        }

        if (removed > 0)
        {
            _logger.LogInformation("Cleaned up {Count} expired bad actor incidents", removed);
        }
    }


    private void RecordIncident(BadActorIncident incident)
    {
        var key = GetKey(incident.RegisterId, incident.ValidatorId);

        var queue = _incidents.GetOrAdd(key, _ => new ConcurrentQueue<BadActorIncident>());
        queue.Enqueue(incident);

        // Trim if over limit
        while (queue.Count > _config.MaxIncidentsPerValidator)
        {
            queue.TryDequeue(out _);
        }

        _lastIncidentTime[key] = incident.OccurredAt;

        Interlocked.Increment(ref _totalIncidents);
        _incidentsByType.AddOrUpdate(incident.IncidentType, 1, (_, count) => count + 1);

        // Update severity based on pattern
        UpdateSeverityBasedOnPattern(incident.RegisterId, incident.ValidatorId, incident);
    }

    private void UpdateSeverityBasedOnPattern(string registerId, string validatorId, BadActorIncident latestIncident)
    {
        var key = GetKey(registerId, validatorId);

        if (!_incidents.TryGetValue(key, out var queue))
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.Subtract(_config.RejectionCountWindow);
        var recentCount = queue.Count(i => i.OccurredAt >= cutoff);

        if (recentCount >= _config.CriticalThreshold && latestIncident.Severity < IncidentSeverity.Critical)
        {
            _logger.LogError(
                "Validator {ValidatorId} has reached critical incident threshold ({Count} incidents in {Window})",
                validatorId, recentCount, _config.RejectionCountWindow);

            // Log escalation as new incident
            var escalation = new BadActorIncident
            {
                IncidentId = Guid.NewGuid().ToString(),
                RegisterId = registerId,
                ValidatorId = validatorId,
                IncidentType = BadActorIncidentType.ExcessiveRejections,
                Severity = IncidentSeverity.Critical,
                OccurredAt = DateTimeOffset.UtcNow,
                Details = $"Escalated to critical: {recentCount} incidents in {_config.RejectionCountWindow}"
            };

            queue.Enqueue(escalation);

            if (_config.EnableCriticalAlerts)
            {
                LogCriticalAlert(escalation);
            }
        }
        else if (recentCount >= _config.HighSeverityThreshold && latestIncident.Severity < IncidentSeverity.High)
        {
            _logger.LogWarning(
                "Validator {ValidatorId} has reached high severity threshold ({Count} incidents in {Window})",
                validatorId, recentCount, _config.RejectionCountWindow);
        }
        else if (recentCount >= _config.WarningThreshold && latestIncident.Severity < IncidentSeverity.Warning)
        {
            _logger.LogWarning(
                "Validator {ValidatorId} has reached warning threshold ({Count} incidents in {Window})",
                validatorId, recentCount, _config.RejectionCountWindow);
        }
    }

    private IncidentSeverity DetermineSeverityForDocketRejection(DocketRejectionReason reason)
    {
        return reason switch
        {
            DocketRejectionReason.InvalidInitiatorSignature => IncidentSeverity.High,
            DocketRejectionReason.UnauthorizedInitiator => IncidentSeverity.Critical,
            DocketRejectionReason.InvalidMerkleRoot => IncidentSeverity.High,
            DocketRejectionReason.InvalidDocketHash => IncidentSeverity.High,
            DocketRejectionReason.InvalidTransaction => IncidentSeverity.Warning,
            DocketRejectionReason.ChainValidationFailed => IncidentSeverity.Warning,
            DocketRejectionReason.InvalidTerm => IncidentSeverity.Info,
            DocketRejectionReason.Timeout => IncidentSeverity.Info,
            DocketRejectionReason.InternalError => IncidentSeverity.Info,
            _ => IncidentSeverity.Info
        };
    }

    private void LogCriticalAlert(BadActorIncident incident)
    {
        // In production, this could send alerts to monitoring systems
        _logger.LogCritical(
            "🚨 CRITICAL ALERT: Bad actor detected! " +
            "Type: {Type}, Validator: {ValidatorId}, Register: {RegisterId}, Details: {Details}",
            incident.IncidentType,
            incident.ValidatorId,
            incident.RegisterId,
            incident.Details);
    }

    private static string GetKey(string registerId, string validatorId)
    {
        return $"{registerId}:{validatorId}";
    }

}

/// <summary>
/// Statistics for the bad actor detector
/// </summary>
public record BadActorDetectorStats
{
    /// <summary>Total incidents recorded</summary>
    public long TotalIncidents { get; init; }

    /// <summary>Incidents by type</summary>
    public IReadOnlyDictionary<BadActorIncidentType, long> IncidentsByType { get; init; }
        = new Dictionary<BadActorIncidentType, long>();

    /// <summary>Number of validators being tracked</summary>
    public int TrackedValidators { get; init; }

    /// <summary>Oldest incident timestamp</summary>
    public DateTimeOffset? OldestIncident { get; init; }
}
