// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Blueprint.Models;
using Sorcha.UI.Core.Components.Blueprints;
using Sorcha.UI.Core.Models.Blueprints;
using Sorcha.UI.Core.Models.Registers;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Designer;
using Sorcha.UI.Core.Services.Feedback;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.UI.Core.Tests.Components.Blueprints;

/// <summary>
/// bUnit tests for the Feature 142 <see cref="GoLivePanel"/> Go-live stage component (T036/T040):
/// the register dropdown excludes sandbox registers and distinguishes no-rights ones (FR-025), the
/// system-info card renders from <see cref="IRegisterSystemInfoService"/> (FR-026), Publish is gated
/// on <see cref="LifecycleState.RehearsalPassedForCurrentExecDef"/> (FR-004), and a 409 outcome
/// surfaces the override affordance (FR-032).
/// </summary>
public class GoLivePanelTests : BunitContext
{
    private readonly Mock<IBlueprintApiService> _blueprintApi = new();
    private readonly Mock<IRegisterReadService> _registerRead = new();
    private readonly Mock<IRegisterSubscriptionService> _subscriptions = new();
    private readonly Mock<IRegisterSystemInfoService> _systemInfo = new();
    private readonly DesignerContext _context = new();

    public GoLivePanelTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(_blueprintApi.Object);
        Services.AddSingleton(_registerRead.Object);
        Services.AddSingleton(_subscriptions.Object);
        Services.AddSingleton(_systemInfo.Object);
        Services.AddSingleton<IInlineFeedback, InlineFeedback>();
        Services.AddSingleton(_context);
    }

    private static BlueprintModel ValidBlueprint() => new()
    {
        Id = "bp-golive-1",
        Title = "Grant Application",
        Description = "Apply for a grant.",
        Version = 3,
    };

    private static RegisterViewModel Register(string id, string name, bool sandbox = false, bool online = true) => new()
    {
        Id = id,
        Name = name,
        Sandbox = sandbox,
        Status = online ? RegisterStatus.Online : RegisterStatus.Offline,
    };

    private static RegisterSubscriptionDto Subscription(string id) => new()
    {
        RegisterId = id,
        SubscriptionType = "Owner",
        Status = "Active",
    };

    private static RegisterSystemInfoViewModel SystemInfo(string id, string name, RegisterGovernanceRole role) => new()
    {
        RegisterId = id,
        Name = name,
        OwnershipAvailable = true,
        IsLocallyOwned = true,
        ValidationAvailable = true,
        ValidatorCount = 2,
        RequiredSignatures = 2,
        IsPublic = true,
        SyncStateAvailable = true,
        SyncStateText = "Caught up",
        DevMode = false,
        PublishedServiceCount = 4,
        CallerRole = role,
    };

    /// <summary>Wires the register list (subscriptions ∩ all) and the per-register system-info responses.</summary>
    private void SetupRegisters(params (RegisterViewModel register, RegisterGovernanceRole role)[] registers)
    {
        _subscriptions.Setup(s => s.GetMySubscribedRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(registers.Select(r => Subscription(r.register.Id)).ToList());
        _registerRead.Setup(r => r.GetRegistersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(registers.Select(r => r.register).ToList());
        foreach (var (register, role) in registers)
        {
            _systemInfo.Setup(s => s.GetSystemInfoAsync(register.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(SystemInfo(register.Id, register.Name, role));
        }
    }

    /// <summary>
    /// Renders the panel alongside a <see cref="MudPopoverProvider"/> sibling — MudSelect renders its
    /// options through a popover that requires the provider in the render tree (mirrors LifecycleRailTests).
    /// </summary>
    private IRenderedComponent<Bunit.Rendering.ContainerFragment> RenderPanel() => Render(builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<GoLivePanel>(1);
        builder.CloseComponent();
    });

    /// <summary>Drives the register selection through the MudSelect value-changed callback and waits for the card.</summary>
    private void SelectRegister(IRenderedComponent<Bunit.Rendering.ContainerFragment> cut, string registerId)
    {
        var select = cut.FindComponent<MudSelect<string>>();
        cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(registerId));
        cut.WaitForState(() => cut.FindAll("[data-testid='golive-systeminfo-card']").Count == 1);
    }

    /// <summary>Sandbox registers are absent from the picker; live ones are present (FR-025/SC-008).</summary>
    [Fact]
    public void RegisterDropdown_ExcludesSandboxRegisters()
    {
        _context.SetBlueprint(ValidBlueprint());
        SetupRegisters(
            (Register("live-1", "Live Register", sandbox: false), RegisterGovernanceRole.Owner),
            (Register("sandbox-1", "Sandbox Register", sandbox: true), RegisterGovernanceRole.Owner));

        var cut = RenderPanel();

        // The picker offers exactly one option — the live register; the sandbox register is filtered out.
        var items = cut.FindComponents<MudSelectItem<string>>();
        items.Should().ContainSingle("only the non-sandbox live register is a candidate");
        items[0].Instance.Value.Should().Be("live-1");
    }

    /// <summary>With no eligible registers, the helpful empty state shows instead of an empty picker.</summary>
    [Fact]
    public void RegisterDropdown_NoEligibleRegisters_ShowsEmptyState()
    {
        _context.SetBlueprint(ValidBlueprint());
        SetupRegisters((Register("sandbox-1", "Sandbox Register", sandbox: true), RegisterGovernanceRole.Owner));

        var cut = RenderPanel();

        cut.FindAll("[data-testid='golive-no-eligible-registers']").Should().HaveCount(1);
        cut.FindAll("[data-testid='golive-register-select']").Should().BeEmpty();
    }

    /// <summary>Selecting a register renders the system-info card with the aggregated fields (FR-026).</summary>
    [Fact]
    public void SelectingRegister_RendersSystemInfoCard()
    {
        _context.SetBlueprint(ValidBlueprint());
        SetupRegisters((Register("live-1", "Live Register"), RegisterGovernanceRole.Admin));

        var cut = RenderPanel();
        SelectRegister(cut, "live-1");

        var card = cut.Find("[data-testid='golive-systeminfo-card']");
        card.TextContent.Should().Contain("Live Register");
        card.TextContent.Should().Contain("validator");
        card.TextContent.Should().Contain("Public");
        card.TextContent.Should().Contain("Admin");
    }

    /// <summary>Publish is disabled until a rehearsal has passed for the current exec def (FR-004).</summary>
    [Fact]
    public void Publish_DisabledUntilRehearsalPassed()
    {
        _context.SetBlueprint(ValidBlueprint());
        SetupRegisters((Register("live-1", "Live Register"), RegisterGovernanceRole.Owner));

        var cut = RenderPanel();
        SelectRegister(cut, "live-1");

        cut.Find("[data-testid='golive-publish-btn']").HasAttribute("disabled")
            .Should().BeTrue("Publish must stay disabled until the current version is rehearsed");

        // Once a rehearsal passes for the current exec def, the Context.Changed subscription re-renders
        // the panel and Publish enables.
        cut.InvokeAsync(() => _context.RecordRehearsalPassed());
        cut.WaitForState(() => !cut.Find("[data-testid='golive-publish-btn']").HasAttribute("disabled"));

        cut.Find("[data-testid='golive-publish-btn']").HasAttribute("disabled")
            .Should().BeFalse("a passing rehearsal + a rights-bearing register unlocks Publish");
    }

    /// <summary>A no-rights register is distinguished and keeps Publish disabled (FR-025/FR-027).</summary>
    [Fact]
    public void NoRightsRegister_IsDistinguished_AndPublishStaysDisabled()
    {
        _context.SetBlueprint(ValidBlueprint());
        _context.RecordRehearsalPassed();
        SetupRegisters((Register("live-1", "Live Register"), RegisterGovernanceRole.None));

        var cut = RenderPanel();
        SelectRegister(cut, "live-1");

        cut.FindAll("[data-testid='golive-no-rights']").Should().NotBeEmpty(
            "a register the caller cannot publish to must be visibly distinguished");
        cut.Find("[data-testid='golive-publish-btn']").HasAttribute("disabled")
            .Should().BeTrue("a no-rights register must not be publishable even after a passing rehearsal");
    }

    /// <summary>A 409 REHEARSAL_REQUIRED outcome surfaces the override confirmation affordance (FR-032).</summary>
    [Fact]
    public void Publish_409RehearsalRequired_SurfacesOverrideAffordance()
    {
        _context.SetBlueprint(ValidBlueprint());
        _context.RecordRehearsalPassed();
        SetupRegisters((Register("live-1", "Live Register"), RegisterGovernanceRole.Owner));

        _blueprintApi.Setup(b => b.PublishGoLiveAsync(
                "bp-golive-1", "live-1", false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoLivePublishOutcome.NeedsRehearsal(new RehearsalRequiredError
            {
                ExecDefHash = "abc123",
                Message = "Version not rehearsed."
            }));

        var cut = RenderPanel();
        SelectRegister(cut, "live-1");

        cut.Find("[data-testid='golive-publish-btn']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='golive-override-confirm']").Count == 1);

        cut.FindAll("[data-testid='golive-rehearsal-required']").Should().HaveCount(1);
        cut.FindAll("[data-testid='golive-override-confirm']").Should().HaveCount(1);
    }

    /// <summary>A successful publish reflects the service as live with its version (FR-028).</summary>
    [Fact]
    public void Publish_Success_ReflectsLiveWithVersion()
    {
        _context.SetBlueprint(ValidBlueprint());
        _context.RecordRehearsalPassed();
        SetupRegisters((Register("live-1", "Live Register"), RegisterGovernanceRole.Owner));

        _blueprintApi.Setup(b => b.PublishGoLiveAsync(
                "bp-golive-1", "live-1", false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoLivePublishOutcome.Published(new PublishBlueprintResult
            {
                BlueprintId = "bp-golive-1",
                Version = 3,
                RegisterId = "live-1",
                Overridden = false,
            }));

        var cut = RenderPanel();
        SelectRegister(cut, "live-1");

        cut.Find("[data-testid='golive-publish-btn']").Click();
        cut.WaitForState(() => cut.FindAll("[data-testid='golive-published']").Count == 1);

        cut.Find("[data-testid='golive-published']").TextContent.Should().Contain("v3");
    }
}
