// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Models.Encryption;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Tracks active encryption operations across page navigations.
/// Scoped service that subscribes to ActionsHub events and maintains operation state.
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
    private readonly ActionsHubConnection _actionsHub;
    private readonly ILogger<EncryptionOperationTracker> _logger;
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
        ActionsHubConnection actionsHub,
        ILogger<EncryptionOperationTracker> logger)
    {
        _actionsHub = actionsHub;
        _logger = logger;

        _actionsHub.OnEncryptionProgress += HandleProgress;
        _actionsHub.OnEncryptionComplete += HandleComplete;
        _actionsHub.OnEncryptionFailed += HandleFailed;
        _actionsHub.OnRecipientProgress += HandleRecipientProgress;
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

    private Task HandleProgress(EncryptionProgressUpdate update)
    {
        if (!_operations.TryGetValue(update.OperationId, out var op)) return Task.CompletedTask;

        op.Status = OperationDisplayStatus.InProgress;
        op.PercentComplete = update.PercentComplete;
        OnStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    private Task HandleRecipientProgress(RecipientEncryptionProgressUpdate update)
    {
        if (!_operations.TryGetValue(update.OperationId, out var op)) return Task.CompletedTask;

        // Find or add the recipient
        var existing = op.Recipients.FirstOrDefault(r => r.Name == update.RecipientName);
        if (existing != null)
        {
            existing.Status = update.Status;
        }
        else
        {
            op.Recipients.Add(new RecipientDisplayState
            {
                Name = update.RecipientName,
                FieldsSummary = FormatDisclosedFields(update.DisclosedFieldsSummary),
                Status = update.Status
            });
        }

        // Update progress based on recipient completion
        var secured = op.Recipients.Count(r => r.Status == "secured");
        if (update.TotalRecipients > 0)
        {
            op.PercentComplete = (int)(secured * 100.0 / update.TotalRecipients);
        }

        OnStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    private Task HandleComplete(EncryptionCompleteUpdate update)
    {
        if (!_operations.TryGetValue(update.OperationId, out var op)) return Task.CompletedTask;

        op.Status = OperationDisplayStatus.Complete;
        op.PercentComplete = 100;
        op.TransactionHash = update.TransactionHash;

        // Mark all remaining recipients as secured
        foreach (var r in op.Recipients.Where(r => r.Status is "waiting" or "encrypting"))
        {
            r.Status = "secured";
        }

        _logger.LogDebug("Encryption operation {OperationId} complete: {TxHash}", update.OperationId, update.TransactionHash);
        OnStateChanged?.Invoke();

        // Clean up completed operations after a delay (keep for 5 minutes for toast/review)
        _ = CleanupAfterDelayAsync(update.OperationId);
        return Task.CompletedTask;
    }

    private Task HandleFailed(EncryptionFailedUpdate update)
    {
        if (!_operations.TryGetValue(update.OperationId, out var op)) return Task.CompletedTask;

        op.Status = OperationDisplayStatus.Failed;
        op.ErrorMessage = update.Error;
        op.FailedRecipient = update.FailedRecipient;

        _logger.LogDebug("Encryption operation {OperationId} failed: {Error}", update.OperationId, update.Error);
        OnStateChanged?.Invoke();

        _ = CleanupAfterDelayAsync(update.OperationId);
        return Task.CompletedTask;
    }

    private async Task CleanupAfterDelayAsync(string operationId)
    {
        await Task.Delay(TimeSpan.FromMinutes(5));
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

        _actionsHub.OnEncryptionProgress -= HandleProgress;
        _actionsHub.OnEncryptionComplete -= HandleComplete;
        _actionsHub.OnEncryptionFailed -= HandleFailed;
        _actionsHub.OnRecipientProgress -= HandleRecipientProgress;
    }
}
