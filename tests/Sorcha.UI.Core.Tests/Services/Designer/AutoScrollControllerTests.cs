// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Moq;
using Sorcha.UI.Core.Services.Designer;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Designer;

public class AutoScrollControllerTests
{
    private static (AutoScrollController ctrl, Mock<IJSRuntime> js) CreateController()
    {
        var js = new Mock<IJSRuntime>(MockBehavior.Loose);
        js.Setup(r => r.InvokeAsync<IJSVoidResult>("sorcha.designer.scrollToBottom", It.IsAny<object[]>()))
          .Returns(ValueTask.FromResult<IJSVoidResult>(default!));
        return (new AutoScrollController(js.Object), js);
    }

    [Fact]
    public async Task OnContentAppendedAsync_WhenEnabled_InvokesScrollToBottom()
    {
        var (ctrl, js) = CreateController();

        await ctrl.OnContentAppendedAsync("msgs");

        js.Verify(r => r.InvokeAsync<IJSVoidResult>(
            "sorcha.designer.scrollToBottom",
            It.Is<object[]>(args => args.Length == 1 && (string)args[0]! == "msgs")),
            Times.Once);
    }

    [Fact]
    public async Task OnUserScroll_FarFromBottom_DisablesAutoScroll()
    {
        var (ctrl, js) = CreateController();

        // 1000 total, viewport 200, scrolled to 500 → distance = 1000 - 700 = 300 > 40
        ctrl.OnUserScroll(scrollTop: 500, scrollHeight: 1000, clientHeight: 200);
        ctrl.IsAutoScrollEnabled.Should().BeFalse();

        await ctrl.OnContentAppendedAsync("msgs");

        js.Verify(r => r.InvokeAsync<IJSVoidResult>(
            "sorcha.designer.scrollToBottom", It.IsAny<object[]>()),
            Times.Never);
    }

    [Fact]
    public async Task OnUserScroll_BackToBottom_ReEnablesAutoScroll()
    {
        var (ctrl, js) = CreateController();

        ctrl.OnUserScroll(scrollTop: 500, scrollHeight: 1000, clientHeight: 200);
        ctrl.IsAutoScrollEnabled.Should().BeFalse();

        // Back to the bottom: 1000 - (790 + 200) = 10 ≤ 40
        ctrl.OnUserScroll(scrollTop: 790, scrollHeight: 1000, clientHeight: 200);
        ctrl.IsAutoScrollEnabled.Should().BeTrue();

        await ctrl.OnContentAppendedAsync("msgs");

        js.Verify(r => r.InvokeAsync<IJSVoidResult>(
            "sorcha.designer.scrollToBottom", It.IsAny<object[]>()),
            Times.Once);
    }

    [Fact]
    public async Task OnContentAppendedAsync_WithUsableRuntime_DoesNotThrow()
    {
        var (ctrl, _) = CreateController();

        var act = async () => await ctrl.OnContentAppendedAsync("msgs");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnContentAppendedAsync_RapidAppends_DoNotThrow()
    {
        var (ctrl, js) = CreateController();

        for (var i = 0; i < 10; i++)
        {
            await ctrl.OnContentAppendedAsync("msgs");
        }

        js.Verify(r => r.InvokeAsync<IJSVoidResult>(
            "sorcha.designer.scrollToBottom", It.IsAny<object[]>()),
            Times.Exactly(10));
    }
}
