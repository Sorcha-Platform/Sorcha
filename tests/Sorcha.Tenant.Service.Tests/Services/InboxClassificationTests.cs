// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Truth-table tests for <see cref="InboxClassification.IsActionable"/> and
/// <see cref="InboxClassification.ActionablePredicate"/>.
///
/// Rule: actionable when <c>Category == Action</c> OR <c>Severity >= ActionRequired</c>.
/// </summary>
public sealed class InboxClassificationTests
{
    // -------------------------------------------------------------------------
    // Truth table data
    // -------------------------------------------------------------------------

    /// <summary>
    /// Full cross-product of category × severity → expected actionable value.
    ///
    /// | Category   | Info  | Warning | ActionRequired | Critical |
    /// |------------|-------|---------|----------------|----------|
    /// | Action     | true  | true    | true           | true     |
    /// | Credential | false | false   | true           | true     |
    /// | Membership | false | false   | true           | true     |
    /// | Security   | false | false   | true           | true     |
    /// | System     | false | false   | true           | true     |
    /// | Workflow   | false | false   | true           | true     |
    /// | Custom     | false | false   | true           | true     |
    /// </summary>
    public static TheoryData<InboxCategory, InboxSeverity, bool> ActionableTruthTable =>
        new()
        {
            // Action category — always actionable regardless of severity
            { InboxCategory.Action, InboxSeverity.Info,           true  },
            { InboxCategory.Action, InboxSeverity.Warning,        true  },
            { InboxCategory.Action, InboxSeverity.ActionRequired, true  },
            { InboxCategory.Action, InboxSeverity.Critical,       true  },

            // Credential — only actionable when severity >= ActionRequired
            { InboxCategory.Credential, InboxSeverity.Info,           false },
            { InboxCategory.Credential, InboxSeverity.Warning,        false },
            { InboxCategory.Credential, InboxSeverity.ActionRequired, true  },
            { InboxCategory.Credential, InboxSeverity.Critical,       true  },

            // Membership
            { InboxCategory.Membership, InboxSeverity.Info,           false },
            { InboxCategory.Membership, InboxSeverity.Warning,        false },
            { InboxCategory.Membership, InboxSeverity.ActionRequired, true  },
            { InboxCategory.Membership, InboxSeverity.Critical,       true  },

            // Security
            { InboxCategory.Security, InboxSeverity.Info,           false },
            { InboxCategory.Security, InboxSeverity.Warning,        false },
            { InboxCategory.Security, InboxSeverity.ActionRequired, true  },
            { InboxCategory.Security, InboxSeverity.Critical,       true  },

            // System
            { InboxCategory.System, InboxSeverity.Info,           false },
            { InboxCategory.System, InboxSeverity.Warning,        false },
            { InboxCategory.System, InboxSeverity.ActionRequired, true  },
            { InboxCategory.System, InboxSeverity.Critical,       true  },

            // Workflow
            { InboxCategory.Workflow, InboxSeverity.Info,           false },
            { InboxCategory.Workflow, InboxSeverity.Warning,        false },
            { InboxCategory.Workflow, InboxSeverity.ActionRequired, true  },
            { InboxCategory.Workflow, InboxSeverity.Critical,       true  },

            // Custom
            { InboxCategory.Custom, InboxSeverity.Info,           false },
            { InboxCategory.Custom, InboxSeverity.Warning,        false },
            { InboxCategory.Custom, InboxSeverity.ActionRequired, true  },
            { InboxCategory.Custom, InboxSeverity.Critical,       true  },
        };

    // -------------------------------------------------------------------------
    // IsActionable (static method)
    // -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ActionableTruthTable))]
    public void IsActionable_TruthTable_MatchesExpected(
        InboxCategory category,
        InboxSeverity severity,
        bool expected)
    {
        InboxClassification.IsActionable(category, severity).Should().Be(expected);
    }

    // -------------------------------------------------------------------------
    // ActionablePredicate (compiled expression — same logic, database-translatable)
    // -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ActionableTruthTable))]
    public void ActionablePredicate_TruthTable_MatchesExpected(
        InboxCategory category,
        InboxSeverity severity,
        bool expected)
    {
        var fn = InboxClassification.ActionablePredicate.Compile();

        var entry = new InboxEntry
        {
            Category = category,
            Severity = severity,
            // Required properties — values are irrelevant for this predicate
            CorrelationKey = "test:key",
            DetailHref = "/api/test",
            Title = "Test entry",
        };

        fn(entry).Should().Be(expected);
    }

    // -------------------------------------------------------------------------
    // Consistency: IsActionable and ActionablePredicate must agree
    // -------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ActionableTruthTable))]
    public void IsActionable_AndActionablePredicate_AlwaysAgree(
        InboxCategory category,
        InboxSeverity severity,
        bool _)
    {
        var fn = InboxClassification.ActionablePredicate.Compile();

        var entry = new InboxEntry
        {
            Category = category,
            Severity = severity,
            CorrelationKey = "test:key",
            DetailHref = "/api/test",
            Title = "Test entry",
        };

        var fromMethod = InboxClassification.IsActionable(category, severity);
        var fromPredicate = fn(entry);

        fromPredicate.Should().Be(fromMethod,
            because: "ActionablePredicate must always agree with IsActionable for the same inputs");
    }
}
