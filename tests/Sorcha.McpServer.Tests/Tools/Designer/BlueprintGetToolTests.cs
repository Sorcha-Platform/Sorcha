// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Designer;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tests.Tools.Designer;

/// <summary>
/// Spec 139 US4: BlueprintGetTool reads via the typed <see cref="IBlueprintServiceClient"/>
/// (route pinned, caller token forwarded), so these tests mock the client rather than HTTP.
/// </summary>
public class BlueprintGetToolTests
{
    private readonly Mock<IMcpAuthorizationService> _authServiceMock = new();
    private readonly Mock<IServiceAvailabilityTracker> _availabilityTrackerMock = new();
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();

    private BlueprintGetTool CreateTool() => new(
        _authServiceMock.Object,
        _availabilityTrackerMock.Object,
        _blueprintClientMock.Object,
        Mock.Of<ILogger<BlueprintGetTool>>());

    private void Allow()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_get")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
    }

    [Fact]
    public async Task GetBlueprintAsync_Unauthorized_ReturnsUnauthorizedResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_get")).Returns(false);

        var result = await CreateTool().GetBlueprintAsync("bp-123");

        result.Status.Should().Be("Unauthorized");
        result.Message.Should().Contain("Access denied");
        result.Blueprint.Should().BeNull();
    }

    [Fact]
    public async Task GetBlueprintAsync_ServiceUnavailable_ReturnsUnavailableResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_get")).Returns(true);
        _availabilityTrackerMock.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);

        var result = await CreateTool().GetBlueprintAsync("bp-123");

        result.Status.Should().Be("Unavailable");
        result.Message.Should().Contain("unavailable");
    }

    [Fact]
    public async Task GetBlueprintAsync_EmptyBlueprintId_ReturnsErrorResult()
    {
        _authServiceMock.Setup(a => a.CanInvokeTool("sorcha_blueprint_get")).Returns(true);

        var result = await CreateTool().GetBlueprintAsync("");

        result.Status.Should().Be("Error");
        result.Message.Should().Contain("required");
    }

    [Fact]
    public async Task GetBlueprintAsync_BlueprintExists_ReturnsSuccessResult()
    {
        Allow();

        var blueprintJson = JsonSerializer.Serialize(new
        {
            Id = "bp-123",
            Title = "Test Blueprint",
            Description = "A test blueprint for unit testing",
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-5),
            Metadata = new Dictionary<string, string> { ["author"] = "test-user" },
            Participants = new[]
            {
                new { Id = "participant-1", Name = "Applicant", Organisation = "Test Org", WalletAddress = "0xabc123", DidUri = (string?)"did:example:123", UseStealthAddress = false },
                new { Id = "participant-2", Name = "Approver", Organisation = "Test Org", WalletAddress = "0xdef456", DidUri = (string?)null, UseStealthAddress = true }
            },
            Actions = new[]
            {
                new { Id = 0, Title = "Submit Application", Description = "Submit the initial application", Sender = "participant-1", Target = (string?)"participant-2", IsStartingAction = true, RequiredActionData = new[] { "form-data" }, AdditionalRecipients = Array.Empty<string>(), Disclosures = new object[] { new { }, new { } } },
                new { Id = 1, Title = "Review Application", Description = "Review and approve or reject", Sender = "participant-2", Target = (string?)null, IsStartingAction = false, RequiredActionData = Array.Empty<string>(), AdditionalRecipients = new[] { "participant-1" }, Disclosures = new object[] { } }
            }
        });

        _blueprintClientMock
            .Setup(c => c.GetBlueprintAsync("bp-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(blueprintJson);

        var result = await CreateTool().GetBlueprintAsync("bp-123");

        result.Status.Should().Be("Success");
        result.Message.Should().Contain("Test Blueprint");
        result.Blueprint.Should().NotBeNull();
        result.Blueprint!.Id.Should().Be("bp-123");
        result.Blueprint.Participants.Should().HaveCount(2);
        result.Blueprint.Participants[1].UseStealthAddress.Should().BeTrue();
        result.Blueprint.Actions.Should().HaveCount(2);
        result.Blueprint.Actions[0].IsStartingAction.Should().BeTrue();
        result.Blueprint.Actions[0].DisclosureCount.Should().Be(2);
        result.Blueprint.Actions[1].AdditionalRecipients.Should().Contain("participant-1");
        result.Blueprint.Metadata.Should().ContainKey("author");

        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task GetBlueprintAsync_ClientReturnsNull_ReturnsNotFoundResult()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintAsync("nonexistent-bp", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateTool().GetBlueprintAsync("nonexistent-bp");

        result.Status.Should().Be("NotFound");
        result.Message.Should().Contain("nonexistent-bp");
        result.Blueprint.Should().BeNull();
        _availabilityTrackerMock.Verify(a => a.RecordSuccess("Blueprint"), Times.Once);
    }

    [Fact]
    public async Task GetBlueprintAsync_InvalidJson_ReturnsNotFoundResult()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintAsync("bp-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync("invalid json");

        var result = await CreateTool().GetBlueprintAsync("bp-123");

        result.Status.Should().Be("NotFound");
        result.Blueprint.Should().BeNull();
    }

    [Fact]
    public async Task GetBlueprintAsync_ResponseTimeIsRecorded()
    {
        Allow();
        _blueprintClientMock
            .Setup(c => c.GetBlueprintAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new { Id = "bp-123", Title = "Test", Description = "Test", Version = 1, Participants = Array.Empty<object>(), Actions = Array.Empty<object>() }));

        var result = await CreateTool().GetBlueprintAsync("bp-123");

        result.ResponseTimeMs.Should().BeGreaterThanOrEqualTo(0);
        result.CheckedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
