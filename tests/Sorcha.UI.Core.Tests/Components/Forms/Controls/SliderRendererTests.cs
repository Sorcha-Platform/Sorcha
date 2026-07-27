// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Components.Forms.Controls;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms.Controls;

/// <summary>
/// Tests for <see cref="SliderRenderer.ResolveInitialValue"/> — the seeding rule that keeps an
/// absent field from defaulting to 0 when 0 is outside the declared range. Feature AIAS M2.
/// </summary>
public class SliderRendererTests
{
    [Fact]
    public void ResolveInitialValue_ValueAbsent_SeedsFromMinimum()
    {
        SliderRenderer.ResolveInitialValue(null, 3).Should().Be(3);
    }

    [Fact]
    public void ResolveInitialValue_ValuePresent_KeepsValue()
    {
        SliderRenderer.ResolveInitialValue(7, 0).Should().Be(7);
    }

    [Fact]
    public void ResolveInitialValue_ValuePresentAndZero_KeepsZeroRatherThanReseeding()
    {
        SliderRenderer.ResolveInitialValue(0, 3).Should().Be(0);
    }
}
