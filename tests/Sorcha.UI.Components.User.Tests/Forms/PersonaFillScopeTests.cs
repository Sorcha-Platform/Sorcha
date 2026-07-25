// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using FluentAssertions;
using Sorcha.UI.Core.Models.Forms;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Forms;

/// <summary>
/// Issue #1271 (UT-008, point 4) — "no per-field cream tint / self tick on autofilled fields".
/// <para>
/// The markup and the CSS both existed all along: <c>SorchaFormRenderer</c> emits
/// <c>class="… autofilled"</c> plus a <c>persona-tick</c> pill, and the scoped stylesheet carries the
/// cream background and warm border for both. What was broken is the MATCH. The renderer asks
/// "is this field persona-filled?" using the field name from the layout's <c>x-sections</c>
/// (<c>"email"</c>, <c>"address"</c>) and compared it for EXACT equality against the resolver's
/// filled set, which holds full JSON Pointers to leaves (<c>"/email/email"</c>,
/// <c>"/address/postcode"</c>).
/// </para>
/// <para>
/// So the cue only ever lit up for a persona fill that landed on a TOP-LEVEL scalar. Every real form
/// — the AIAS application included — groups its fields into objects, so it never lit up at all. The
/// citizen was given no per-field indication that a value had come from their profile.
/// </para>
/// </summary>
public sealed class PersonaFillScopeTests
{
    [Fact]
    public void ANestedFillMarksItsContainingField()
    {
        // Exactly the AIAS shape: the layout section names "email", the resolver fills "/email/email".
        var filled = new HashSet<string> { "/email/email" };

        PersonaFillScope.CoversField(filled, "email").Should().BeTrue(
            "the citizen's email WAS filled from their profile — the field must say so");
    }

    [Fact]
    public void ADeeplyNestedFillStillMarksTheTopLevelField()
    {
        var filled = new HashSet<string> { "/address/postcode", "/address/town" };

        PersonaFillScope.CoversField(filled, "address").Should().BeTrue();
    }

    [Fact]
    public void ATopLevelFillStillMatches()
    {
        var filled = new HashSet<string> { "/fullName" };

        PersonaFillScope.CoversField(filled, "fullName").Should().BeTrue();
        PersonaFillScope.CoversField(filled, "/fullName").Should().BeTrue(
            "callers pass the layout's field name, which may or may not be pointer-shaped");
    }

    [Fact]
    public void AFieldWhoseNameIsAPrefixOfAnotherIsNotMarked()
    {
        // The decisive case for prefix matching: "/addressLine1" must NOT satisfy "address".
        // A bare StartsWith would light up the wrong field and quietly tell the citizen their
        // address came from their profile when it did not.
        var filled = new HashSet<string> { "/addressLine1" };

        PersonaFillScope.CoversField(filled, "address").Should().BeFalse();
    }

    [Fact]
    public void AnUnfilledFieldIsNotMarked()
    {
        var filled = new HashSet<string> { "/email/email" };

        PersonaFillScope.CoversField(filled, "portrait").Should().BeFalse();
    }

    [Fact]
    public void NothingFilledMarksNothing()
        => PersonaFillScope.CoversField(new HashSet<string>(), "email").Should().BeFalse();
}
