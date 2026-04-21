// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Services.Designer;
using Xunit;
using BlueprintAction = Sorcha.Blueprint.Models.Action;

namespace Sorcha.UI.Core.Tests.Services.Designer;

public class PreviewPagerLogicTests
{
    private static IReadOnlyList<BlueprintAction> ThreeActions() =>
    [
        new BlueprintAction { Id = 1, Title = "A" },
        new BlueprintAction { Id = 2, Title = "B" },
        new BlueprintAction { Id = 3, Title = "C" }
    ];

    [Fact]
    public void Next_FromMiddle_ReturnsFollowingActionId()
    {
        PreviewPagerLogic.Next(ThreeActions(), "1").Should().Be("2");
    }

    [Fact]
    public void Previous_FromMiddle_ReturnsPrecedingActionId()
    {
        PreviewPagerLogic.Previous(ThreeActions(), "2").Should().Be("1");
    }

    [Fact]
    public void Jump_KnownId_ReturnsId()
    {
        PreviewPagerLogic.Jump(ThreeActions(), "3").Should().Be("3");
    }

    [Fact]
    public void Jump_UnknownId_ReturnsNull()
    {
        PreviewPagerLogic.Jump(ThreeActions(), "99").Should().BeNull();
    }

    [Fact]
    public void Previous_AtFirst_ReturnsNull()
    {
        PreviewPagerLogic.Previous(ThreeActions(), "1").Should().BeNull();
    }

    [Fact]
    public void Next_AtLast_ReturnsNull()
    {
        PreviewPagerLogic.Next(ThreeActions(), "3").Should().BeNull();
    }

    [Fact]
    public void Next_UnknownCurrentId_FallsBackToFirstAction()
    {
        PreviewPagerLogic.Next(ThreeActions(), "99").Should().Be("1");
    }

    [Fact]
    public void Previous_NullCurrentId_FallsBackToFirstAction()
    {
        PreviewPagerLogic.Previous(ThreeActions(), null).Should().Be("1");
    }
}
