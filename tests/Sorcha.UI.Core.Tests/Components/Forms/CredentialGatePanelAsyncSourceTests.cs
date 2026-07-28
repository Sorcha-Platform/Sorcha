// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.UI.Core.Components.Forms.Panels;
using Sorcha.UI.Core.Models.Credentials;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// #1330 — the PRE-submit <see cref="CredentialGatePanel"/> stops blocking form submission on
/// async-source (SorchaWallet/HAIP) requirements. Their selection is deliberately discarded (the
/// gate is actually enforced post-submit by the F111/F127 timebound presentation lifecycle), so
/// forcing a citizen through a Select dialog whose result is thrown away was pure friction. Only
/// <see cref="PresentationSource.SorchaInternal"/> requirements keep the inline Select/blocking
/// flow — this is the behaviour these tests pin.
/// </summary>
public sealed class CredentialGatePanelAsyncSourceTests : BunitContext
{
    private readonly Mock<ICredentialApiService> _api = new();

    public CredentialGatePanelAsyncSourceTests()
    {
        _api.Setup(a => a.MatchCredentialsAsync(It.IsAny<string>(),
                It.IsAny<List<CredentialRequirement>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CredentialMatchResult
                { RequirementType = "vct", Matched = true, CredentialId = "urn:uuid:c1" }]);

        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_api.Object);
        Services.AddSingleton(Mock.Of<MudBlazor.IDialogService>());
    }

    private IRenderedComponent<CredentialGatePanel> RenderPanel(PresentationSource source, FormContext formContext)
        => Render(builder =>
        {
            builder.OpenComponent<CascadingValue<FormContext>>(0);
            builder.AddAttribute(1, "Value", formContext);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenComponent<CredentialGatePanel>(0);
                inner.AddAttribute(1, "Requirements", new[]
                {
                    new CredentialRequirement { Type = "vct", PresentationSource = source }
                });
                inner.AddAttribute(2, "WalletAddress", "ws1q");
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }).FindComponent<CredentialGatePanel>();

    [Fact]
    public void AsyncSourceRequirement_DoesNotBlockSubmission()
    {
        var ctx = new FormContext { CredentialGateSatisfied = false }; // renderer's init for a gated action
        var cut = RenderPanel(PresentationSource.SorchaWallet, ctx);
        cut.WaitForAssertion(() => ctx.CredentialGateSatisfied.Should().BeTrue(
            "the gate for an async-source requirement is enforced post-submit by the presentation lifecycle"));
    }

    [Fact]
    public void AsyncSourceRequirement_RendersInfoNotSelectButton()
    {
        var cut = RenderPanel(PresentationSource.SorchaWallet, new FormContext());
        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("after you submit");
            cut.FindAll("button").Where(b => b.TextContent.Contains("Select")).Should().BeEmpty();
        });
    }

    [Fact]
    public void InternalSourceRequirement_StillRendersSelectFlow()
    {
        var ctx = new FormContext { CredentialGateSatisfied = false };
        var cut = RenderPanel(PresentationSource.SorchaInternal, ctx);
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("button").Where(b => b.TextContent.Contains("Select")).Should().HaveCount(1);
            ctx.CredentialGateSatisfied.Should().BeFalse("the inline path still requires a selection");
        });
    }
}
