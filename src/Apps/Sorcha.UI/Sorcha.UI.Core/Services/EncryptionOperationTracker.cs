// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Models.Encryption;
using Sorcha.UI.Core.Services.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Tracks active encryption operations across page navigations.
/// Scoped service that subscribes to BlueprintHub events and maintains operation state.
/// </summary>
public interface IEncryptionOperationTracker : IDisposable
{
    /// <summary>Active operations keyed by operation ID.</summary>
    IReadOnlyDictionary<string, EncryptionOperationState> ActiveOperations { get; }

    /// <summary>The most recent active operation (shown in popover).</summary>
    EncryptionOperationState? CurrentOperation { get; }

    /// <summary>Number of active (non-complete) operations.</summary>
    int ActiveCount { get; }

    /// <summary>Start tracking a new encryption operation.</summary>
    void TrackOperation(string operationId, List<RecipientDisplayState>? initialRecipients = null);

    /// <summary>Cycle to the next active operation (for multi-operation badge).</summary>
    void CycleToNextOperation();

    /// <summary>Event raised when any tracked operation changes state.</summary>
    event Action? OnStateChanged;
}

/// <summary>
/// Implementation of <see cref="IEncryptionOperationTracker"/>.
/// </summary>
public sealed class EncryptionOperationTracker : IEncryptionOperationTracker
{
    private readonly Dictionary<string, EncryptionOperationState> _operations = new();
    private readonly BlueprintHubConnection _blueprintHub;
    private readonly IOperationStatusService _operationStatus;
    private readonly ILogger<EncryptionOperationTracker> _logger;
    private readonly CancellationTokenSource _cts = new();
    private string? _currentOperationId;
    private bool _disposed;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, EncryptionOperationState> ActiveOperations => _operations;

    /// <inheritdoc />
    public EncryptionOperationState? CurrentOperation =>
        _currentOperationId != null && _operations.TryGetValue(_currentOperationId, out var op) ? op : null;

    /// <inheritdoc />
    public int ActiveCount => _operations.Count(kv => !kv.Value.IsComplete);

    /// <inheritdoc />
    public event Action? OnStateChanged;

    public EncryptionOperationTracker(
        BlueprintHubConnection blueprintHub,
        IOperationStatusService operationStatus,
        ILogger<EncryptionOperationTracker> logger)
    {
        _blueprintHub = blueprintHub;
        _operationStatus = operationStatus;
        _logger = logger;

        _blueprintHub.OnEncryptionProgress += HandleProgress;
        _blueprintHub.OnEncryptionComplete += HandleComplete;
        _blueprintHub.OnEncryptionFailed += HandleFailed;
    }

    /// <inheritdoc />
    public void TrackOperation(string operationId, List<RecipientDisplayState>? initialRecipients = null)
    {
        var state = new EncryptionOperationState
        {
            OperationId = operationId,
            Status = OperationDisplayStatus.InProgress,
            Recipients = initialRecipients ?? []
        };

        _operations[operationId] = state;
        _currentOperationId = operationId;

        _logger.LogDebug("Tracking encryption operation {OperationId}", operationId);
        OnStateChanged?.Invoke();
    }

    /// <inheritdoc />
    public void CycleToNextOperation()
    {
        var activeOps = _operations
            .Where(kv => !kv.Value.IsComplete)
            .Select(kv => kv.Key)
            .ToList();

        if (activeOps.Count <= 1) return;

        var currentIndex = activeOps.IndexOf(_currentOperationId ?? "");
        _currentOperationId = activeOps[(currentIndex + 1) % activeOps.Count];
        OnStateChanged?.Invoke();
    }

    // Feature 118 T087 — SignalR signals carry the operation id only; progress
    // detail (percent, stage) is fetched from /api/operations/{operationId}.
    private async Task HandleProgress(EncryptionSignal signal)
    {
        if (!_operations.TryGetValue(signal.OperationId, out var op)) return;

        op.Status = OperationDisplayStatus.InProgress;
        var detail = await _operationStatus.GetStatusAsync(signal.OperationId, _cts.Token).ConfigureAwait(false);
        if (detail is not null)
        {
            op.PercentComplete = detail.PercentComplete;
        }
        OnStateChanged?.Invoke();
    }

    private async Task HandleComplete(EncryptionSignal signal)
    {
        if (!_operations.TryGetValue(signal.OperationId, out var op)) return;

        op.Status = OperationDisplayStatus.Complete;
        op.PercentComplete = 100;

        // Mark all remaining recipients as secured
        foreach (var r in op.Recipients.Where(r => r.Status == "waiting"))
        {
            r.Status = "secured";
        }

        _logger.LogDebug("Encryption operation {OperationId} complete", signal.OperationId);
        OnStateChanged?.Invoke();

        // Best-effort detail refresh — gives recipient state and final stage.
        _ = _operationStatus.GetStatusAsync(signal.OperationId, _cts.Token);

        // Clean up completed operations after a delay (keep for 5 minutes for toast/review)
        _ = CleanupAfterDelayAsync(signal.OperationId);
    }

    private async Task HandleFailed(EncryptionSignal signal)
    {
        if (!_operations.TryGetValue(signal.OperationId, out var op)) return;

        op.Status = OperationDisplayStatus.Failed;
        // Pull error detail via REST — the wire signal carries no failure reason.
        var detail = await _operationStatus.GetStatusAsync(signal.OperationId, _cts.Token).ConfigureAwait(false);
        op.ErrorMessage = detail?.ErrorMessage;

        _logger.LogDebug("Encryption operation {OperationId} failed", signal.OperationId);
        OnStateChanged?.Invoke();

        _ = CleanupAfterDelayAsync(signal.OperationId);
    }

    private async Task CleanupAfterDelayAsync(string operationId)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), _cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _operations.Remove(operationId);
        if (_currentOperationId == operationId)
        {
            _currentOperationId = _operations.Keys.LastOrDefault();
        }
    }

    /// <summary>
    /// Formats JSON Pointer paths into human-readable text.
    /// ["/*"] → "all fields"; ["/decision", "/siteAddress"] → "decision, site address"
    /// </summary>
    internal static string FormatDisclosedFields(string[] fields)
    {
        if (fields.Length == 0) return "no fields";
        if (fields.Length == 1 && fields[0] == "/*") return "all fields";

        return string.Join(", ", fields.Select(f =>
        {
            var name = f.TrimStart('/');
            // Convert camelCase to space-separated: "siteAddress" → "site address"
            return string.Concat(name.Select((c, i) =>
                i > 0 && char.IsUpper(c) ? $" {char.ToLower(c)}" : c.ToString()));
        }));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();
        _blueprintHub.OnEncryptionProgress -= HandleProgress;
        _blueprintHub.OnEncryptionComplete -= HandleComplete;
        _blueprintHub.OnEncryptionFailed -= HandleFailed;
    }
}
