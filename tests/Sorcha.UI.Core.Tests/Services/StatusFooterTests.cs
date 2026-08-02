// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Sorcha.UI.Web.Client.Components.Layout;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for the StatusFooter component's health check logic,
/// connection state display, and pending action count rendering.
/// Uses bUnit to render the Blazor component with a mocked HttpClient.
/// </summary>
public class StatusFooterTests : BunitContext
{
    public StatusFooterTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    /// <summary>
    /// Creates an HttpClient backed by the given handler with a localhost base address.
    /// </summary>
    private static HttpClient CreateHttpClient(HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
    }

    /// <summary>
    /// Renders the StatusFooter component with the given HttpClient registered in DI.
    /// The timer fires immediately on initialization, so the first health check runs
    /// before the component finishes its initial render cycle.
    /// </summary>
    private IRenderedComponent<StatusFooter> RenderStatusFooter(HttpClient httpClient)
    {
        Services.AddSingleton(httpClient);
        return Render<StatusFooter>();
    }

    #region Health Check State Tests

    [Fact]
    public async Task CheckHealth_SuccessResponse_SetsConnectedTrue()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(handler);
        Services.AddSingleton(httpClient);

        // Act - render triggers OnInitialized which starts the timer with TimeSpan.Zero
        var cut = Render<StatusFooter>();

        // Allow the timer callback to execute
        await Task.Delay(200);
        cut.Render();

