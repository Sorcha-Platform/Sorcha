// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.Tenant.Models.Persona;
using Sorcha.UI.Core.Components.Persona;
using Sorcha.UI.Core.Services.Feedback;
using Sorcha.UI.Core.Services.Persona;
using Sorcha.UI.Testing;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Persona;

/// <summary>
/// bUnit tests for the shared <see cref="PersonaEditor"/> component.
/// Covers load (T004), save-success (T005), validation rejection (T010),
/// wallet-not-provisioned rejection (T011), and network failure (T012).
/// </summary>
public sealed class PersonaEditorTests : ComponentTestFixture
{
    private readonly Mock<IPersonaService> _personaService;
    private readonly Mock<IInlineFeedback> _feedback;

    public PersonaEditorTests()
    {
        Services.AddLogging();

        _personaService = ProvideMock<IPersonaService>();
        _feedback = ProvideMock<IInlineFeedback>();

        // Defaults — overridden per test where needed.
        _personaService
            .Setup(s => s.GetAutofillEnabledAsync())
            .ReturnsAsync(true);
        _personaService
            .Setup(s => s.GetAsync(It.IsAny<PersonaReadOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonaReadModelV1());
    }

    // ---- T004: Load tests ----

    [Fact]
    public void Load_WithExistingPersona_PopulatesFormFields()
    {
        var persona = new PersonaReadModelV1
        {
            GivenName = Attr("Alice"),
            FamilyName = Attr("Smith"),
            AllEmails = [new PersonaEmail("alice@example.com", IsDefault: true)],
        };

        _personaService
            .Setup(s => s.GetAsync(It.IsAny<PersonaReadOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(persona);

        var cut = Render(EditorUnderPopover());

        cut.Markup.Should().Contain("Alice", because: "given name should be populated");
        cut.Markup.Should().Contain("Smith", because: "family name should be populated");
        cut.Markup.Should().Contain("alice@example.com", because: "email should be populated");
    }

    [Fact]
    public void Load_NoPersona_ShowsEmptyEditableForm()
    {
        _personaService
            .Setup(s => s.GetAsync(It.IsAny<PersonaReadOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PersonaReadModelV1());

        var cut = Render(EditorUnderPopover());

        cut.Markup.Should().Contain("Save profile", because: "save button is visible once loading completes");
        cut.Markup.Should().NotContain("mud-progress-linear", because: "loading spinner should be hidden");
        cut.Markup.Should().NotContain("We couldn't load", because: "no load error for empty persona");
    }

    // ---- T005: Save success test ----

    [Fact]
    public async Task Save_ValidInput_CallsUpdateAsync_ShowsSuccessAndRebinds()
    {
        var returned = new PersonaReadModelV1
        {
            GivenName = Attr("Bob"),
        };

        _personaService
            .Setup(s => s.UpdateAsync(It.IsAny<PersonaAttributesV1>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returned);
        _personaService
            .Setup(s => s.SetAutofillEnabledAsync(It.IsAny<bool>()))
            .Returns(Task.CompletedTask);

        var cut = Render(EditorUnderPopover());

        var saveButton = cut.FindAll("button")
            .First(b => b.TextContent.Contains("Save profile"));

        await cut.InvokeAsync(() => saveButton.Click());

        _personaService.Verify(
            s => s.UpdateAsync(It.IsAny<PersonaAttributesV1>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _feedback.Verify(
            f => f.ShowSuccess("Profile saved.", It.IsAny<string?>(), It.IsAny<int?>()),
            Times.Once);
    }

    // ---- T010: Validation rejection test ----

    [Fact]
    public async Task Save_PersonaValidationException_ShowsInlineErrors_PreservesInput()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["email"] = ["Invalid email format"],
        };

        _personaService
            .Setup(s => s.UpdateAsync(It.IsAny<PersonaAttributesV1>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PersonaValidationException(errors));

        var cut = Render(EditorUnderPopover());

        var saveButton = cut.FindAll("button")
            .First(b => b.TextContent.Contains("Save profile"));

        await cut.InvokeAsync(() => saveButton.Click());

        // Error is shown with autoDismissMs: 0 (must acknowledge)
        _feedback.Verify(
            f => f.ShowError(
                It.Is<string>(m => m.Contains("email") || m.Contains("Invalid")),
                It.IsAny<string?>(),
                0),
            Times.AtLeastOnce);

        // Form fields are still visible — input preserved
        cut.Markup.Should().Contain("Save profile",
            because: "form remains visible so the citizen can correct errors");
    }

    // ---- T011: Wallet-not-provisioned rejection test ----

    [Fact]
    public async Task Save_WalletNotProvisionedException_ShowsDistinctProvisioningMessage()
    {
        _personaService
            .Setup(s => s.UpdateAsync(It.IsAny<PersonaAttributesV1>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PersonaWalletNotProvisionedException());

        var cut = Render(EditorUnderPopover());

        var saveButton = cut.FindAll("button")
            .First(b => b.TextContent.Contains("Save profile"));

        await cut.InvokeAsync(() => saveButton.Click());

        // Distinct provisioning message — not a generic error
        _feedback.Verify(
            f => f.ShowWarning(
                It.Is<string>(m => m.Contains("wallet")),
                It.IsAny<string?>(),
                It.IsAny<int?>()),
            Times.Once);

        // Generic "Couldn't save" must NOT appear
        _feedback.Verify(
            f => f.ShowError(
                It.Is<string>(m => m.Contains("Couldn't save")),
                It.IsAny<string?>(),
                It.IsAny<int?>()),
            Times.Never);
    }

    // ---- T012: Network failure test ----

    [Fact]
    public async Task Save_NetworkFailure_ShowsRetryMessage_PreservesEnteredData()
    {
        _personaService
            .Setup(s => s.UpdateAsync(It.IsAny<PersonaAttributesV1>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("Network unavailable"));

        var cut = Render(EditorUnderPopover());

        var saveButton = cut.FindAll("button")
            .First(b => b.TextContent.Contains("Save profile"));

        await cut.InvokeAsync(() => saveButton.Click());

        // Generic retry message shown as a non-auto-dismissing error
        _feedback.Verify(
            f => f.ShowError(
                It.Is<string>(m => m.Contains("Couldn't save")),
                It.IsAny<string?>(),
                0),
            Times.Once);

        // Form is still visible — entered data preserved
        cut.Markup.Should().Contain("Save profile",
            because: "form remains visible so the citizen can retry");
    }

    // ---- Helpers ----

    private static PersonaAttribute<string> Attr(string value) =>
        new(value, PersonaAttributeSource.SelfAsserted, null, DateTimeOffset.UtcNow);

    private static Microsoft.AspNetCore.Components.RenderFragment EditorUnderPopover() => builder =>
    {
        builder.OpenComponent<MudBlazor.MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<PersonaEditor>(1);
        builder.CloseComponent();
    };
}
