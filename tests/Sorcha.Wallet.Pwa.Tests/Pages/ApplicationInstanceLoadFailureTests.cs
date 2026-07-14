// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Applications;
using Sorcha.Wallet.Pwa.Services.Drafts;
using Xunit;
using ApplicationInstancePage = Sorcha.Wallet.Pwa.Pages.ApplicationInstance;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// P0 fix (<c>fix/pwa-p0-claim-and-camera</c>) — <c>ApplicationInstance.razor</c> must only report
/// "offline" when the device is genuinely offline. Before this fix, <c>LoadFormAsync</c> collapsed
/// every failure (including a 403 from the citizen's own permission gate) into a bare <c>null</c>,
/// and the page treated any such failure as "offline" unconditionally — never consulting the
/// already-injected <see cref="IConnectivity"/>. These tests drive the page through the discriminated
/// <see cref="ApplicationFormLoadResult"/> outcomes and assert each lands on its own honest state.
/// </summary>
public sealed class ApplicationInstanceLoadFailureTests : ComponentTestFixture
{
    private readonly Mock<IApplicationActionClient> _actionClient = new();
    private readonly Mock<IPendingApplicationClient> _pendingApplications = new();
    private readonly Mock<IActionContextCache> _actionContextCache = new();
    private readonly Mock<IDraftStore> _draftStore = new();
    private readonly Mock<ISubmitQueue> _submitQueue = new();
    private readonly Mock<IFileChunkUploader> _fileUploader = new();
    private readonly Mock<IConnectivity> _connectivity = new();

    public ApplicationInstanceLoadFailureTests()
    {
        Services.AddSingleton(_actionClient.Object);
        Services.AddSingleton(_pendingApplications.Object);
        Services.AddSingleton(_actionContextCache.Object);
        Services.AddSingleton(_draftStore.Object);
        Services.AddSingleton(_submitQueue.Object);
        Services.AddSingleton(_fileUploader.Object);
        Services.AddSingleton(_connectivity.Object);
        Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ApplicationInstancePage>>(
            NullLogger<ApplicationInstancePage>.Instance);

        // No offline cache prepared for any of these scenarios — forces the page onto its
        // failure-state branch instead of a silently-successful cache hit.
        _actionContextCache
            .Setup(c => c.GetForInstanceAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sorcha.Wallet.Pwa.Services.Drafts.Models.CachedActionContext?)null);
    }

    [Fact]
    public void Forbidden_WhileOnline_RendersForbidden_NotOffline()
    {
        _actionClient.Setup(c => c.LoadFormAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationFormLoadResult.Forbidden);
        _connectivity.SetupGet(c => c.IsOnline).Returns(true);

        var cut = Render<ApplicationInstancePage>(ps => ps.Add(p => p.InstanceId, Guid.NewGuid()));

        cut.FindAll("[data-testid=application-instance-forbidden]").Should().ContainSingle();
        cut.FindAll("[data-testid=application-instance-offline]").Should().BeEmpty();
    }

    [Fact]
    public void Forbidden_WhileOffline_StillRendersForbidden_NotOffline()
    {
        // A 403 is a real server answer — the request reached the server. Even if the connectivity
        // flag is stale/false, the honest state is "forbidden", not "offline" (P0 fix's core claim).
        _actionClient.Setup(c => c.LoadFormAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationFormLoadResult.Forbidden);
        _connectivity.SetupGet(c => c.IsOnline).Returns(false);

        var cut = Render<ApplicationInstancePage>(ps => ps.Add(p => p.InstanceId, Guid.NewGuid()));

        cut.FindAll("[data-testid=application-instance-forbidden]").Should().ContainSingle();
        cut.FindAll("[data-testid=application-instance-offline]").Should().BeEmpty();
    }

    [Fact]
    public void NetworkError_WhileOffline_RendersOffline()
    {
        _actionClient.Setup(c => c.LoadFormAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationFormLoadResult.NetworkError);
        _connectivity.SetupGet(c => c.IsOnline).Returns(false);

        var cut = Render<ApplicationInstancePage>(ps => ps.Add(p => p.InstanceId, Guid.NewGuid()));

        cut.FindAll("[data-testid=application-instance-offline]").Should().ContainSingle();
    }

    [Fact]
    public void NetworkError_WhileOnline_RendersError_NotOffline()
    {
        // The deeper defect this fix closes: a genuine server-side problem (5xx, etc.) while the
        // device is online must never be fabricated into "you're offline".
        _actionClient.Setup(c => c.LoadFormAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationFormLoadResult.NetworkError);
        _connectivity.SetupGet(c => c.IsOnline).Returns(true);

        var cut = Render<ApplicationInstancePage>(ps => ps.Add(p => p.InstanceId, Guid.NewGuid()));

        cut.FindAll("[data-testid=application-instance-error]").Should().ContainSingle();
        cut.FindAll("[data-testid=application-instance-offline]").Should().BeEmpty();
    }

    [Fact]
    public void NotFound_WhileOnline_RendersError_NotOffline()
    {
        _actionClient.Setup(c => c.LoadFormAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplicationFormLoadResult.NotFound);
        _connectivity.SetupGet(c => c.IsOnline).Returns(true);

        var cut = Render<ApplicationInstancePage>(ps => ps.Add(p => p.InstanceId, Guid.NewGuid()));

        cut.FindAll("[data-testid=application-instance-error]").Should().ContainSingle();
        cut.FindAll("[data-testid=application-instance-offline]").Should().BeEmpty();
    }
}
