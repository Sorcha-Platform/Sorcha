// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json.Nodes;

namespace Sorcha.Agent.Decision.Checks;

/// <summary>
/// Asserts the applicant's email-verified signal. Reads a configurable boolean field from the
/// payload (default <c>/emailVerified</c>) and treats JSON <c>true</c> or the string "true" as
/// verified. The verified-email state is stamped onto the submission at application time
/// (anonymous signup + email verification already exists — see spec assumptions).
/// </summary>
public sealed class EmailVerifiedCheck : IExternalCheck
{
    private readonly string _field;
    private readonly string? _emailField;

    /// <summary>Creates the check reading <paramref name="field"/> for the verified signal.</summary>
    /// <param name="name">Fact key (e.g. <c>emailVerified</c>).</param>
    /// <param name="field">JSON-Pointer to the boolean verified signal (default <c>/emailVerified</c>).</param>
    /// <param name="emailField">Optional JSON-Pointer to the email address, surfaced as the detail string.</param>
    public EmailVerifiedCheck(string name, string? field = null, string? emailField = null)
    {
        Name = name;
        _field = string.IsNullOrWhiteSpace(field) ? "/emailVerified" : field;
        _emailField = emailField;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
    {
        var node = PayloadPointer.Resolve(payload, _field);
        var verified = node switch
        {
            JsonValue value when value.TryGetValue(out bool b) => b,
            JsonValue value when value.TryGetValue(out string? s) => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        var detail = _emailField is not null ? PayloadPointer.ResolveString(payload, _emailField) : null;
        return Task.FromResult(new ExternalCheckResult(Name, verified, detail));
    }
}
