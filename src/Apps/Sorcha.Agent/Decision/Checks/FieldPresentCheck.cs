// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Agent.Decision.Checks;

/// <summary>
/// Generic presence check: <c>Value=true</c> iff the configured JSON-Pointer field is present and
/// non-empty. Used for <c>photoPresent</c> (<c>/portrait/tokenImageBase64</c>) — a recorded signal,
/// not a hard requirement at the Assured Identity stage.
/// </summary>
public sealed class FieldPresentCheck : IExternalCheck
{
    private readonly string _field;

    /// <summary>Creates the check for the field at <paramref name="field"/>.</summary>
    /// <param name="name">Fact key (e.g. <c>photoPresent</c>).</param>
    /// <param name="field">JSON-Pointer to the field whose presence is tested.</param>
    public FieldPresentCheck(string name, string field)
    {
        Name = name;
        _field = field ?? throw new ArgumentNullException(nameof(field));
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
    {
        var present = PayloadPointer.IsPresentAndNonEmpty(payload, _field);
        return Task.FromResult(new ExternalCheckResult(Name, present));
    }
}
