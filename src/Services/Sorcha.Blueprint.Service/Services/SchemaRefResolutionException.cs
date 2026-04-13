// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Thrown by <see cref="SchemaRefResolver"/> when a JSON Schema <c>$ref</c>
/// to a Sorcha core primitive cannot be resolved — the URI is unknown, the
/// reference chain forms a cycle, or the URI scheme isn't yet implemented.
/// </summary>
/// <remarks>
/// The resolver wraps every failure in this exception type so that callers
/// (validation engine, blueprint publish path, form renderer) can distinguish
/// a primitive-resolution problem from any other JSON Schema error and
/// surface a clear remediation message to the blueprint author.
/// </remarks>
public sealed class SchemaRefResolutionException : Exception
{
    /// <summary>The <c>$ref</c> URI that failed to resolve, if known.</summary>
    public string? RefUri { get; }

    /// <summary>Initialises a new instance of the <see cref="SchemaRefResolutionException"/> class.</summary>
    public SchemaRefResolutionException(string message, string? refUri = null)
        : base(message)
    {
        RefUri = refUri;
    }

    /// <summary>Initialises a new instance with an inner exception.</summary>
    public SchemaRefResolutionException(string message, Exception innerException, string? refUri = null)
        : base(message, innerException)
    {
        RefUri = refUri;
    }
}
