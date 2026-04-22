// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Resolves <c>${...}</c> substitution tokens in a persona payload template.
/// </summary>
public interface IPayloadTokenResolver
{
    /// <summary>
    /// Returns a deep copy of <paramref name="template"/> with all tokens replaced by their
    /// concrete values for this fire. Tokens that are the entire string value preserve the
    /// native JSON type (number, string, literal); embedded tokens perform string interpolation.
    /// </summary>
    JsonObject Resolve(JsonNode template, PersonaFireContext ctx);

    /// <summary>
    /// Walks <paramref name="template"/> and returns a list of token errors (unknown names,
    /// malformed arguments, empty choice lists). Empty result means the template is valid.
    /// </summary>
    IReadOnlyList<string> ValidateTokens(JsonNode template);
}
