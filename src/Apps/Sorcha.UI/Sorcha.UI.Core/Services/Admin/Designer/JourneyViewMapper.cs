// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Designer;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// Pure, deterministic mapper that projects a <see cref="BlueprintModel"/> into the read-only
/// <see cref="JourneyViewModel"/> for the F142 "Understand" canvas (FR-006/FR-007/FR-009).
/// No I/O, no mutation of the source Blueprint — same Blueprint always yields the same journey.
/// </summary>
public static class JourneyViewMapper
{
    /// <summary>
    /// Builds a plain-language, role-labelled journey from <paramref name="blueprint"/>.
    /// Steps come out in flow order (following routes from the starting action), falling back to
    /// Action id order when routes are absent or cannot be followed. A <c>null</c> Blueprint, or
    /// one with no actions, yields <see cref="JourneyViewModel.Empty"/>.
    /// </summary>
    /// <param name="blueprint">The Blueprint to project, or <c>null</c>.</param>
    /// <returns>The derived, read-only journey.</returns>
    public static JourneyViewModel Map(BlueprintModel? blueprint)
    {
        if (blueprint is null || blueprint.Actions.Count == 0)
        {
            return JourneyViewModel.Empty;
        }

        // Stable role lookup: participant id -> (index, role). Index drives the colour band
        // so a participant keeps the same colour across every step they act on.
        var roles = BuildRoles(blueprint);

        var ordered = OrderActions(blueprint);

        var steps = new List<JourneyStep>(ordered.Count);
        foreach (var action in ordered)
        {
            steps.Add(MapStep(action, roles));
        }

        return new JourneyViewModel(steps);
    }

    private static IReadOnlyDictionary<string, JourneyRole> BuildRoles(BlueprintModel blueprint)
    {
        var roles = new Dictionary<string, JourneyRole>(StringComparer.Ordinal);
        var index = 0;
        foreach (var participant in blueprint.Participants)
        {
            if (string.IsNullOrEmpty(participant.Id) || roles.ContainsKey(participant.Id))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(participant.Name) ? participant.Id : participant.Name;
            roles[participant.Id] = new JourneyRole(
                participant.Id,
                name,
                index,
                ParticipantInfo.GetColourForIndex(index));
            index++;
        }

        return roles;
    }

    /// <summary>
    /// Orders actions in flow order by walking routes from the starting action. Any actions not
    /// reached by the walk (disconnected, or no routes at all) are appended in ascending id order.
    /// </summary>
    private static IReadOnlyList<BlueprintAction> OrderActions(BlueprintModel blueprint)
    {
        var byId = new Dictionary<int, BlueprintAction>();
        foreach (var action in blueprint.Actions)
        {
            byId.TryAdd(action.Id, action);
        }

        var ordered = new List<BlueprintAction>(blueprint.Actions.Count);
        var visited = new HashSet<int>();

        var start = blueprint.Actions.FirstOrDefault(a => a.IsStartingAction)
                    ?? blueprint.Actions.OrderBy(a => a.Id).FirstOrDefault();

        if (start is not null)
        {
            WalkRoutes(start, byId, ordered, visited);
        }

        // Append anything the route walk did not reach, in ascending id order, for determinism.
        foreach (var action in blueprint.Actions.OrderBy(a => a.Id))
        {
            if (visited.Add(action.Id))
            {
                ordered.Add(action);
            }
        }

        return ordered;
    }

    private static void WalkRoutes(
        BlueprintAction action,
        IReadOnlyDictionary<int, BlueprintAction> byId,
        List<BlueprintAction> ordered,
        HashSet<int> visited)
    {
        if (!visited.Add(action.Id))
        {
            return;
        }

        ordered.Add(action);

        if (action.Routes is null)
        {
            return;
        }

        // Visit next actions in declared route order, then their listed next-action order, for
        // deterministic output regardless of dictionary iteration.
        foreach (var route in action.Routes)
        {
            foreach (var nextId in route.NextActionIds)
            {
                if (byId.TryGetValue(nextId, out var next))
                {
                    WalkRoutes(next, byId, ordered, visited);
                }
            }
        }
    }

