// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Components.Forms.Controls;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.AddressLookup;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Forms;

/// <summary>
/// Render-level tests for the postcode field.
/// </summary>
/// <remarks>
/// The uppercase display lives in <c>PostcodeLookupRenderer.razor.css</c> as
/// <c>.postcode-lookup ::deep input</c>. Whether the browser actually applies it is not something a
/// test can answer — bUnit has no style engine — so that half is verified live. What IS testable, and
/// what silently broke this control once already, is the JOIN: the stylesheet addresses the markup by
/// a class name, and nothing else relates the two. Rename or drop the wrapper and the selector matches
/// nothing, with no build error, no warning, and no failing test — the postcode simply renders
/// lowercase again.
/// </remarks>
public sealed class PostcodeLookupRendererTests : BunitContext
{
    public PostcodeLookupRendererTests()
    {
        Services.AddMudServices(o => o.PopoverOptions.CheckForPopoverProvider = false);
        Services.AddSingleton<IFormSchemaService, FormSchemaService>();

        // No provider available: discovery returns null, the renderer degrades to a plain text input
        // and still renders. That is the path with the fewest moving parts for a markup assertion.
        var lookup = new Mock<IAddressLookupClient>();
        lookup.Setup(l => l.GetProvidersAsync(It.IsAny<CancellationToken>()))
              .Returns(Task.FromResult<IReadOnlyList<ProviderInfo>?>(null));
        Services.AddSingleton(lookup.Object);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private const string Schema = """
        {
          "type": "object",
          "properties": {
            "postcode": { "type": "string", "x-address-lookup": true }
          }
        }
        """;

    private IRenderedComponent<PostcodeLookupRenderer> RenderPostcode()
    {
        var context = new FormContext { DataSchema = JsonDocument.Parse(Schema) };
        return Render<PostcodeLookupRenderer>(p => p
            .AddCascadingValue(context)
            .Add(c => c.Control, new Control { Scope = "/postcode", Title = "Postcode" }));
    }

    [Fact]
    public void Renders_TheWrapperClassTheStylesheetTargets()
    {
        // `.postcode-lookup ::deep input` in PostcodeLookupRenderer.razor.css binds to this class.
        var cut = RenderPostcode();

        cut.Find(".postcode-lookup").Should().NotBeNull(
            "the uppercase stylesheet selects on this class and fails silently if it is renamed");
    }

    [Fact]
    public void TheInputIsInsideThatWrapper()
    {
        // ::deep matches DESCENDANTS of the scoped element. An input moved out of the wrapper would
        // leave the selector valid and matching nothing.
        var cut = RenderPostcode();

        cut.FindAll(".postcode-lookup input").Should().NotBeEmpty(
            "::deep only reaches inputs nested inside the scoped wrapper");
    }

    [Fact]
    public void DoesNotReintroduceTheInlineStyleThatSilentlyDidNothing()
    {
        // The first attempt at this put `text-transform: uppercase` on MudTextField's Style parameter.
        // It landed on MudBlazor's wrapper div, where it computes as "uppercase" and achieves nothing:
        // the UA stylesheet sets `text-transform: none` on form controls, so it is not inherited into
        // the input. It looked correct in the markup and in DevTools on the wrapper, and was wrong on
        // screen — which is why it shipped.
        var cut = RenderPostcode();

        cut.Markup.Should().NotContain("text-transform",
            "an inline text-transform here targets the wrapper, not the input, and does nothing");
    }
}
