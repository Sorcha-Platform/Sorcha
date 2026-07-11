// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using Sorcha.Verifier.Engine.Dcql;
using CredentialRequirementModel = Sorcha.Blueprint.Models.Credentials.CredentialRequirement;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 181 US2 — maps an action's <see cref="CredentialRequirementModel"/> set onto a single
/// DCQL <see cref="DcqlQuery"/> (contract §4). Each requirement becomes one credential query
/// (id = slugified requirement key); requirements sharing an <see cref="CredentialRequirementModel.AnyOfGroup"/>
/// tag ride one <c>credential_sets</c> entry as alternatives, while ungrouped requirements are
/// each individually required. THE one place blueprint requirements become a wire query — both the
/// SorchaWallet consumer's served request object and the HAIP verifier's declared query build
/// through here (FR-008), delegating the wire shape to the shared <see cref="DcqlRequestBuilder"/>.
/// </summary>
public static class RequirementDcqlMapper
{
    /// <summary>
    /// Build a <see cref="DcqlQuery"/> from the supplied requirements.
    /// </summary>
    /// <remarks>
    /// <para>Single requirement, or several with no <c>anyOfGroup</c> tag ⇒ one credential query per
    /// requirement and NO <c>credential_sets</c> (a plain AND over every ask — the single-ask shape is
    /// byte-identical to the pre-US2 producer). This preserves the wallet/verifier "absent
    /// credential_sets ⇒ every query required" convention.</para>
    /// <para>As soon as ANY requirement carries an <c>anyOfGroup</c>, the requiredness of every ask must
    /// be made explicit (a bare <c>credentials</c> list with <c>credential_sets</c> present would leave
    /// ungrouped asks optional under the DCQL solver), so this emits: one required set per named group
    /// (options = each grouped query as its own singleton — any one satisfies), plus one required
    /// singleton set per ungrouped requirement.</para>
    /// </remarks>
    /// <param name="requirements">The action's presentation requirements (already filtered to a single
    /// presentation source by the caller). Must be non-empty.</param>
    public static DcqlQuery Build(IReadOnlyList<CredentialRequirementModel> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        if (requirements.Count == 0)
        {
            throw new ArgumentException("At least one credential requirement is required.", nameof(requirements));
        }

        // Assign each requirement a unique, DCQL-legal id derived from its type.
        var asks = new List<DcqlCredentialAsk>(requirements.Count);
        var idByRequirement = new List<(CredentialRequirementModel Req, string Id)>(requirements.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var req in requirements)
        {
            var id = MakeUniqueId(req.Type, used);
            used.Add(id);
            idByRequirement.Add((req, id));
            var requiredClaims = req.RequiredClaims?.Select(c => c.ClaimName).ToList() ?? [];
            asks.Add(DcqlCredentialAsk.SdJwt(id, req.Type, requiredClaims));
        }

        // No alternative groups anywhere ⇒ plain AND (no credential_sets).
        var hasAnyGroup = requirements.Any(r => !string.IsNullOrWhiteSpace(r.AnyOfGroup));
        if (!hasAnyGroup)
        {
            return DcqlRequestBuilder.Build(asks);
        }

        // Make every ask's requiredness explicit once any group exists.
        var alternatives = new List<DcqlAlternativeGroup>();

        // Named groups → one required set with each member as a singleton option (any one satisfies).
        foreach (var group in idByRequirement
            .Where(x => !string.IsNullOrWhiteSpace(x.Req.AnyOfGroup))
            .GroupBy(x => x.Req.AnyOfGroup!, StringComparer.Ordinal))
        {
            var options = group.Select(x => (IReadOnlyList<string>)new[] { x.Id }).ToList();
            var purpose = group
                .Select(x => x.Req.Description)
                .FirstOrDefault(d => !string.IsNullOrWhiteSpace(d));
            alternatives.Add(new DcqlAlternativeGroup(options, Required: true, Purpose: purpose));
        }

        // Ungrouped requirements → one required singleton set each (keeps them AND-required).
        foreach (var (req, id) in idByRequirement.Where(x => string.IsNullOrWhiteSpace(x.Req.AnyOfGroup)))
        {
            alternatives.Add(new DcqlAlternativeGroup(
                [[id]], Required: true, Purpose: req.Description));
        }

        return DcqlRequestBuilder.Build(asks, alternatives);
    }

    /// <summary>
    /// Slugify a requirement type into the DCQL id charset (<c>[a-zA-Z0-9_-]+</c>) and
    /// disambiguate collisions with a numeric suffix.
    /// </summary>
    private static string MakeUniqueId(string type, ISet<string> used)
    {
        var slug = Slugify(type);
        if (!used.Contains(slug))
        {
            return slug;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{slug}-{i}";
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string Slugify(string type)
    {
        var sb = new StringBuilder(type.Length);
        foreach (var ch in type)
        {
            sb.Append(char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' ? ch : '-');
        }

        // Collapse to a legal, non-empty id. Trim leading/trailing hyphens for tidiness.
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "credential" : slug;
    }
}
