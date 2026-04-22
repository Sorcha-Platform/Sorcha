// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Agent.Persona;

/// <summary>
/// Long-running persona trigger loop. Implementations fire submissions on their
/// declared trigger schedule and exit cleanly on cancellation or completion.
/// </summary>
public interface IPersonaLoop
{
    /// <summary>
    /// Runs the loop until the trigger completes (e.g. once-trigger fires) or cancellation.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Number of successful submissions observed so far. Used in tests and telemetry.
    /// </summary>
    int CompletedIterations { get; }
}
