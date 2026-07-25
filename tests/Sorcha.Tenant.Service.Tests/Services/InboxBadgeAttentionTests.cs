// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Guards issue #1267: a rejected identity application produced an inbox entry that the web bell
/// could never badge, so every surface a citizen would naturally check showed nothing and their own
/// conclusion was that the application had vanished.
/// <para>
/// F184 writes a decision notice as <c>Category = Workflow, Severity = Warning</c>. The badge counted
/// <c>ActionablePredicate</c> — <c>Category == Action || Severity &gt;= ActionRequired</c> — which
/// matches neither arm. The entry existed and the PWA Activity feed listed it; only the count
/// excluded it.
/// </para>
/// </summary>
public sealed class InboxBadgeAttentionTests
{
    /// <summary>THE reported case: exactly what F184 writes for a rejection.</summary>
    [Fact]
    public void DecisionNotice_WorkflowWarning_NeedsAttention()
        => InboxClassification.NeedsAttention(InboxCategory.Workflow, InboxSeverity.Warning)
            .Should().BeTrue(
                "a rejected identity application must reach the bell — this is the entry that went "
                + "unnoticed and made the citizen think their application had vanished (#1267)");

    /// <summary>
    /// The precise gap being closed: the same entry is NOT "actionable", which is why the old badge
    /// missed it. Asserting both sides documents that the two notions are deliberately different
    /// rather than one being a mistake.
    /// </summary>
    [Fact]
    public void DecisionNotice_IsNotActionable_WhichIsWhyTheOldBadgeMissedIt()
    {
        InboxClassification.IsActionable(InboxCategory.Workflow, InboxSeverity.Warning)
            .Should().BeFalse();
        InboxClassification.NeedsAttention(InboxCategory.Workflow, InboxSeverity.Warning)
            .Should().BeTrue();
    }

    /// <summary>
    /// The badge must NOT become a generic unread counter — routine information still stays quiet, or
    /// the badge stops meaning anything and gets ignored.
    /// </summary>
    [Theory]
    [InlineData(InboxCategory.System)]
    [InlineData(InboxCategory.Membership)]
    [InlineData(InboxCategory.Credential)]
    [InlineData(InboxCategory.Workflow)]
    public void InfoSeverity_DoesNotNeedAttention(InboxCategory category)
        => InboxClassification.NeedsAttention(category, InboxSeverity.Info).Should().BeFalse(
            "\"Profile updated\" must not badge the bell (#1267)");

    /// <summary>An Action-category entry needs attention regardless of a quiet severity.</summary>
    [Fact]
    public void ActionCategory_NeedsAttention_EvenAtInfoSeverity()
        => InboxClassification.NeedsAttention(InboxCategory.Action, InboxSeverity.Info)
            .Should().BeTrue();

    /// <summary>Everything already counted must keep counting — widening must not drop anything.</summary>
    [Theory]
    [InlineData(InboxCategory.Workflow, InboxSeverity.ActionRequired)]
    [InlineData(InboxCategory.Security, InboxSeverity.Critical)]
    [InlineData(InboxCategory.Action, InboxSeverity.Info)]
    public void EverythingActionable_AlsoNeedsAttention(InboxCategory category, InboxSeverity severity)
    {
        InboxClassification.IsActionable(category, severity).Should().BeTrue("test premise");
        InboxClassification.NeedsAttention(category, severity).Should().BeTrue(
            "needs-attention must be strictly WIDER than actionable, never different");
    }

    /// <summary>
    /// Derived from the enum itself: needs-attention must be a superset of actionable for every
    /// combination the server can produce, so a future severity cannot make the badge narrower.
    /// </summary>
    [Fact]
    public void NeedsAttention_IsASupersetOfActionable_ForEveryCombination()
    {
        foreach (var category in Enum.GetValues<InboxCategory>())
        {
            foreach (var severity in Enum.GetValues<InboxSeverity>())
            {
                if (InboxClassification.IsActionable(category, severity))
                {
                    InboxClassification.NeedsAttention(category, severity).Should().BeTrue(
                        $"{category}/{severity} is actionable, so it must also need attention");
                }
            }
        }
    }
}
