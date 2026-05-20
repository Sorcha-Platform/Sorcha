// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Wire = Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// One merged presentation entry for the Activity feed. UI-agnostic so the merge
/// rule (Feature 114 US5 PR3, design §5) can be unit-tested without rendering a
/// component.
/// </summary>
/// <param name="Id">Wallet-generated presentation entry id (the unit of identity).</param>
/// <param name="Title">Display title (e.g. "Presented Assured Identity").</param>
/// <param name="Subtitle">Verifier · claim count · outcome.</param>
/// <param name="PresentedAt">UTC time the presentation was made (sort key).</param>
public sealed record PresentationActivityItem(
    Guid Id,
    string Title,
    string Subtitle,
    DateTimeOffset PresentedAt);

/// <summary>
/// Pure merge of the citizen's server-side presentation history with the
/// device-local presentation log (Feature 114, US5 PR3).
/// </summary>
/// <remarks>
/// Implements design §5:
/// <code>display = (server history) ∪ { local entries where !SyncedToServer }</code>
/// <list type="bullet">
/// <item>A just-made presentation (<c>SyncedToServer == false</c>) is shown from the
/// local log immediately — instant feedback before the next sync.</item>
/// <item>Once synced, the entry is represented by the server list; the synced local
/// copy is display-suppressed, so a server-authoritative delete removes it from every
/// device and a lingering synced local copy never resurrects it.</item>
/// <item>Server rows are enriched with the matching local entry's credential label
/// where available (originating device only); other devices fall back to a generic
/// title since the wire shape carries no credential type.</item>
/// </list>
/// </remarks>
public static class PresentationActivityMerge
{
    /// <summary>Build the merged, newest-first presentation activity list.</summary>
    public static IReadOnlyList<PresentationActivityItem> Build(
        IReadOnlyList<Wire.PresentationLogEntry> serverEntries,
        IReadOnlyList<PresentationLogEntry> localEntries)
    {
        ArgumentNullException.ThrowIfNull(serverEntries);
        ArgumentNullException.ThrowIfNull(localEntries);

        var localById = localEntries
            .GroupBy(e => e.Id)
            .ToDictionary(g => g.Key, g => g.First());
        var serverIds = serverEntries.Select(e => e.Id).ToHashSet();

        var items = new List<PresentationActivityItem>(serverEntries.Count + localEntries.Count);

        foreach (var s in serverEntries)
        {
            var local = localById.GetValueOrDefault(s.Id);
            var label = local?.CredentialLabel ?? local?.CredentialType ?? "a credential";
            items.Add(new PresentationActivityItem(
                s.Id, $"Presented {label}", BuildServerSubtitle(s), s.PresentedAt));
        }

        // Only not-yet-synced local entries that the server list does not already cover.
        foreach (var l in localEntries.Where(e => !e.SyncedToServer && !serverIds.Contains(e.Id)))
        {
            items.Add(new PresentationActivityItem(
                l.Id, $"Presented {l.CredentialLabel ?? l.CredentialType}", BuildLocalSubtitle(l), l.PresentedAt));
        }

        return items.OrderByDescending(i => i.PresentedAt).ToList();
    }

    private static string BuildLocalSubtitle(PresentationLogEntry p)
    {
        var claimText = p.DisclosedClaims.Count == 1 ? "1 claim" : $"{p.DisclosedClaims.Count} claims";
        var outcomeText = p.Outcome == PresentationLogOutcome.Sent ? "Sent" : "Rejected";
        return $"{p.VerifierLabel} · {claimText} · {outcomeText}";
    }

    private static string BuildServerSubtitle(Wire.PresentationLogEntry p)
    {
        var claimText = p.DisclosedClaims.Count == 1 ? "1 claim" : $"{p.DisclosedClaims.Count} claims";
        var outcomeText = p.Outcome switch
        {
            Wire.PresentationLogOutcome.VerifierRejected => "Rejected",
            Wire.PresentationLogOutcome.DeclinedByCitizen => "Declined",
            _ => "Sent" // Presented / Acknowledged
        };
        var verifier = string.IsNullOrWhiteSpace(p.VerifierLabel) ? "Unknown verifier" : p.VerifierLabel;
        return $"{verifier} · {claimText} · {outcomeText}";
    }
}
