// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using Bunit;
using Bunit.TestDoubles;

using FluentAssertions;

using Microsoft.AspNetCore.Components;

using Sorcha.UI.Core.Services;
using Sorcha.UI.Testing;

using Xunit;

using LinkRequiredGate = Sorcha.UI.Web.Client.Components.AccountLink.LinkRequiredGate;
using LinkExistingAccountPrompt = Sorcha.UI.Core.Components.AccountLink.LinkExistingAccountPrompt;

namespace Sorcha.UI.Core.Tests.AccountLink;

/// <summary>
/// Feature 173 — the boot-time gate for the LinkRequired step-up flow.
/// </summary>
/// <remarks>
/// <para>
/// The gate's own doc comment has always said it renders the prompt "as a full-screen takeover
/// <b>instead of</b> the normal signed-out home". It did not: it was a bare <c>@if</c> mounted as a
/// sibling of the router in <c>Routes.razor</c>, so the prompt rendered <i>in addition to</i> the
/// routed page — as a plain block element in normal flow <b>above</b> <c>MudLayout</c>.
/// </para>
/// <para>
/// Measured live against Docker: with the prompt showing, <c>.mud-main-content</c> sat at
/// <b>136.56px</b> instead of <b>0</b>, while <c>.mud-appbar</c> stayed at 0 because it is
/// position-fixed. The whole application was pushed down by the height of the prompt and the
/// prompt's own text was sliced off under the app bar.
/// </para>
/// <para>
/// These tests pin the containment relationship rather than the pixels: the routed content is a
/// <b>child</b> of the gate, and the gate renders exactly one of the two. A layout that cannot
/// render both cannot displace one with the other.
/// </para>
/// </remarks>
public sealed class LinkRequiredGateTests : ComponentTestFixture
{
    private const string RoutedContentMarker = "routed-content-marker";

    public LinkRequiredGateTests()
    {
        // The takeover branch renders its own MudThemeProvider, because MudBlazor's providers live
        // in MainLayout — inside the children the gate suppresses.
        ProvideMock<IThemeService>();
    }

    /// <summary>Stages a link-pending token the way <c>fragment-handoff.js</c> does.</summary>
    private void StageLinkPendingToken(string token)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new { linkPendingToken = token }));

        JSInterop.Setup<JsonElement?>("sorcha.fragmentHandoff.getLinkPending")
            .SetResult(payload);
    }

    /// <summary>No token staged — the JS shim returns null.</summary>
    private void StageNoToken() =>
        JSInterop.Setup<JsonElement?>("sorcha.fragmentHandoff.getLinkPending")
            .SetResult(null);

    private RenderFragment RoutedContent() => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "data-testid", RoutedContentMarker);
        builder.CloseElement();
    };

    private IRenderedComponent<LinkRequiredGate> RenderGate()
    {
        // The real prompt injects nine services and starts a step-up flow on render; stubbing it
        // keeps these tests about the gate's ONE decision — which branch renders.
        ComponentFactories.AddStub<LinkExistingAccountPrompt>();

        return Render<LinkRequiredGate>(parameters => parameters
            .Add(p => p.ChildContent, RoutedContent()));
    }

    [Fact]
    public void WithNoLinkPendingToken_RendersTheRoutedContent()
    {
        StageNoToken();

        var gate = RenderGate();

        gate.WaitForAssertion(() =>
            gate.FindAll($"[data-testid='{RoutedContentMarker}']").Should().HaveCount(1,
                "the gate must be a transparent passthrough when no link is pending"));
        gate.FindComponents<Stub<LinkExistingAccountPrompt>>().Should().BeEmpty();
    }

    [Fact]
    public void WithALinkPendingToken_RendersThePrompt()
    {
        StageLinkPendingToken("token-abc");

        var gate = RenderGate();

        gate.WaitForAssertion(() =>
            gate.FindComponents<Stub<LinkExistingAccountPrompt>>().Should().HaveCount(1));
    }

    [Fact]
    public void WithALinkPendingToken_SuppressesTheRoutedContent()
    {
        // The defect. Rendering both put an unstyled block above MudLayout and displaced the whole
        // app — and left the page behind an auth step-up reachable.
        StageLinkPendingToken("token-abc");

        var gate = RenderGate();

        gate.WaitForAssertion(() =>
            gate.FindComponents<Stub<LinkExistingAccountPrompt>>().Should().HaveCount(1));
        gate.FindAll($"[data-testid='{RoutedContentMarker}']").Should().BeEmpty(
            "the prompt is a takeover — the routed page must not render alongside it");
    }

    [Fact]
    public void TheTokenFromStagingIsHandedToThePrompt()
    {
        StageLinkPendingToken("token-xyz");

        var gate = RenderGate();

        gate.WaitForAssertion(() =>
            gate.FindComponents<Stub<LinkExistingAccountPrompt>>().Should().HaveCount(1));
        gate.FindComponent<Stub<LinkExistingAccountPrompt>>()
            .Instance.Parameters[nameof(LinkExistingAccountPrompt.LinkPendingToken)]
            .Should().Be("token-xyz");
    }

    [Fact]
    public void WhenStagingThrows_RendersTheRoutedContent()
    {
        // JS interop is unavailable in some hosts. Failing closed here would mean a blank app, so
        // the gate must fall through to the routed page rather than swallow the whole UI.
        JSInterop.Setup<JsonElement?>("sorcha.fragmentHandoff.getLinkPending")
            .SetException(new InvalidOperationException("no JS here"));

        var gate = RenderGate();

        gate.WaitForAssertion(() =>
            gate.FindAll($"[data-testid='{RoutedContentMarker}']").Should().HaveCount(1));
    }
}