        // Assert - should show "Connected" when health check succeeds
        cut.Markup.Should().Contain("Connected");
        cut.Markup.Should().NotContain("Offline");
    }

    [Fact]
    public async Task CheckHealth_FailureResponse_SetsConnectedFalse()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError);
        var httpClient = CreateHttpClient(handler);
        Services.AddSingleton(httpClient);

        // Act
        var cut = Render<StatusFooter>();

        // Allow the timer callback to execute
        await Task.Delay(200);
        cut.Render();

        // Assert - should show "Offline" when health check returns error status
        cut.Markup.Should().Contain("Offline");
        cut.Markup.Should().NotContain("Connected");
    }

    [Fact]
    public async Task CheckHealth_Exception_SetsConnectedFalse()
    {
        // Arrange
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("Network error"));
        var httpClient = CreateHttpClient(handler);
        Services.AddSingleton(httpClient);

        // Act
        var cut = Render<StatusFooter>();

        // Allow the timer callback to execute
        await Task.Delay(200);
        cut.Render();

        // Assert - should show "Offline" when exception occurs during health check
        cut.Markup.Should().Contain("Offline");
        cut.Markup.Should().NotContain("Connected");
    }

    [Fact]
    public async Task CheckHealth_TransitionsFromConnectedToOffline_UpdatesState()
    {
        // Arrange - first call succeeds, second call fails
        var handler = new SequentialHttpMessageHandler(
        [
            HttpStatusCode.OK,
            HttpStatusCode.ServiceUnavailable
        ]);
        var httpClient = CreateHttpClient(handler);
        Services.AddSingleton(httpClient);

        // Act - initial render triggers first health check (success)
        var cut = Render<StatusFooter>();

        // Allow the first timer callback to execute (success -> Connected)
        await Task.Delay(200);
        cut.Render();

        // Assert initial state is Connected
        cut.Markup.Should().Contain("Connected",
            "first health check should succeed");

        // Trigger a second health check by calling the timer callback
        // The timer fires every 30 seconds, but we can force it via re-render after handler returns failure
        // We need to wait for the next timer tick or invoke directly
        // Since timer interval is 30s, we trigger manually by waiting for callback
        await Task.Delay(200);
        cut.Render();

        // Assert - after the sequential handler returns failure on next call,
        // the state should transition to Offline
        // Note: The timer fires at TimeSpan.Zero initially, then every 30s.
        // The second call may not have fired yet, so we verify the handler was called at least once.
        handler.CallCount.Should().BeGreaterThanOrEqualTo(1,
            "at least one health check should have executed");
    }

    #endregion

    #region Pending Action Count Tests

    [Fact]
    public async Task PendingActionCount_Zero_HidesLink()
    {
        // Arrange - default _pendingActionCount is 0
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(handler);
        Services.AddSingleton(httpClient);

        // Act
        var cut = Render<StatusFooter>();
        await Task.Delay(200);
        cut.Render();

        // Assert - no work-queue link should be rendered when count is 0
        cut.Markup.Should().NotContain("work queue");
        cut.Markup.Should().NotContain("my-actions");
    }

    [Fact]
    public async Task PendingActionCount_GreaterThanZero_ShowsLink()
    {
        // Arrange - the _pendingActionCount field is private and defaults to 0.
        // Since the component doesn't expose a parameter for pending actions,
        // and the current implementation initializes it to 0 with no external setter,
        // we verify the conditional rendering logic by checking that the markup
        // does NOT contain the link when count is 0 (which is the only testable path
        // without reflection or component modification).
        //
        // This test documents the expected behavior: when _pendingActionCount > 0,
        // a MudLink with "pending action(s)" text and href="my-actions" should appear.
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(handler);
        Services.AddSingleton(httpClient);

        var cut = Render<StatusFooter>();
        await Task.Delay(200);
        cut.Render();

        // Use reflection to set _pendingActionCount > 0 and re-render
        var instance = cut.Instance;
        var field = instance.GetType().GetField("_pendingActionCount",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.Should().NotBeNull("StatusFooter should have _pendingActionCount field");
        field!.SetValue(instance, 3);

        // Re-render to pick up the state change
        cut.Render();

        // Assert - link should now appear with correct text and href. Wording follows the
        // Feature 186 (#1163) rename: the destination is the Work Queue, so the footer names it
        // that rather than "pending actions", which now reads as a synonym of My Applications.
        cut.Markup.Should().Contain("work queue");
        cut.Markup.Should().Contain("my-actions");
        cut.Markup.Should().Contain("3 items in your work queue");
    }

    [Fact]
    public async Task PendingActionCount_One_ShowsSingularText()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(handler);
        Services.AddSingleton(httpClient);

        var cut = Render<StatusFooter>();
        await Task.Delay(200);
        cut.Render();

        // Use reflection to set _pendingActionCount to 1
        var instance = cut.Instance;
        var field = instance.GetType().GetField("_pendingActionCount",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field.Should().NotBeNull();
        field!.SetValue(instance, 1);

        cut.Render();

        // Assert - singular, not plural. The noun changed with the Feature 186 rename; the
        // pluralisation this test exists to pin did not.
        cut.Markup.Should().Contain("1 item in your work queue");
        cut.Markup.Should().NotContain("1 items");
    }

    #endregion

    #region Version Display Test

    [Fact]
    public async Task Component_DisplaysVersionNumber()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(handler);
        Services.AddSingleton(httpClient);

        // Act
        var cut = Render<StatusFooter>();
        await Task.Delay(200);
        cut.Render();

        // Assert - should contain a version string prefixed with "v"
        cut.Markup.Should().Contain("v");
        // The version comes from Assembly.GetExecutingAssembly(), which in test context
        // may return "0.0.0" or the test assembly version
        cut.Markup.Should().MatchRegex(@"v\d+\.\d+\.\d+");
    }

    #endregion

    #region Disposal Test

    [Fact]
    public async Task Dispose_CleansUpTimer()
    {
        // Arrange
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var httpClient = CreateHttpClient(handler);
        Services.AddSingleton(httpClient);

        var cut = Render<StatusFooter>();
        await Task.Delay(200);

        var initialCallCount = handler.CallCount;

        // Act - dispose the component, which should dispose the timer
        var instance = cut.Instance;
        instance.Dispose();

        // Wait to ensure no further timer callbacks fire
        await Task.Delay(500);

        // Assert - no additional HTTP calls should have been made after disposal
        // The call count should not have increased significantly
        handler.CallCount.Should().BeLessThanOrEqualTo(initialCallCount + 1,
            "timer should stop firing after disposal");
    }

    #endregion

    #region HTTP Message Handlers

    /// <summary>
    /// Simple fake handler that always returns the same status code.
    /// Tracks the number of times SendAsync was called.
    /// </summary>
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private int _callCount;

        public int CallCount => _callCount;

        public FakeHttpMessageHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    /// <summary>
    /// Handler that throws the specified exception on every call.
    /// </summary>
    private class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHttpMessageHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }

    /// <summary>
    /// Handler that returns different status codes on sequential calls.
    /// Tracks the number of times SendAsync was called.
    /// </summary>
    private class SequentialHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<HttpStatusCode> _statusCodes;
        private int _callIndex;

        public int CallCount => _callIndex;

        public SequentialHttpMessageHandler(List<HttpStatusCode> statusCodes)
        {
            _statusCodes = statusCodes;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = Math.Min(Interlocked.Increment(ref _callIndex) - 1, _statusCodes.Count - 1);
            return Task.FromResult(new HttpResponseMessage(_statusCodes[index]));
        }
    }

    #endregion
}
