// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq.Expressions;

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Static helpers for classifying inbox entries as actionable. Centralises the
/// definition of "actionable" — <c>Category == Action</c> or
/// <c>Severity >= ActionRequired</c> — so that in-memory guards and EF Core
/// query predicates share a single source of truth.
/// </summary>
public static class InboxClassification
{
    /// <summary>
    /// Returns <c>true</c> when the entry is actionable, i.e. when
    /// <c>Category == Action</c> or <c>Severity >= ActionRequired</c>.
    /// </summary>
    /// <param name="category">The entry's domain category.</param>
    /// <param name="severity">The entry's severity level.</param>
    public static bool IsActionable(InboxCategory category, InboxSeverity severity) =>
        category == InboxCategory.Action || severity >= InboxSeverity.ActionRequired;

    /// <summary>
    /// EF Core-translatable predicate that matches actionable entries, i.e. those
    /// where <c>Category == Action</c> or <c>Severity >= ActionRequired</c>.
    /// Pass to <c>IQueryable&lt;InboxEntry&gt;.Where</c> to let the database
    /// apply the filter rather than loading all rows into memory.
    /// </summary>
    public static readonly Expression<Func<InboxEntry, bool>> ActionablePredicate =
        e => e.Category == InboxCategory.Action || e.Severity >= InboxSeverity.ActionRequired;

    /// <summary>
    /// True when an entry should draw the user's attention — <c>Category == Action</c>, or severity at
    /// <c>Warning</c> or above. Strictly wider than <see cref="IsActionable"/>.
    /// </summary>
    /// <remarks>
    /// Issue #1267: the unread bell badge counted <see cref="ActionablePredicate"/> only, i.e.
    /// <c>Severity &gt;= ActionRequired</c>. A rejected identity application is written by F184 as
    /// <c>Category=Workflow, Severity=Warning</c>, which matches neither arm — so the entry existed,
    /// the PWA Activity feed listed it, and the web bell showed NO badge. The citizen's own
    /// conclusion was that the application had vanished.
    /// <para>
    /// "Needs attention" is deliberately not "unread": <c>Info</c> entries such as "Profile updated"
    /// still do not badge, so the bell keeps meaning something rather than becoming a generic unread
    /// counter. This is the narrower of the two options — widening the badge, not redefining it.
    /// </para>
    /// </remarks>
    public static bool NeedsAttention(InboxCategory category, InboxSeverity severity) =>
        category == InboxCategory.Action || severity >= InboxSeverity.Warning;

    /// <summary>
    /// EF Core-translatable counterpart of <see cref="NeedsAttention"/>. Backs the unread bell badge.
    /// </summary>
    public static readonly Expression<Func<InboxEntry, bool>> NeedsAttentionPredicate =
        e => e.Category == InboxCategory.Action || e.Severity >= InboxSeverity.Warning;
}
