// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Feedback;

/// <summary>
/// Default <see cref="IInlineFeedback"/> implementation. Backs the host with a
/// thread-safe list plus per-message <see cref="Timer"/> for auto-dismissal.
/// </summary>
/// <remarks>
/// <para>
/// Registered scoped per circuit / WASM instance so concurrent users don't see
/// each other's feedback in Server-rendered hosts, and so disposal is wired to
/// the consuming layout lifetime.
/// </para>
/// <para>
/// All public methods are safe to call from any thread. The <see cref="Changed"/>
/// event fires synchronously on the caller's thread; the consuming host is
/// responsible for marshalling onto the renderer via <c>InvokeAsync</c>.
/// </para>
/// </remarks>
public sealed class InlineFeedback : IInlineFeedback, IDisposable
{
    /// <summary>Default auto-dismiss window in milliseconds.</summary>
    public const int DefaultAutoDismissMs = 4000;

    private readonly object _gate = new();
    private readonly List<InlineFeedbackMessage> _messages = new();
    private readonly Dictionary<Guid, Timer> _timers = new();
    private bool _disposed;

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public IReadOnlyList<InlineFeedbackMessage> CurrentMessages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public Guid ShowSuccess(string message, string? detailHref = null, int? autoDismissMs = null)
        => Add(InlineFeedbackSeverity.Success, message, detailHref, autoDismissMs);

    /// <inheritdoc />
    public Guid ShowError(string message, string? detailHref = null, int? autoDismissMs = null)
        => Add(InlineFeedbackSeverity.Error, message, detailHref, autoDismissMs);

    /// <inheritdoc />
    public Guid ShowInfo(string message, string? detailHref = null, int? autoDismissMs = null)
        => Add(InlineFeedbackSeverity.Info, message, detailHref, autoDismissMs);

    /// <inheritdoc />
    public Guid ShowWarning(string message, string? detailHref = null, int? autoDismissMs = null)
        => Add(InlineFeedbackSeverity.Warning, message, detailHref, autoDismissMs);

    /// <inheritdoc />
    public void Dismiss(Guid messageId)
    {
        bool changed;
        lock (_gate)
        {
            changed = _messages.RemoveAll(m => m.Id == messageId) > 0;
            if (_timers.Remove(messageId, out var timer))
            {
                timer.Dispose();
            }
        }
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        bool changed;
        lock (_gate)
        {
            changed = _messages.Count > 0;
            _messages.Clear();
            foreach (var timer in _timers.Values)
            {
                timer.Dispose();
            }
            _timers.Clear();
        }
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Disposes any in-flight auto-dismiss timers.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }

    private Guid Add(InlineFeedbackSeverity severity, string message, string? detailHref, int? autoDismissMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var dismissMs = autoDismissMs ?? DefaultAutoDismissMs;
        var entry = new InlineFeedbackMessage(
            Id: Guid.NewGuid(),
            Severity: severity,
            Message: message,
            DetailHref: detailHref,
            CreatedAt: DateTimeOffset.UtcNow,
            AutoDismissMs: dismissMs);

        Timer? timer = null;
        lock (_gate)
        {
            _messages.Add(entry);
            if (dismissMs > 0)
            {
                // The timer callback re-enters Dismiss, which acquires _gate;
                // do NOT hold _gate while the timer is constructed because the
                // callback can fire synchronously in some implementations.
                timer = new Timer(
                    callback: static state => ((TimerState)state!).Feedback.Dismiss(((TimerState)state!).Id),
                    state: new TimerState(this, entry.Id),
                    dueTime: dismissMs,
                    period: Timeout.Infinite);
                _timers[entry.Id] = timer;
            }
        }
        Changed?.Invoke();
        return entry.Id;
    }

    private sealed record TimerState(InlineFeedback Feedback, Guid Id);
}
