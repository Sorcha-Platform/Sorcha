// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Admin;
using Sorcha.ServiceClients.Haip;

namespace Sorcha.McpServer.Tests.Tools.Admin;

/// <summary>
/// Feature 140 Wave 2: PresentationRequestTool routes through the typed <see cref="IHaipServiceClient"/>.
/// </summary>
public class PresentationRequestToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IHaipServiceClient> _haipClientMock = new();

    private PresentationRequestTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _haipClientMock.Object,
        Mock.Of<ILogger<PresentationRequestTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_presentation_request")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task CreateRequest_Unauthorized_ReturnsUnauthorized()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_presentation_request")).Returns(false);

        var result = await CreateTool().CreateRequestAsync("IdCred");

        result.Status.Should().Be("Unauthorized");
        _haipClientMock.Verify(c => c.CreatePresentationRequestAsync(
            It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<List<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRequest_MissingType_ReturnsError()
    {
        Allow();

        var result = await CreateTool().CreateRequestAsync("");

        result.Status.Should().Be("Error");
    }

    [Fact]
    public async Task CreateRequest_InvalidClaimsJson_ReturnsError()
    {
        Allow();

        var result = await CreateTool().CreateRequestAsync("IdCred", requiredClaimsJson: "{bad");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("Invalid JSON");
    }

    [Fact]
    public async Task CreateRequest_Unavailable_ReturnsUnavailable()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_presentation_request")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await CreateTool().CreateRequestAsync("IdCred");

        result.Status.Should().Be("Unavailable");
    }

    [Fact]
    public async Task CreateRequest_Success_ReturnsRequest()
    {
        Allow();
        var requestId = Guid.NewGuid();
        _haipClientMock
            .Setup(c => c.CreatePresentationRequestAsync("IdCred",
                It.IsAny<List<string>>(), It.IsAny<List<string>>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatePresentationRequestResult(requestId, "openid4vp://authorize", "https://req", "nonce-1", DateTimeOffset.UtcNow.AddMinutes(5)));

        var result = await CreateTool().CreateRequestAsync("IdCred", "[\"givenName\"]", "[\"did:sorcha:issuer\"]");

        result.Status.Should().Be("Success");
        result.RequestId.Should().Be(requestId);
        result.AuthorizationRequestUri.Should().Be("openid4vp://authorize");
        result.Nonce.Should().Be("nonce-1");
    }
}
