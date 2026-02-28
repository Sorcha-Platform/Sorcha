// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Implements rotating leader election where validators take turns being leader.
/// Each term, the leader rotates to the next validator in the ordered list.
/// </summary>
public class RotatingLeaderElectionService : ILeaderElectionService, IDisposable
{
    private readonly ValidatorConfiguration _validatorConfig;
    private readonly LeaderElectionConfiguration _electionConfig;
    private readonly IValidatorRegistry _validatorRegistry;
    private readonly IGenesisConfigService _genesisConfigService;
    private readonly ILogger<RotatingLeaderElectionService> _logger;

    // State
    private string? _registerId;
    private string? _currentLeaderId;
    private long _currentTerm;
    private DateTimeOffset? _lastHeartbeatReceived;
    private DateTimeOffset _termStartTime;
    private int _missedHeartbeats;
    private bool _isRunning;
    private bool _isDisposed;

    // Timers
    private Timer? _heartbeatTimer;
    private Timer? _leaderCheckTimer;
    private Timer? _termRotationTimer;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly Random _jitterRandom = new();

    // Cached config
    private LeaderElectionConfig? _cachedConfig;
    private IReadOnlyList<string>? _cachedValidatorOrder;

    /// <inheritdoc/>
    public string? CurrentLeaderId => _currentLeaderId;

    /// <inheritdoc/>
    public bool IsLeader => _currentLeaderId == _validatorConfig.ValidatorId;

    /// <inheritdoc/>
    public long CurrentTerm => _currentTerm;

    /// <inheritdoc/>
    public DateTimeOffset? LastHeartbeatReceived => _lastHeartbeatReceived;

    /// <inheritdoc/>
    public event EventHandler<LeaderChangedEventArgs>? LeaderChanged;

