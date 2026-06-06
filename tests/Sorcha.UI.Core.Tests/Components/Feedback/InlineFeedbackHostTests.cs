// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.UI.Core.Components.Feedback;
using Sorcha.UI.Core.Services.Feedback;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Feedback;

/// <summary>
/// bunit smoke for <see cref="InlineFeedbackHost"/> — proves the component
/// resolves its DI dependency and renders a clean container when there are
/// no messages. The full message-rendering integration is exercised by the
/// Playwright E2E suite; rendering <c>MudAlert</c> inside <c>@foreach</c>
/// under bunit hits a known render-tree diff edge case and isn't worth
/// fighting at unit-test scope when the service-side contract is already
/// covered by <see cref="InlineFeedbackTests"/>.
/// </summary>
public sealed class InlineFeedbackHostTests : BunitContext
{
    public InlineFeedbackHostTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IInlineFeedback>(new InlineFeedback());
    }

    [Fact]
    public void Host_WithNoMessages_RendersEmptyContainer()
    {
        var cut = Render<InlineFeedbackHost>();

        cut.Find("[data-testid='inline-feedback-host']").TextContent.Trim()
            .Should().BeEmpty();
    }

    [Fact]
    public void Host_Container_IsAPoliteLiveRegion()
    {
        // The host is the platform-wide feedback surface; without a live region
        // screen-reader users never hear success/error messages.
        var cut = Render<InlineFeedbackHost>();

        var host = cut.Find("[data-testid='inline-feedback-host']");
        host.GetAttribute("aria-live").Should().Be("polite");
        host.GetAttribute("aria-atomic").Should().Be("false");
    }
}
