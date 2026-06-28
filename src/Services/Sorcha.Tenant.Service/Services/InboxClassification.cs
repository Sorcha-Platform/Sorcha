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
}
