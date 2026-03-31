// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Data;

/// <summary>
/// Singleton signal that DatabaseInitializerHostedService sets when migrations
/// and seeding are complete. Background services that query the database should
/// await <see cref="WaitAsync"/> before their first database access.
/// </summary>
public sealed class DatabaseReadySignal
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Blocks until the database has been initialised, or the token is cancelled.
    /// </summary>
    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        return _tcs.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Signals that the database is ready. Idempotent — subsequent calls are no-ops.
    /// </summary>
    public void Signal() => _tcs.TrySetResult();

    /// <summary>
    /// Whether the database has been signalled as ready.
    /// </summary>
    public bool IsReady => _tcs.Task.IsCompletedSuccessfully;
}