    private static JourneyStep MapStep(BlueprintAction action, IReadOnlyDictionary<string, JourneyRole> roles)
    {
        var role = ResolveRole(action, roles);
        var badges = BuildBadges(action);
        var summary = BuildSummary(action, role);
        var detail = BuildDetail(action, role);

        return new JourneyStep(role, action.Title, summary, badges, detail);
    }

    private static JourneyRole ResolveRole(BlueprintAction action, IReadOnlyDictionary<string, JourneyRole> roles)
    {
        if (!string.IsNullOrEmpty(action.Sender) && roles.TryGetValue(action.Sender, out var role))
        {
            return role;
        }

        // Unknown / unset sender: present a neutral, stable placeholder role.
        var name = string.IsNullOrWhiteSpace(action.Sender) ? "Participant" : action.Sender;
        return new JourneyRole(action.Sender ?? string.Empty, name, 0, ParticipantInfo.GetColourForIndex(0));
    }

    private static IReadOnlyList<JourneyBadge> BuildBadges(BlueprintAction action)
    {
        var badges = new List<JourneyBadge>();

        if (action.CredentialRequirements is not null)
        {
            foreach (var requirement in action.CredentialRequirements)
            {
                if (!string.IsNullOrWhiteSpace(requirement.Type))
                {
                    badges.Add(new JourneyBadge(JourneyBadgeKind.MustProve, requirement.Type));
                }
            }
        }

        var issuance = action.CredentialIssuanceConfig;
        if (issuance is not null && !string.IsNullOrWhiteSpace(issuance.CredentialType))
        {
            badges.Add(new JourneyBadge(JourneyBadgeKind.Issues, issuance.CredentialType));
        }

        return badges;
    }

    /// <summary>
    /// Derives a plain-language, factual one-line summary from the participant role and the
    /// action's intent: a starting action "applies", an issuing action "issues …", a routed
    /// reviewer action "reviews / decides", otherwise the participant simply acts on the step.
    /// </summary>
    private static string BuildSummary(BlueprintAction action, JourneyRole role)
    {
        var who = role.Name;

        if (action.CredentialIssuanceConfig is { } issuance && !string.IsNullOrWhiteSpace(issuance.CredentialType))
        {
            return $"{who} issues the {issuance.CredentialType}.";
        }

        if (action.IsStartingAction)
        {
            return $"{who} applies — this starts the service.";
        }

        if (HasDecisionRouting(action))
        {
            return $"{who} reviews and decides what happens next.";
        }

        var title = string.IsNullOrWhiteSpace(action.Title) ? "this step" : action.Title.ToLowerInvariant();
        return $"{who} completes {title}.";
    }

    private static JourneyStepDetail BuildDetail(BlueprintAction action, JourneyRole role)
    {
        var disclosureSummary = BuildDisclosureSummary(action, role);
        var decision = BuildDecisionSummary(action);
        var formReference = $"action-{action.Id}";

        return new JourneyStepDetail(action.Id, disclosureSummary, decision, formReference);
    }

    private static string BuildDisclosureSummary(BlueprintAction action, JourneyRole role)
    {
        var disclosures = action.Disclosures?.ToList() ?? [];
        if (disclosures.Count == 0)
        {
            return $"{role.Name} sees the data captured at this step.";
        }

        var recipientCount = disclosures
            .Select(d => d.ParticipantAddress)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Distinct(StringComparer.Ordinal)
            .Count();

        var fieldCount = disclosures.Sum(d => d.DataPointers?.Count ?? 0);
        var allFields = disclosures.Any(d => d.DataPointers?.Contains("/*") == true);

        var what = allFields
            ? "all submitted fields"
            : fieldCount == 1 ? "1 field" : $"{fieldCount} fields";

        return recipientCount <= 1
            ? $"Discloses {what} to the participant for this step."
            : $"Discloses {what} across {recipientCount} participants.";
    }

    private static string? BuildDecisionSummary(BlueprintAction action)
    {
        if (!HasDecisionRouting(action))
        {
            return null;
        }

        var routeCount = action.Routes!.Count();
        return routeCount > 1
            ? $"Chooses one of {routeCount} routes to determine the next step."
            : "Decides whether the workflow proceeds.";
    }

    private static bool HasDecisionRouting(BlueprintAction action) =>
        action.Routes is not null && action.Routes.Any();
}
