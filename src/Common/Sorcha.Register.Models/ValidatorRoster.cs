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
    [Required]
    [MinLength(1, ErrorMessage = "At least one validator is required")]
    [MaxLength(10, ErrorMessage = "Maximum 10 validators allowed per register")]
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

        var duplicateIds = Validators
            .GroupBy(v => v.ValidatorId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateIds.Count > 0)
            errors.Add($"Duplicate validator IDs: {string.Join(", ", duplicateIds)}");

        return errors;
    }
}
