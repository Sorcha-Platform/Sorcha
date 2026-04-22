// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Submits a persona-generated starting action to the Blueprint Service.
/// </summary>
public interface IPersonaSubmitter
{
    /// <summary>
    /// Submits a single persona-generated action to the Blueprint Service and classifies
    /// the outcome as <see cref="PersonaSubmissionOutcome.Submitted"/>,
    /// <see cref="PersonaSubmissionOutcome.TransientFailure"/>, or
    /// <see cref="PersonaSubmissionOutcome.HardFailure"/>.
    /// </summary>
    /// <param name="persona">The persona whose target and identity drive the submission.</param>
    /// <param name="resolvedPayload">The payload with all <c>${…}</c> tokens evaluated.</param>
    /// <param name="cancellationToken">Cancellation token; agent shutdown aborts the call.</param>
    Task<PersonaSubmissionResult> SubmitAsync(
        PersonaDefinition persona,
        JsonObject resolvedPayload,
        CancellationToken cancellationToken);
}
