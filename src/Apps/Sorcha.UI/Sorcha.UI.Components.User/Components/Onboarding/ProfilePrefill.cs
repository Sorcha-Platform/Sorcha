// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

namespace Sorcha.UI.Components.User.Components.Onboarding;

/// <summary>
/// Derives the initial profile (persona) values carried across from a freshly signed-up user's
/// identity claims — whatever the signup form or social provider gave us: a split name, granular
/// given/family names, an email, a phone number. Pure and side-effect-free so the carry-across
/// logic (the part that previously dropped the email and never split the name) is unit-testable
/// without rendering the MudBlazor component.
/// </summary>
public static class ProfilePrefill
{
    /// <summary>The seed values, aligned to the CompleteProfileStep form fields.</summary>
    public readonly record struct Seed(
        string? GivenName,
        string? FamilyName,
        string? FullName,
        string? Email,
        string? Phone)
    {
        /// <summary>True when at least one field was carried across from the user's claims.</summary>
        public bool HasData =>
            !string.IsNullOrWhiteSpace(GivenName)
            || !string.IsNullOrWhiteSpace(FamilyName)
            || !string.IsNullOrWhiteSpace(FullName)
            || !string.IsNullOrWhiteSpace(Email)
            || !string.IsNullOrWhiteSpace(Phone);
    }

    /// <summary>
    /// Reads whatever identity claims we hold for <paramref name="user"/>. Prefers granular OIDC /
    /// social claims (<c>given_name</c>, <c>family_name</c>, <c>phone_number</c>) when the provider
    /// supplied them, and falls back to splitting the single display name. Anything absent is left
    /// null for the user to fill in.
    /// </summary>
    public static Seed FromClaims(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var given = FirstNonBlank(user, "given_name", ClaimTypes.GivenName);
        var family = FirstNonBlank(user, "family_name", ClaimTypes.Surname);
        var displayName = FirstNonBlank(user, "name", "preferred_username", ClaimTypes.Name);

        string? fullName = null;
        if (!string.IsNullOrWhiteSpace(given) || !string.IsNullOrWhiteSpace(family))
        {
            // Granular name claims present — keep the provider's display name as the fallback,
            // or synthesise one from the parts.
            fullName = !string.IsNullOrWhiteSpace(displayName) ? displayName : JoinName(given, family);
        }
        else if (!string.IsNullOrWhiteSpace(displayName))
        {
            (given, family) = SplitDisplayName(displayName!);
            fullName = displayName;
        }

        var email = FirstNonBlank(user, "email", ClaimTypes.Email);
        var phone = FirstNonBlank(user, "phone_number", ClaimTypes.MobilePhone, ClaimTypes.HomePhone);

        return new Seed(Trim(given), Trim(family), Trim(fullName), Trim(email), Trim(phone));
    }

    /// <summary>
    /// Splits a single display name into a best-effort (given, family) pair: first token is the
    /// given name, the remainder is the family name. A single token yields a given name only.
    /// </summary>
    public static (string? Given, string? Family) SplitDisplayName(string displayName)
    {
        var parts = (displayName ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => (null, null),
            1 => (parts[0], null),
            _ => (parts[0], string.Join(' ', parts[1..])),
        };
    }

    private static string JoinName(string? given, string? family) =>
        string.Join(' ', new[] { given, family }.Where(s => !string.IsNullOrWhiteSpace(s)));

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonBlank(ClaimsPrincipal user, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = user.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
