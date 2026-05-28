// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using Moq;
using Sorcha.UI.Core.Models.Blueprints;
using Sorcha.UI.Core.Models.Common;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Designer;
using Sorcha.UI.Core.Services.Feedback;
using Sorcha.UI.Web.Client.Pages.DesignerShell;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.UI.Core.Tests.Components.Designer;

/// <summary>
/// Feature 142 (T055 / US6) — bUnit coverage for the <see cref="DesignerToolbar"/> Load wiring.
/// Verifies that clicking Load opens <c>LoadBlueprintDialog</c> through <see cref="IDialogService"/>,
/// that a returned blueprint id triggers <c>IBlueprintApiService.GetBlueprintDetailAsync</c>, and
/// that the loaded blueprint is adopted via <c>DesignerContext.SetBlueprint</c> (which clears
/// <c>PassedExecDefHash</c>, naturally re-locking Go live for a fresh draft / amend draft).
/// </summary>
public class DesignerToolbarLoadDialogTests : BunitContext
{
    private readonly Mock<IBlueprintApiService> _blueprintApi = new(MockBehavior.Strict);
    private readonly Mock<IDialogService> _dialogService = new(MockBehavior.Strict);
    private readonly Mock<IInlineFeedback> _feedback = new();
    private readonly DesignerContext _context = new();

    public DesignerToolbarLoadDialogTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(_blueprintApi.Object);
        Services.AddSingleton(_feedback.Object);
        Services.AddSingleton(_dialogService.Object);
        Services.AddSingleton(_context);
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static BlueprintModel SampleDraft(string id = "bp-amend-1", string title = "Amend Draft") => new()
    {
        Id = id,
        Title = title,
        Description = "Loaded via the toolbar.",
    };

    private static IDialogReference DialogReferenceReturning(DialogResult result)
    {
        var dialogRef = new Mock<IDialogReference>();
        dialogRef.SetupGet(d => d.Result).Returns(Task.FromResult<DialogResult?>(result));
        return dialogRef.Object;
    }

    /// <summary>
    /// Renders the toolbar alongside a sibling <see cref="MudPopoverProvider"/> so MudTextField
    /// / MudButton popovers do not blow up the bUnit renderer (mirrors LifecycleRailTests).
    /// </summary>
    private IRenderedComponent<DesignerToolbar> RenderToolbar()
    {
        var fragment = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<DesignerToolbar>(1);
            builder.CloseComponent();
        });
        return fragment.FindComponent<DesignerToolbar>();
    }

    [Fact]
    public async Task OnLoadClicked_DialogOkWithBlueprintId_FetchesDetailAndSetsContext()
    {
        // Arrange — dialog returns a selected blueprint id; the api returns the corresponding blueprint.
        const string selectedId = "bp-amend-42";
        var draft = SampleDraft(selectedId, "Amend Loop Draft");

        _dialogService
            .Setup(d => d.ShowAsync<Sorcha.UI.Core.Components.Designer.LoadBlueprintDialog>(
                It.IsAny<string>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(DialogReferenceReturning(DialogResult.Ok(selectedId)));

        _blueprintApi
            .Setup(a => a.GetBlueprintDetailAsync(selectedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(draft);

        var cut = RenderToolbar();

        // Act — find and click the Load button.
        var loadButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Load", StringComparison.OrdinalIgnoreCase));
        loadButton.Should().NotBeNull("the toolbar should render a Load button.");
        await loadButton!.ClickAsync(new());

        // Wait for the async OnLoadClicked to flush.
        await Task.Yield();

        // Assert — the dialog was opened, the blueprint was fetched, and the context adopted it.
        _dialogService.VerifyAll();
        _blueprintApi.VerifyAll();
        _context.Blueprint.Should().NotBeNull();
        _context.Blueprint!.Id.Should().Be(selectedId);
        _context.Blueprint.Title.Should().Be("Amend Loop Draft");
    }

    [Fact]
    public async Task OnLoadClicked_DialogCancelled_DoesNotTouchContext()
    {
        // Arrange — dialog cancelled; no api call should follow.
        _dialogService
            .Setup(d => d.ShowAsync<Sorcha.UI.Core.Components.Designer.LoadBlueprintDialog>(
                It.IsAny<string>(), It.IsAny<DialogOptions>()))
            .ReturnsAsync(DialogReferenceReturning(DialogResult.Cancel()));

        var cut = RenderToolbar();

        // Act
        var loadButton = cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Load", StringComparison.OrdinalIgnoreCase));
        loadButton.Should().NotBeNull();
        await loadButton!.ClickAsync(new());
        await Task.Yield();

        // Assert — context still empty, no api call attempted.
        _dialogService.VerifyAll();
        _blueprintApi.Verify(
            a => a.GetBlueprintDetailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _context.Blueprint.Should().BeNull();
    }
}
