// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MudBlazor.Services;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Credentials;
using Sorcha.UI.Web.Client.Components.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Credentials;

/// <summary>
/// bUnit tests for <see cref="PresentationRequestQrCard"/> error/retry state.
/// Covers: transport-error outcome stops polling, error alert renders, Retry button present,
/// and Retry click restarts polling.
/// </summary>
public sealed class PresentationRequestQrCardTests : BunitContext
{
    private readonly Mock<IHaipOfferService> _haipServiceMock = new();
    private readonly Mock<IQrPresentationService> _qrServiceMock = new();
    private readonly Guid _requestId = Guid.NewGuid();

    public PresentationRequestQrCardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton(_haipServiceMock.Object);
        Services.AddSingleton(_qrServiceMock.Object);
        Services.AddSingleton(Mock.Of<ILogger<PresentationRequestQrCard>>());

        _qrServiceMock
            .Setup(q => q.GenerateSvgFromUri(It.IsAny<string>(), It.IsAny<int>()))
            .Returns("<svg data-testid=\"stub-qr\"/>");
    }

    private IRenderedComponent<PresentationRequestQrCard> RenderCard()
    {
        return Render<PresentationRequestQrCard>(p => p
            .Add(c => c.RequestId, _requestId)
            .Add(c => c.PresentationRequestUri, "openid4vp://authorize?request_uri=test")
            .Add(c => c.CredentialType, "VerifiableCredential"));
    }

    [Fact]
    public async Task TransportError_StopsPolling_AndShowsErrorAlert()
    {
        _haipServiceMock
            .Setup(s => s.GetVerificationResultAsync(_requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationPollOutcome
            {
                IsTransportError = true,
                ErrorMessage = "A server error occurred. Please try again."
            });

        var cut = RenderCard();

        // First poll tick fires after PollInterval (2s). WaitForAssertion polls until the condition
        // is met (up to the timeout).
        await cut.WaitForAssertionAsync(() =>
        {
            cut.Markup.Should().Contain("server error", because: "error alert must render the transport error message");
            cut.Markup.Should().Contain("Retry", because: "Retry button must be visible after a transport error");
        }, timeout: TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task TransportError_PollingStops_ServiceNotCalledAgain()
    {
        var callCount = 0;
        _haipServiceMock
            .Setup(s => s.GetVerificationResultAsync(_requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return new VerificationPollOutcome
                {
                    IsTransportError = true,
                    ErrorMessage = "Error"
                };
            });

        var cut = RenderCard();

        await cut.WaitForAssertionAsync(() =>
        {
            cut.Markup.Should().Contain("Retry");
        }, timeout: TimeSpan.FromSeconds(6));

        var countAfterError = callCount;
        // Wait another poll interval to confirm the loop stopped.
        await Task.Delay(TimeSpan.FromSeconds(3));
        callCount.Should().Be(countAfterError, because: "polling loop must stop after a transport error");
    }

    [Fact]
    public async Task RetryButton_Click_RestartsPolling()
    {
        var callCount = 0;
        _haipServiceMock
            .Setup(s => s.GetVerificationResultAsync(_requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new VerificationPollOutcome { IsTransportError = true, ErrorMessage = "Error" }
                    : new VerificationPollOutcome { Result = null, IsTransportError = false };
            });

        var cut = RenderCard();

        // Wait for error state.
        await cut.WaitForAssertionAsync(() =>
        {
            cut.Markup.Should().Contain("Retry");
        }, timeout: TimeSpan.FromSeconds(6));

        // Click the Retry button.
        cut.Find("button[class*=mud-button]").Click();

        // After retry, error alert should disappear as polling resumes.
        await cut.WaitForAssertionAsync(() =>
        {
            cut.Markup.Should().NotContain("Retry", because: "after clicking Retry the error state should clear");
        }, timeout: TimeSpan.FromSeconds(6));
    }
}
