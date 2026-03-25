// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// Categorisation of a single credential claim for selective-disclosure UI.
/// </summary>
public enum DisclosureCategory
{
    /// <summary>The verifier requires this claim to be shared.</summary>
    Required,

    /// <summary>The verifier has requested this claim but the holder may withhold it.</summary>
    Optional,

    /// <summary>The claim is present in the credential but was not requested by the verifier.</summary>
    NotRequested,
}

/// <summary>
/// View model for a single claim in a credential disclosure picker.
/// </summary>
public class DisclosureClaimViewModel
{
    /// <summary>The claim name / key.</summary>
    public required string ClaimName { get; init; }

    /// <summary>The claim value from the credential.</summary>
    public required object ClaimValue { get; init; }

    /// <summary>Disclosure category assigned by <see cref="CategoriseClaims"/>.</summary>
    public required DisclosureCategory Category { get; init; }

    /// <summary>
    /// Whether the holder is currently sharing this claim.
    /// Required claims are always true; optional and not-requested claims default to false.
    /// </summary>
    public bool IsSharing { get; set; }

    /// <summary>
    /// Categorises all claims from a credential into a display-ready list ordered
    /// Required → Optional → NotRequested.
    /// </summary>
    /// <param name="allClaims">All claims present in the credential.</param>
    /// <param name="requiredClaims">Claims the verifier mandates.</param>
    /// <param name="disclosable">Claims the credential marks as selectively disclosable.</param>
    /// <param name="optionalClaims">Claims the verifier requests but permits withholding.</param>
    /// <returns>Ordered list of <see cref="DisclosureClaimViewModel"/>.</returns>
    public static List<DisclosureClaimViewModel> CategoriseClaims(
        Dictionary<string, object> allClaims,
        IEnumerable<string> requiredClaims,
        IEnumerable<string>? disclosable = null,
        IEnumerable<string>? optionalClaims = null)
    {
        var requiredSet = new HashSet<string>(requiredClaims, StringComparer.OrdinalIgnoreCase);
        var optionalSet = new HashSet<string>(optionalClaims ?? [], StringComparer.OrdinalIgnoreCase);

        var result = new List<DisclosureClaimViewModel>(allClaims.Count);

        foreach (var (key, value) in allClaims)
        {
            DisclosureCategory category;
            bool isSharing;

            if (requiredSet.Contains(key))
            {
                category = DisclosureCategory.Required;
                isSharing = true;
            }
            else if (optionalSet.Contains(key))
            {
                category = DisclosureCategory.Optional;
                isSharing = false;
            }
            else
            {
                category = DisclosureCategory.NotRequested;
                isSharing = false;
            }

            result.Add(new DisclosureClaimViewModel
            {
                ClaimName = key,
                ClaimValue = value,
                Category = category,
                IsSharing = isSharing,
            });
        }

        // Order: Required → Optional → NotRequested
        return result
            .OrderBy(c => c.Category switch
            {
                DisclosureCategory.Required => 0,
                DisclosureCategory.Optional => 1,
                _ => 2,
            })
            .ToList();
    }
}
