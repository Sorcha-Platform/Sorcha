// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MudBlazor;
using Sorcha.UI.Core.Components.Inbox;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Testing;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Inbox;

/// <summary>
/// bUnit tests for the inbox drawer. Guards the wallet-side title humanizer:
/// a server-composed title carrying a raw PascalCase VCT is rendered humanized,
/// never as the machine token.
/// </summary>
public sealed class InboxPanelTests : ComponentTestFixture
{
    private readonly Mock<IInboxApiService> _api = new();

    public InboxPanelTests()
    {
        Services.AddLogging();
        Services.AddSingleton(_api.Object);
        // TenantHubConnection is sealed but does not connect in its ctor — a real
        // instance is fine; InboxPanel only subscribes to its OnInboxEntryAdded event.
        Services.AddSingleton(new TenantHubConnection(
            "http://test.local",
            () => Task.FromResult<string?>(null),
            NullLogger<TenantHubConnection>.Instance,
            _api.Object));
    }

    private static InboxEntryDto Entry(string title) => new()
    {
        Id = Guid.NewGuid(),
        PlatformUserId = Guid.NewGuid(),
        Category = "Credential",
        Severity = "Info",
        CorrelationKey = "k",
        DetailHref = "",
        SourceEventId = Guid.NewGuid(),
        OccurredAt = DateTimeOffset.UtcNow,
        Title = title,
    };

    private RenderFragment OpenPanelWith(string title) => builder =>
    {
        _api.Setup(a => a.ListAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxPageDto(new List<InboxEntryDto> { Entry(title) }, 1, 50, 1));

        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<InboxPanel>(1);
        builder.AddAttribute(2, nameof(InboxPanel.IsOpen), true);
        builder.CloseComponent();
    };

    [Fact]
    public void RawVctInTitle_RendersHumanized_NeverPascalToken()
    {
        var cut = Render(OpenPanelWith("Acme Verification Co. issued you a AssuredIdentityCredential"));

        cut.Markup.Should().Contain("Assured Identity credential");
        cut.Markup.Should().NotContain("AssuredIdentityCredential");
    }

    [Fact]
    public void FriendlyTitle_PassesThroughUnchanged()
    {
        var cut = Render(OpenPanelWith("Your council application was approved"));

        cut.Markup.Should().Contain("Your council application was approved");
    }
}