    public RotatingLeaderElectionService(
        IOptions<ValidatorConfiguration> validatorConfig,
        IOptions<LeaderElectionConfiguration> electionConfig,
        IValidatorRegistry validatorRegistry,
        IGenesisConfigService genesisConfigService,
        ILogger<RotatingLeaderElectionService> logger)
    {
        _validatorConfig = validatorConfig?.Value ?? throw new ArgumentNullException(nameof(validatorConfig));
        _electionConfig = electionConfig?.Value ?? throw new ArgumentNullException(nameof(electionConfig));
        _validatorRegistry = validatorRegistry ?? throw new ArgumentNullException(nameof(validatorRegistry));
        _genesisConfigService = genesisConfigService ?? throw new ArgumentNullException(nameof(genesisConfigService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to validator list changes
        _validatorRegistry.ValidatorListChanged += OnValidatorListChanged;
    }

    /// <inheritdoc/>
    public async Task StartAsync(string registerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        await _stateLock.WaitAsync(ct);
        try
        {
            if (_isRunning)
            {
                _logger.LogWarning("Leader election already running for register {RegisterId}", _registerId);
                return;
            }

            _registerId = registerId;
            _isRunning = true;

            _logger.LogInformation(
                "Starting leader election for register {RegisterId}, validator {ValidatorId}",
                registerId, _validatorConfig.ValidatorId);

            // Load configuration
            await RefreshConfigAsync(ct);

            // Initial election after grace period
            _ = Task.Run(async () =>
            {
                await Task.Delay(_electionConfig.StartupGracePeriod, ct);
                await TriggerElectionAsync(ct);
            }, ct);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            if (!_isRunning)
                return;

            _logger.LogInformation(
                "Stopping leader election for register {RegisterId}",
                _registerId);

            _isRunning = false;

            // Stop timers
            _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _leaderCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _termRotationTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            // If we're the leader, step down
            if (IsLeader)
            {
                var previousLeader = _currentLeaderId;
                _currentLeaderId = null;

                RaiseLeaderChanged(previousLeader, null, LeaderChangeReason.LeaderResigned);
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SendHeartbeatAsync(CancellationToken ct = default)
    {
        if (!IsLeader || !_isRunning)
        {
            _logger.LogDebug("Not sending heartbeat - not leader or not running");
            return;
        }

        try
        {
            // In a real implementation, this would send heartbeats to all followers via gRPC
            // For now, we just log the heartbeat
            _logger.LogDebug(
                "Leader {ValidatorId} sending heartbeat for term {Term}",
                _validatorConfig.ValidatorId, _currentTerm);

            // Heartbeat broadcasting to followers requires a dedicated gRPC RPC
            // (e.g., ValidatorService.SendLeaderHeartbeat) which is not yet defined in the proto.
            // For now, followers detect leader liveness via the term rotation timer and
            // missed heartbeat threshold (CheckLeaderHealthAsync). In single-validator deployments
            // this is a no-op. Multi-validator heartbeat broadcasting will be added when the
            // validator-to-validator heartbeat proto is defined.
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending heartbeat");
        }
    }

    /// <inheritdoc/>
    public async Task ProcessHeartbeatAsync(
        string leaderId,
        long term,
        long latestDocketNumber,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaderId);

        await _stateLock.WaitAsync(ct);
        try
        {
            if (!_isRunning)
                return;

            // If we receive a heartbeat with a higher term, accept new leader
            if (term > _currentTerm)
            {
                _logger.LogInformation(
                    "Received heartbeat with higher term {NewTerm} > {CurrentTerm}, accepting leader {LeaderId}",
                    term, _currentTerm, leaderId);

                var previousLeader = _currentLeaderId;
                _currentTerm = term;
                _currentLeaderId = leaderId;
                _lastHeartbeatReceived = DateTimeOffset.UtcNow;
                _missedHeartbeats = 0;

                // Restart term rotation timer
                StartTermRotationTimer();

                if (previousLeader != leaderId)
                {
                    RaiseLeaderChanged(previousLeader, leaderId, LeaderChangeReason.HigherTermReceived);
                }
            }
            else if (term == _currentTerm && leaderId == _currentLeaderId)
            {
                // Normal heartbeat from current leader
                _lastHeartbeatReceived = DateTimeOffset.UtcNow;
                _missedHeartbeats = 0;

                if (_electionConfig.EnableDetailedLogging)
                {
                    _logger.LogDebug(
                        "Received heartbeat from leader {LeaderId}, term {Term}",
                        leaderId, term);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Ignoring heartbeat from {LeaderId} term {Term} - current leader is {CurrentLeader} term {CurrentTerm}",
                    leaderId, term, _currentLeaderId, _currentTerm);
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task TriggerElectionAsync(CancellationToken ct = default)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            if (!_isRunning || string.IsNullOrEmpty(_registerId))
                return;

            _logger.LogInformation(
                "Triggering election for register {RegisterId}, current term {Term}",
                _registerId, _currentTerm);

            // Increment term
            _currentTerm++;
            _termStartTime = DateTimeOffset.UtcNow;

            // Get validator order
            var validatorOrder = await GetValidatorOrderAsync(ct);
            if (validatorOrder.Count == 0)
            {
                _logger.LogWarning("No validators available for election");
                return;
            }

            // Determine new leader based on term number
            var leaderIndex = (int)(_currentTerm % validatorOrder.Count);
            var newLeaderId = validatorOrder[leaderIndex];
            var previousLeader = _currentLeaderId;

            _currentLeaderId = newLeaderId;

            _logger.LogInformation(
                "Election complete: term {Term}, leader {LeaderId} (index {Index}/{Total})",
                _currentTerm, newLeaderId, leaderIndex, validatorOrder.Count);

            // Start appropriate timers
            if (IsLeader)
            {
                _logger.LogInformation(
                    "This validator is the new leader for term {Term}",
                    _currentTerm);

                StartHeartbeatTimer();
                StopLeaderCheckTimer();
            }
            else
            {
                _logger.LogInformation(
                    "Following leader {LeaderId} for term {Term}",
                    newLeaderId, _currentTerm);

                StopHeartbeatTimer();
                StartLeaderCheckTimer();
            }

            // Start term rotation timer
            StartTermRotationTimer();

            // Raise event
            RaiseLeaderChanged(previousLeader, newLeaderId, LeaderChangeReason.InitialElection);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetNextLeaderAsync(string? currentLeaderId)
    {
        var validatorOrder = await GetValidatorOrderAsync();
        if (validatorOrder.Count == 0)
            return null;

        if (string.IsNullOrEmpty(currentLeaderId))
            return validatorOrder[0];

        var currentIndex = validatorOrder.ToList().IndexOf(currentLeaderId);
        if (currentIndex < 0)
            return validatorOrder[0];

        var nextIndex = (currentIndex + 1) % validatorOrder.Count;
        return validatorOrder[nextIndex];
    }


    private void StartHeartbeatTimer()
    {
        var interval = GetHeartbeatInterval();
        var jitter = TimeSpan.FromMilliseconds(_jitterRandom.Next(_electionConfig.HeartbeatJitterMs));

        _heartbeatTimer?.Dispose();
        _heartbeatTimer = new Timer(
            async _ => await SendHeartbeatAsync(),
            null,
            interval + jitter,
            interval);

        _logger.LogDebug("Started heartbeat timer with interval {Interval}", interval);
    }

    private void StopHeartbeatTimer()
    {
        _heartbeatTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void StartLeaderCheckTimer()
    {
        var interval = GetHeartbeatInterval();
        _missedHeartbeats = 0;

        _leaderCheckTimer?.Dispose();
        _leaderCheckTimer = new Timer(
            async _ => await CheckLeaderHealthAsync(),
            null,
            interval,
            interval);

        _logger.LogDebug("Started leader check timer with interval {Interval}", interval);
    }

    private void StopLeaderCheckTimer()
    {
        _leaderCheckTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void StartTermRotationTimer()
    {
        var termDuration = GetTermDuration();
        if (termDuration == null)
        {
            _logger.LogDebug("No term duration configured, term rotation disabled");
            return;
        }

        _termRotationTimer?.Dispose();
        _termRotationTimer = new Timer(
            async _ => await HandleTermExpiredAsync(),
            null,
            termDuration.Value,
            Timeout.InfiniteTimeSpan); // One-shot timer

        _logger.LogDebug("Started term rotation timer with duration {Duration}", termDuration);
    }



    private async Task CheckLeaderHealthAsync()
    {
        if (!_isRunning || IsLeader)
            return;

        await _stateLock.WaitAsync();
        try
        {
            var timeout = GetLeaderTimeout();
            var timeSinceLastHeartbeat = _lastHeartbeatReceived.HasValue
                ? DateTimeOffset.UtcNow - _lastHeartbeatReceived.Value
                : TimeSpan.MaxValue;

            if (timeSinceLastHeartbeat > timeout)
            {
                _missedHeartbeats++;

                if (_missedHeartbeats >= _electionConfig.MissedHeartbeatsThreshold)
                {
                    _logger.LogWarning(
                        "Leader {LeaderId} timeout after {MissedHeartbeats} missed heartbeats, triggering election",
                        _currentLeaderId, _missedHeartbeats);

                    var previousLeader = _currentLeaderId;

                    // Release lock before triggering election
                    _stateLock.Release();
                    try
                    {
                        await TriggerElectionAsync();
                    }
                    finally
                    {
                        await _stateLock.WaitAsync();
                    }

                    // Raise event for leader timeout
                    if (previousLeader != _currentLeaderId)
                    {
                        RaiseLeaderChanged(previousLeader, _currentLeaderId, LeaderChangeReason.LeaderTimeout);
                    }
                }
                else
                {
                    _logger.LogDebug(
                        "No heartbeat from leader {LeaderId} ({MissedHeartbeats}/{Threshold})",
                        _currentLeaderId, _missedHeartbeats, _electionConfig.MissedHeartbeatsThreshold);
                }
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task HandleTermExpiredAsync()
    {
        if (!_isRunning)
            return;

        _logger.LogInformation(
            "Term {Term} expired, rotating leader",
            _currentTerm);

        var previousLeader = _currentLeaderId;
        await TriggerElectionAsync();

        if (previousLeader != _currentLeaderId)
        {
            RaiseLeaderChanged(previousLeader, _currentLeaderId, LeaderChangeReason.TermExpired);
        }
    }



    private async void OnValidatorListChanged(object? sender, ValidatorListChangedEventArgs e)
    {
        if (e.RegisterId != _registerId || !_isRunning)
            return;

        _logger.LogInformation(
            "Validator list changed: {ChangeType} for {ValidatorId}, new count: {Count}",
            e.ChangeType, e.ValidatorId, e.NewValidatorCount);

        // Invalidate cached validator order
        _cachedValidatorOrder = null;

        // If our position changed, we may need to re-evaluate leadership
        if (e.ChangeType is ValidatorListChangeType.ValidatorAdded or ValidatorListChangeType.ValidatorRemoved)
        {
            var previousLeader = _currentLeaderId;
            await TriggerElectionAsync();

            if (previousLeader != _currentLeaderId)
            {
                RaiseLeaderChanged(previousLeader, _currentLeaderId, LeaderChangeReason.ValidatorListChanged);
            }
        }
    }

    private void RaiseLeaderChanged(string? previousLeader, string? newLeader, LeaderChangeReason reason)
    {
        var args = new LeaderChangedEventArgs
        {
            PreviousLeaderId = previousLeader,
            NewLeaderId = newLeader,
            Term = _currentTerm,
            Reason = reason
        };

        _logger.LogInformation(
            "Leader changed: {Previous} -> {New}, term {Term}, reason: {Reason}",
            previousLeader ?? "(none)", newLeader ?? "(none)", _currentTerm, reason);

        LeaderChanged?.Invoke(this, args);
    }



    private async Task RefreshConfigAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_registerId))
            return;

        try
        {
            _cachedConfig = await _genesisConfigService.GetLeaderElectionConfigAsync(_registerId, ct);
            _logger.LogDebug(
                "Loaded leader election config: mechanism={Mechanism}, heartbeat={Heartbeat}, timeout={Timeout}",
                _cachedConfig.Mechanism,
                _cachedConfig.HeartbeatInterval,
                _cachedConfig.LeaderTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load leader election config, using defaults");
            _cachedConfig = null;
        }
    }

    private async Task<IReadOnlyList<string>> GetValidatorOrderAsync(CancellationToken ct = default)
    {
        if (_cachedValidatorOrder != null)
            return _cachedValidatorOrder;

        if (string.IsNullOrEmpty(_registerId))
            return [];

        _cachedValidatorOrder = await _validatorRegistry.GetValidatorOrderAsync(_registerId, ct);
        return _cachedValidatorOrder;
    }

    private TimeSpan GetHeartbeatInterval()
    {
        return _cachedConfig?.HeartbeatInterval ?? _electionConfig.DefaultHeartbeatInterval;
    }

    private TimeSpan GetLeaderTimeout()
    {
        return _cachedConfig?.LeaderTimeout ?? _electionConfig.DefaultLeaderTimeout;
    }

    private TimeSpan? GetTermDuration()
    {
        return _cachedConfig?.TermDuration ?? _electionConfig.DefaultTermDuration;
    }

    /// <inheritdoc/>
    public string? GetLeaderForTerm(long term)
    {
        // For rotating leader election, we can calculate who should be leader for a given term
        // based on the validator order list
        if (_cachedValidatorOrder == null || _cachedValidatorOrder.Count == 0)
        {
            // If we don't have the validator order cached, we can only return
            // the current leader if the term matches
            if (term == _currentTerm)
                return _currentLeaderId;

            return null;
        }

        // Term 0 is always the first validator
        var validatorIndex = (int)(term % _cachedValidatorOrder.Count);
        return _cachedValidatorOrder[validatorIndex];
    }



    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _heartbeatTimer?.Dispose();
        _leaderCheckTimer?.Dispose();
        _termRotationTimer?.Dispose();
        _stateLock.Dispose();

        _validatorRegistry.ValidatorListChanged -= OnValidatorListChanged;

        GC.SuppressFinalize(this);
    }

}
