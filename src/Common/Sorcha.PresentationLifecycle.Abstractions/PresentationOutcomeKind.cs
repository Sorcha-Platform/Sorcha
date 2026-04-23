// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.PresentationLifecycle.Abstractions;

/// <summary>
/// Resolution kind for a presentation attempt.
/// </summary>
public enum PresentationOutcomeKind
{
    /// <summary>
    /// The consumer's verifier accepted the presented evidence. Workflow proceeds.
    /// </summary>
    Success,

    /// <summary>
    /// The consumer's verifier rejected the presented evidence. The attempt record
    /// is preserved; workflow terminates or reroutes per the blueprint's rejection
    /// configuration.
    /// </summary>
    Decline
}
