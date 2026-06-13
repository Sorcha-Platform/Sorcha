// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Sorcha.UI.Testing;
using Xunit;
using FloatingTabBar = Sorcha.UI.Core.Components.Wallet.FloatingTabBar;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// Feature 151 US2 — the "To do" tab count badge: shows the outstanding-action count when > 0,
/// hides when 0, and updates reactively when the count parameter changes (e.g. after a live signal
/// re-fetch in the host).
/// </summary>
public sealed class ActionsBadgeTests : ComponentTestFixture
{
    [Fact]
    public void TodoCount_Positive_RendersBadgeWithCount()
    {
        var cut = Render<FloatingTabBar>(ps => ps.Add(p => p.TodoCount, 3));

        var badge = cut.Find("[data-testid=footer-nav-todo-badge]");
        badge.TextContent.Trim().Should().Be("3");
    }

    [Fact]
    public void TodoCount_Zero_RendersNoBadge()
    {
        var cut = Render<FloatingTabBar>(ps => ps.Add(p => p.TodoCount, 0));

        cut.FindAll("[data-testid=footer-nav-todo-badge]").Should().BeEmpty();
    }

    [Fact]
    public void TodoCount_Change_UpdatesBadgeReactively()
    {
        var cut = Render<FloatingTabBar>(ps => ps.Add(p => p.TodoCount, 2));
        cut.Find("[data-testid=footer-nav-todo-badge]").TextContent.Trim().Should().Be("2");

        cut.Render(ps => ps.Add(p => p.TodoCount, 5));
        cut.Find("[data-testid=footer-nav-todo-badge]").TextContent.Trim().Should().Be("5");

        cut.Render(ps => ps.Add(p => p.TodoCount, 0));
        cut.FindAll("[data-testid=footer-nav-todo-badge]").Should().BeEmpty();
    }
}
