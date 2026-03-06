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
/// Tests for the OperationNotificationListener Blazor component.
/// Verifies EventsHub subscription, snackbar notifications, and dispose behavior.
/// </summary>
public class OperationNotificationListenerTests : BunitContext
{
    private readonly Mock<ISnackbar> _snackbarMock;
    private readonly TestableEventsHubConnection _eventsHub;

    public OperationNotificationListenerTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;

        _snackbarMock = new Mock<ISnackbar>();
        _snackbarMock.Setup(s => s.Configuration).Returns(new SnackbarConfiguration());
        _eventsHub = new TestableEventsHubConnection();

        Services.AddSingleton<EventsHubConnection>(_eventsHub);
        Services.AddSingleton<ISnackbar>(_snackbarMock.Object);
    }

    [Fact]
    public void OnInitialized_SubscribesToEncryptionOperationCompletedEvent()
    {
        // Act
        var cut = Render<OperationNotificationListener>();

        // Assert — component subscribed (handler count incremented)
        _eventsHub.EncryptionOperationCompletedHandlerCount.Should().Be(1);
    }

    [Fact]
    public void OnEncryptionOperationCompleted_Success_ShowsSuccessSnackbar()
    {
        // Arrange
        var cut = Render<OperationNotificationListener>();
        var dto = new EncryptionOperationCompletedDto
        {
            OperationId = "op-success",
            IsSuccess = true,
            TransactionHash = "abc123def456",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        _eventsHub.RaiseEncryptionOperationCompleted(dto);

        // Assert
        _snackbarMock.Verify(s => s.Add(
            It.Is<string>(msg => msg.Contains("Encryption completed") && msg.Contains("abc123def456")),
            Severity.Success,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void OnEncryptionOperationCompleted_Failure_ShowsErrorSnackbar()
    {
        // Arrange
        var cut = Render<OperationNotificationListener>();
        var dto = new EncryptionOperationCompletedDto
        {
            OperationId = "op-fail",
            IsSuccess = false,
            ErrorMessage = "P-256 key not available",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        _eventsHub.RaiseEncryptionOperationCompleted(dto);

        // Assert
        _snackbarMock.Verify(s => s.Add(
            It.Is<string>(msg => msg.Contains("Encryption failed") && msg.Contains("P-256 key not available")),
            Severity.Error,
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public void Dispose_UnsubscribesFromEncryptionOperationCompletedEvent()
    {
        // Arrange
        var cut = Render<OperationNotificationListener>();
        _eventsHub.EncryptionOperationCompletedHandlerCount.Should().Be(1);

        // Act — Disposing the BunitContext triggers IDisposable.Dispose on all rendered components
        Dispose();

        // Assert — handler was removed
        _eventsHub.EncryptionOperationCompletedHandlerCount.Should().Be(0);
    }

    [Fact]
    public void DefaultState_NoErrors()
    {
        // Act
        var cut = Render<OperationNotificationListener>();

        // Assert — no snackbar calls on initial render
        _snackbarMock.Verify(s => s.Add(
            It.IsAny<string>(),
            It.IsAny<Severity>(),
            It.IsAny<Action<SnackbarOptions>>(),
            It.IsAny<string>()), Times.Never);

        // Assert — component renders without throwing
        cut.Markup.Should().BeEmpty("listener component should not render any visible UI");
    }

    /// <summary>
    /// Testable subclass of EventsHubConnection that exposes event raising via reflection.
    /// </summary>
    private sealed class TestableEventsHubConnection : EventsHubConnection
    {
        public TestableEventsHubConnection()
            : base(
                "https://localhost",
                Mock.Of<IAuthenticationService>(),
                Mock.Of<IConfigurationService>(),
                NullLogger<EventsHubConnection>.Instance)
        {
        }

        /// <summary>
        /// Gets the current number of subscribed handlers via reflection.
        /// </summary>
        public int EncryptionOperationCompletedHandlerCount => GetHandlerCount();

        private int GetHandlerCount()
        {
            var eventField = typeof(EventsHubConnection)
                .GetField(nameof(OnEncryptionOperationCompleted), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var handler = eventField?.GetValue(this) as Action<EncryptionOperationCompletedDto>;
            if (handler == null) return 0;
            return handler.GetInvocationList().Length;
        }

        /// <summary>
        /// Raises the OnEncryptionOperationCompleted event on the base class via reflection.
        /// </summary>
        public void RaiseEncryptionOperationCompleted(EncryptionOperationCompletedDto dto)
        {
            var field = typeof(EventsHubConnection)
                .GetField(nameof(OnEncryptionOperationCompleted), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var handler = field?.GetValue(this) as Action<EncryptionOperationCompletedDto>;
            handler?.Invoke(dto);
        }
    }
}
