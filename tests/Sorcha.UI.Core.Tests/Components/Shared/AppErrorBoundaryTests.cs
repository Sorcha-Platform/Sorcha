// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.UI.Core.Components.Shared;
using Sorcha.UI.Testing;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Shared;

/// <summary>
/// bUnit tests for the app-wide <see cref="AppErrorBoundary"/> that wraps the
/// routed page in both host shells. Verifies it renders children normally,
/// shows the recovery panel when a child throws, recovers on the Try-again
/// button, and auto-recovers on navigation.
/// </summary>
public sealed class AppErrorBoundaryTests : ComponentTestFixture
{
    /// <summary>A child whose throwing is flipped at will by the test.</summary>
    private sealed class ThrowSwitch { public bool ShouldThrow { get; set; } }

    private sealed class Bomb : ComponentBase
    {
        [Parameter] public ThrowSwitch Switch { get; set; } = default!;

        protected override void OnParametersSet()
        {
            if (Switch.ShouldThrow)
                throw new InvalidOperationException("boom");
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
            => builder.AddMarkupContent(0, "<span data-testid=\"bomb-ok\">ok</span>");
    }

    private static RenderFragment Child(ThrowSwitch sw) => builder =>
    {
        builder.OpenComponent<Bomb>(0);
        builder.AddAttribute(1, nameof(Bomb.Switch), sw);
        builder.CloseComponent();
    };

    private IRenderedComponent<AppErrorBoundary> RenderWith(ThrowSwitch sw) =>
        Render<AppErrorBoundary>(ps => ps.Add(p => p.ChildContent, Child(sw)));

    [Fact]
    public void RendersChild_WhenNoError()
    {
        var cut = RenderWith(new ThrowSwitch { ShouldThrow = false });

        cut.FindAll("[data-testid=bomb-ok]").Should().ContainSingle();
        cut.FindAll("[data-testid=app-error-boundary]").Should().BeEmpty();
    }

    [Fact]
    public void ShowsRecoveryPanel_WhenChildThrows()
    {
        var cut = RenderWith(new ThrowSwitch { ShouldThrow = true });

        cut.Find("[data-testid=app-error-boundary]").TextContent
            .Should().Contain("Something went wrong");
        cut.FindAll("[data-testid=app-error-retry]").Should().ContainSingle();
        cut.FindAll("[data-testid=app-error-reload]").Should().ContainSingle();
    }

    [Fact]
    public void TryAgain_RecoversWhenChildNoLongerThrows()
    {
        var sw = new ThrowSwitch { ShouldThrow = true };
        var cut = RenderWith(sw);
        cut.FindAll("[data-testid=app-error-boundary]").Should().ContainSingle();

        sw.ShouldThrow = false;
        cut.Find("[data-testid=app-error-retry]").Click();

        cut.FindAll("[data-testid=app-error-boundary]").Should().BeEmpty();
        cut.FindAll("[data-testid=bomb-ok]").Should().ContainSingle();
    }

    [Fact]
    public void Navigation_AutoRecoversTheBoundary()
    {
        var sw = new ThrowSwitch { ShouldThrow = true };
        var cut = RenderWith(sw);
        cut.FindAll("[data-testid=app-error-boundary]").Should().ContainSingle();

        sw.ShouldThrow = false;
        var nav = Services.GetRequiredService<NavigationManager>();
        cut.InvokeAsync(() => nav.NavigateTo("/somewhere-else"));

        cut.FindAll("[data-testid=app-error-boundary]").Should().BeEmpty();
        cut.FindAll("[data-testid=bomb-ok]").Should().ContainSingle();
    }
}
