// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using System.Text.RegularExpressions;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Derives EUDI / ISO 18013-5 style <c>age_over_NN</c> boolean claims from a
/// <c>dateOfBirth</c> at credential issue time. Issuing a boolean threshold instead of the
/// birth date is the privacy-preserving pattern the verifier "Age over 18?" preset consumes —
/// the holder proves the threshold without disclosing their date of birth or exact age.
/// </summary>
public static partial class AgeClaimDeriver
{
    [GeneratedRegex(@"^age_over_(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex AgeOverPattern();

    /// <summary>
    /// True when <paramref name="claimName"/> is an <c>age_over_NN</c> claim, yielding the threshold NN.
    /// </summary>
    public static bool AgeOverClaimThreshold(string claimName, out int threshold)
    {
        threshold = 0;
        if (string.IsNullOrEmpty(claimName)) return false;
        var m = AgeOverPattern().Match(claimName);
        if (!m.Success) return false;
        return int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out threshold);
    }

    /// <summary>
    /// Computes whether the holder is at least <paramref name="threshold"/> years old as of
    /// <paramref name="today"/>. Returns <c>false</c> (fail-closed — no claim should be issued)
    /// when the date of birth is null, empty, or not an ISO <c>yyyy-MM-dd</c> date.
    /// </summary>
    public static bool TryDeriveAgeOver(string? dateOfBirth, DateOnly today, int threshold, out bool isOver)
    {
        isOver = false;
        if (string.IsNullOrWhiteSpace(dateOfBirth)) return false;
        if (!DateOnly.TryParseExact(dateOfBirth, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dob))
            return false;

        var age = today.Year - dob.Year;
        if (dob > today.AddYears(-age)) age--;   // birthday not yet reached this year
        isOver = age >= threshold;
        return true;
    }
}
