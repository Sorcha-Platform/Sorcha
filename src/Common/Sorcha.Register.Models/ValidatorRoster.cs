// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// The collection of authorized validator signing keys for a register,
/// plus threshold parameters for future n-of-m signing enforcement.
/// Stored in the RegisterControlRecord and updated via governance transactions.
/// </summary>
public class ValidatorRoster
{
    /// <summary>
    /// Authorized validator entries. Must contain at least 1 and at most 10 entries.
    /// </summary>
    /// <remarks>
    /// Bounds enforced by <see cref="Validate"/>: min 1, max 10 entries.
    /// DataAnnotations [MinLength]/[MaxLength] are not used on List&lt;T&gt; as they
    /// operate on string/array length, not collection count.
    /// </remarks>
    [Required]
    [JsonPropertyName("validators")]
    public List<ValidatorRosterEntry> Validators { get; set; } = [];

    /// <summary>
    /// Minimum number of valid signatures required per docket.
    /// Defaults to 1 (single-signer mode). Future n-of-m enforcement
    /// will use this field without schema changes.
    /// </summary>
    [Required]
    [Range(1, 10)]
    [JsonPropertyName("requiredSignatures")]
    public int RequiredSignatures { get; set; } = 1;

    /// <summary>
    /// Roster version, incremented on each governance update.
    /// Genesis roster is version 1.
    /// </summary>
    [Required]
    [Range(1, int.MaxValue)]
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets all validators with Active status (authorized to sign new dockets).
    /// </summary>
    [JsonIgnore]
    public IEnumerable<ValidatorRosterEntry> ActiveValidators =>
        Validators.Where(v => v.Status == ValidatorKeyStatus.Active);

    /// <summary>
    /// Gets all validators eligible for docket signature verification
    /// (Active keys for current dockets, Rotated keys for historical dockets).
    /// Revoked keys are excluded from all verification.
    /// </summary>
    [JsonIgnore]
    public IEnumerable<ValidatorRosterEntry> VerifiableValidators =>
        Validators.Where(v => v.Status is ValidatorKeyStatus.Active or ValidatorKeyStatus.Rotated);

    /// <summary>
    /// Validates the roster's internal consistency.
    /// Returns a list of validation errors, empty if valid.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Validators.Count == 0)
            errors.Add("At least one validator is required.");

        if (Validators.Count > 10)
            errors.Add("Maximum 10 validators allowed per register.");

        var activeCount = ActiveValidators.Count();
        if (activeCount == 0 && Validators.Count > 0)
            errors.Add("At least one validator must have Active status.");

        if (RequiredSignatures < 1)
            errors.Add("RequiredSignatures must be at least 1.");

        if (RequiredSignatures > activeCount && activeCount > 0)
            errors.Add($"RequiredSignatures ({RequiredSignatures}) exceeds active validator count ({activeCount}).");

        // Uniqueness is on (ValidatorId, DerivationContext), not ValidatorId alone.
        //
        // Feature 196 (#1591): the roster is a PURPOSE-KEYED registry — that is what
        // ValidatorRosterEntry.DerivationContext is for — so one node legitimately holds several
        // entries: sorcha:docket-signing to seal dockets, and sorcha:blueprint-publish to publish
        // definitions. Keying uniqueness on ValidatorId alone contradicted that and made the second
        // purpose key unrepresentable, which is why blueprint-publication authority had nowhere to
        // live. Two entries for the same node and the SAME purpose are still a genuine duplicate and
        // still refused.
        //
        // This only relaxes the rule: nothing that validated before stops validating.
        var duplicateIds = Validators
            .GroupBy(v => (v.ValidatorId, v.DerivationContext))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.ValidatorId} ({g.Key.DerivationContext})")
            .ToList();

        if (duplicateIds.Count > 0)
            errors.Add($"Duplicate validator IDs: {string.Join(", ", duplicateIds)}");

        return errors;
    }
}
