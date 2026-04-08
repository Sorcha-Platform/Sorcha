// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using MudBlazor.Services;
using Sorcha.Tenant.Models.Persona;
using Sorcha.UI.Core.Components.Forms;
using Sorcha.UI.Core.Models.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// bUnit tests for <see cref="PersonaFillSummary"/>. Feature 092 / US3 / T061.
/// </summary>
public sealed class PersonaFillSummaryTests : Bunit.BunitContext
{
    public PersonaFillSummaryTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static PersonaFillResult Fill(string path, string attr, object? value) =>
        new(path, attr, value, PersonaAttributeSource.SelfAsserted, PersonaMatchMode.Inference);

    [Fact]
    public void RendersNothing_WhenCountIsZero()
    {
        var cut = Render<PersonaFillSummary>(parameters => parameters
            .Add(p => p.Count, 0)
            .Add(p => p.Fills, Array.Empty<PersonaFillResult>()));

        cut.FindAll(".persona-fill-summary").Should().BeEmpty();
    }

    [Fact]
    public void RendersSummaryAndAnnouncesPolitely_WhenCountIsPositive()
    {
        var fills = new[]
        {
            Fill("/email", "defaultEmail", "ada@example.com"),
            Fill("/firstName", "givenName", "Ada"),
            Fill("/dob", "dateOfBirth", new DateOnly(1815, 12, 10)),
        };

        var cut = Render<PersonaFillSummary>(parameters => parameters
            .Add(p => p.Count, fills.Length)
            .Add(p => p.Fills, fills));

        var banner = cut.Find(".persona-fill-summary");
        banner.GetAttribute("role").Should().Be("status");
        banner.GetAttribute("aria-live").Should().Be("polite");
        banner.TextContent.Should().Contain("3 fields filled from your profile");
    }

    [Fact]
    public void SingularFormat_WhenCountIsOne()
    {
        var cut = Render<PersonaFillSummary>(parameters => parameters
            .Add(p => p.Count, 1)
            .Add(p => p.Fills, new[] { Fill("/email", "defaultEmail", "ada@example.com") }));

        cut.Find(".persona-fill-summary").TextContent.Should().Contain("1 field filled");
    }

    [Fact]
    public async Task ClearAll_RaisesOnClearAllCallback()
    {
        var clearAllFired = false;
        var cut = Render<PersonaFillSummary>(parameters => parameters
            .Add(p => p.Count, 2)
            .Add(p => p.Fills, new[]
            {
                Fill("/email", "defaultEmail", "ada@example.com"),
                Fill("/firstName", "givenName", "Ada"),
            })
            .Add(p => p.OnClearAll, () => { clearAllFired = true; }));

        var buttons = cut.FindAll("button");
        var clearAll = buttons.First(b => b.TextContent.Contains("Clear all"));
        await cut.InvokeAsync(() => clearAll.Click());

        clearAllFired.Should().BeTrue();
    }

    [Fact]
    public void OffState_RendersFillFromProfileButton_WhenPendingCountPositive()
    {
        var cut = Render<PersonaFillSummary>(parameters => parameters
            .Add(p => p.IsAutofillEnabled, false)
            .Add(p => p.Count, 0)
            .Add(p => p.PendingCount, 4)
            .Add(p => p.Fills, Array.Empty<PersonaFillResult>()));

        cut.FindAll(".persona-fill-summary-offstate").Should().NotBeEmpty();
        cut.Find(".persona-fill-summary-offstate").TextContent.Should().Contain("Your profile can fill 4 fields");
        cut.FindAll("button").Any(b => b.TextContent.Contains("Fill from profile")).Should().BeTrue();
    }

    [Fact]
    public async Task OffState_FillFromProfile_RaisesCallback()
    {
        var fired = false;
        var cut = Render<PersonaFillSummary>(parameters => parameters
            .Add(p => p.IsAutofillEnabled, false)
            .Add(p => p.Count, 0)
            .Add(p => p.PendingCount, 2)
            .Add(p => p.OnFillFromProfile, () => { fired = true; }));

        var btn = cut.FindAll("button").First(b => b.TextContent.Contains("Fill from profile"));
        await cut.InvokeAsync(() => btn.Click());
        fired.Should().BeTrue();
    }

    [Fact]
    public async Task Review_OpensPanelAndEmitsClearField()
    {
        string? clearedPath = null;
        var cut = Render<PersonaFillSummary>(parameters => parameters
            .Add(p => p.Count, 2)
            .Add(p => p.Fills, new[]
            {
                Fill("/email", "defaultEmail", "ada@example.com"),
                Fill("/firstName", "givenName", "Ada"),
            })
            .Add(p => p.OnClearField, (string path) => { clearedPath = path; }));

        var review = cut.FindAll("button").First(b => b.TextContent.Contains("Review"));
        await cut.InvokeAsync(() => review.Click());

        cut.FindAll(".persona-fill-review").Should().NotBeEmpty();
        var dialog = cut.Find(".persona-fill-review");
        dialog.GetAttribute("role").Should().Be("dialog");

        var rowClearButtons = cut.FindAll(".persona-fill-review button[aria-label^='Clear ']");
        rowClearButtons.Should().NotBeEmpty();
        await cut.InvokeAsync(() => rowClearButtons[0].Click());

        clearedPath.Should().NotBeNull();
    }
}
