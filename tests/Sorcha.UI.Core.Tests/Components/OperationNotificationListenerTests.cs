// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MudBlazor;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Admin;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Authentication;
using Sorcha.UI.Core.Services.Configuration;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components;

/// <summary>
/// Tests for the OperationNotificationListener Blazor component (Feature 118 T114).
/// Verifies WalletHub subscription on Complete + Failed events, snackbar
/// notifications, and dispose behavior.
/// </summary>
public class OperationNotificationListenerTests : BunitContext
{
    private readonly Mock<ISnackbar> _snackbarMock;
    private readonly TestableWalletHubConnection _walletHub;

    public OperationNotificationListenerTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        _snackbarMock = new Mock<ISnackbar>();
        _snackbarMock.Setup(s => s.Configuration).Returns(new SnackbarConfiguration());
        _walletHub = new TestableWalletHubConnection();

        Services.AddSingleton<WalletHubConnection>(_walletHub);
        Services.AddSingleton<ISnackbar>(_snackbarMock.Object);
    }

    [Fact]
    public void OnInitialized_SubscribesToEncryptionCompleteAndFailed()
    {
        var cut = Render<OperationNotificationListener>();

        _walletHub.EncryptionCompleteHandlerCount.Should().Be(1);
        _walletHub.EncryptionFailedHandlerCount.Should().Be(1);
    }

    [Fact]
    public async Task OnEncryptionComplete_ShowsSuccessSnackbar()
    {
        var cut = Render<OperationNotificationListener>();
        var signal = new EncryptionSignal
        {
            OperationId = "op-success",
            PercentComplete = 100,
            Status = "complete",
            Timestamp = DateTimeOffset.UtcNow
        };

        await _walletHub.RaiseEncryptionCompleteAsync(signal);

        _snackbarMock.Verify(s => s.Add(
            It.Is<string>(msg => msg.Contains("Encryption complete")),
            Severity.Success,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task OnEncryptionFailed_ShowsErrorSnackbar()
    {
        var cut = Render<OperationNotificationListener>();
        var signal = new EncryptionSignal
        {
            OperationId = "op-fail",
            PercentComplete = 0,
            Status = "failed",
            Timestamp = DateTimeOffset.UtcNow
        };

        await _walletHub.RaiseEncryptionFailedAsync(signal);

        _snackbarMock.Verify(s => s.Add(
            It.Is<string>(msg => msg.Contains("Encryption failed")),
            Severity.Error,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void Dispose_UnsubscribesFromEncryptionEvents()
    {
        var cut = Render<OperationNotificationListener>();
        _walletHub.EncryptionCompleteHandlerCount.Should().Be(1);
        _walletHub.EncryptionFailedHandlerCount.Should().Be(1);

        Dispose();

        _walletHub.EncryptionCompleteHandlerCount.Should().Be(0);
        _walletHub.EncryptionFailedHandlerCount.Should().Be(0);
    }

    [Fact]
    public void DefaultState_NoErrors()
    {
        var cut = Render<OperationNotificationListener>();

        _snackbarMock.Verify(s => s.Add(
            It.IsAny<string>(),
            It.IsAny<Severity>(),
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Never);

        cut.Markup.Should().BeEmpty("listener component should not render any visible UI");
    }

    /// <summary>
    /// Testable subclass of WalletHubConnection that exposes event raising via reflection.
    /// </summary>
    private sealed class TestableWalletHubConnection : WalletHubConnection
    {
        public TestableWalletHubConnection()
            : base(
                "https://localhost",
                Mock.Of<IAuthenticationService>(),
                Mock.Of<IConfigurationService>(),
                NullLogger<WalletHubConnection>.Instance)
        {
        }

        public int EncryptionCompleteHandlerCount => GetHandlerCount(nameof(OnEncryptionComplete));
        public int EncryptionFailedHandlerCount => GetHandlerCount(nameof(OnEncryptionFailed));

        public Task RaiseEncryptionCompleteAsync(EncryptionSignal signal) =>
            InvokeAsync(nameof(OnEncryptionComplete), signal);

        public Task RaiseEncryptionFailedAsync(EncryptionSignal signal) =>
            InvokeAsync(nameof(OnEncryptionFailed), signal);

        private int GetHandlerCount(string eventName)
        {
            var field = typeof(WalletHubConnection).GetField(eventName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var handler = field?.GetValue(this) as Func<EncryptionSignal, Task>;
            return handler?.GetInvocationList().Length ?? 0;
        }

        private async Task InvokeAsync(string eventName, EncryptionSignal signal)
        {
            var field = typeof(WalletHubConnection).GetField(eventName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(this) is Func<EncryptionSignal, Task> handler)
            {
                await handler(signal);
            }
        }
    }
}
