// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using FluentAssertions;
using Sorcha.Tenant.Models.Persona;
using Sorcha.UI.Core.Models.Persona;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Persona;

/// <summary>
/// Issue #1271 (UT-008, point 3) — the Default radio rendered with nothing selected even when the
/// citizen had exactly one email. The editor hydrates its rows verbatim from the read model, and a
/// persona whose default was never explicitly set comes back with every row's <c>IsDefault</c>
/// false. "One value, and it is not the default" is not a state that means anything to a citizen —
/// it just reads as a broken control.
/// </summary>
public sealed class PersonaDefaultSelectionTests
{
    private static PersonaEmail Email(string address, bool isDefault) => new(address, isDefault);

    [Fact]
    public void ASingleRowWithNoDefaultBecomesTheDefault()
    {
        var rows = new List<PersonaEmail> { Email("a@example.test", false) };

        PersonaDefaultSelection.EnsureOne(rows, r => r.IsDefault, (r, d) => r with { IsDefault = d });

        rows[0].IsDefault.Should().BeTrue();
    }

    [Fact]
    public void SeveralRowsWithNoDefaultPromoteTheFirst()
    {
        var rows = new List<PersonaEmail>
        {
            Email("a@example.test", false),
            Email("b@example.test", false),
        };

        PersonaDefaultSelection.EnsureOne(rows, r => r.IsDefault, (r, d) => r with { IsDefault = d });

        rows[0].IsDefault.Should().BeTrue();
        rows[1].IsDefault.Should().BeFalse();
    }

    [Fact]
    public void AnExplicitDefaultIsLeftAlone()
    {
        var rows = new List<PersonaEmail>
        {
            Email("a@example.test", false),
            Email("b@example.test", true),
        };

        PersonaDefaultSelection.EnsureOne(rows, r => r.IsDefault, (r, d) => r with { IsDefault = d });

        rows[0].IsDefault.Should().BeFalse("promoting the first would silently move the citizen's choice");
        rows[1].IsDefault.Should().BeTrue();
    }

    [Fact]
    public void SeveralDefaultsAreReducedToTheFirst()
    {
        // Defensive: the read model does not promise exactly one, and two selected radios in one
        // group is a worse control than none.
        var rows = new List<PersonaEmail>
        {
            Email("a@example.test", true),
            Email("b@example.test", true),
        };

        PersonaDefaultSelection.EnsureOne(rows, r => r.IsDefault, (r, d) => r with { IsDefault = d });

        rows.Should().SatisfyRespectively(
            first => first.IsDefault.Should().BeTrue(),
            second => second.IsDefault.Should().BeFalse());
    }

    [Fact]
    public void AnEmptyListIsLeftEmpty_NoDefaultIsInvented()
    {
        var rows = new List<PersonaEmail>();

        PersonaDefaultSelection.EnsureOne(rows, r => r.IsDefault, (r, d) => r with { IsDefault = d });

        rows.Should().BeEmpty();
    }
}
