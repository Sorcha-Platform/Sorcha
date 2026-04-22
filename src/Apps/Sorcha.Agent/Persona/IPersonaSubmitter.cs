// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Submits a persona-generated starting action to the Blueprint Service.
/// </summary>
public interface IPersonaSubmitter
{
    Task<PersonaSubmissionResult> SubmitAsync(
        PersonaDefinition persona,
        JsonObject resolvedPayload,
        CancellationToken cancellationToken);
}
