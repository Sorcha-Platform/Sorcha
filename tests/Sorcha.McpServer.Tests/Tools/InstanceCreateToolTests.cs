// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.McpServer.Tools.Designer;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tests.Tools;

public class InstanceCreateToolTests
{
    private readonly Mock<IBlueprintServiceClient> _client = new();
    private readonly Mock<IMcpAuthorizationService> _auth = new();
    private readonly Mock<IServiceAvailabilityTracker> _availability = new();

    private InstanceCreateTool CreateSut()
    {
        _auth.Setup(a => a.CanInvokeTool("sorcha_instance_create")).Returns(true);
        _availability.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(true);
        return new InstanceCreateTool(
            _auth.Object, _availability.Object, _client.Object,
            NullLogger<InstanceCreateTool>.Instance);
    }

    // The wire shape here mirrors the REAL response: POST /api/instances/ serializes
    // Sorcha.Blueprint.Service.Models.Instance via Results.Created. There is no top-level
    // instanceReference field, and state carries as the enum's underlying int (no
    // JsonStringEnumConverter is registered for InstanceState) — see InstanceStateResolver.
    [Fact]
    public async Task CreateInstanceAsync_Success_ReturnsInstanceIdAndState()
    {
        _client.Setup(c => c.CreateInstanceAsync("bp-1", "reg-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                { "id": "inst-42", "blueprintId": "bp-1", "registerId": "reg-1",
                  "state": 0, "tenantId": "default",
                  "metadata": { "BlueprintTitle": "Construction Permit" } }
                """);

        var result = await CreateSut().CreateInstanceAsync("bp-1", "reg-1");

        result.Status.Should().Be("Success");
        result.InstanceId.Should().Be("inst-42");
        result.State.Should().Be("Active");
        // instanceReference is written by ActionExecutionService only after the first action is
        // submitted and folded — a freshly created instance's metadata never carries it yet.
        result.InstanceReference.Should().BeNull();
    }

    // instanceReference lives inside Metadata, not as a top-level field (CLAUDE.md's "Instance
    // Reference Configuration" pattern). This proves the tool reads it from the right place once
    // it IS present, without claiming the create endpoint itself ever populates it.
    [Fact]
    public async Task CreateInstanceAsync_MetadataCarriesInstanceReference_SurfacesIt()
    {
        _client.Setup(c => c.CreateInstanceAsync("bp-1", "reg-1", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("""
                { "id": "inst-42", "blueprintId": "bp-1", "registerId": "reg-1",
                  "state": 0, "metadata": { "instanceReference": "CP-RIV-14-A7K3" } }
                """);

        var result = await CreateSut().CreateInstanceAsync("bp-1", "reg-1");

        result.Status.Should().Be("Success");
        result.InstanceReference.Should().Be("CP-RIV-14-A7K3");
    }

    // The endpoint's one documented non-success shape for this route is a 409
    // "blueprint_not_available" — the blueprint hasn't finished replicating to this node yet. The
    // typed client collapses every non-success status to null (matching CreateBlueprintAsync's
    // convention), so the tool cannot distinguish 409 from a rarer permanent failure by status
    // code — instead it surfaces the transient/retryable framing in the message text, since
    // replication lag is this endpoint's one documented failure mode. This test asserts THAT
    // framing is actually present, not just that Status == "Error" — a prior draft of this test
    // asserted only the latter, which passes even if the message told the agent to give up.
    [Fact]
    public async Task CreateInstanceAsync_BlueprintNotReplicatedYet_SurfacesRetryableState()
    {
        _client.Setup(c => c.CreateInstanceAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await CreateSut().CreateInstanceAsync("bp-1", "reg-1");

        result.Status.Should().Be("Error");
        result.InstanceId.Should().BeNull();
        result.Message.Should().ContainEquivalentOf("retry");
    }

    [Fact]
    public async Task CreateInstanceAsync_NotEntitled_RefusesWithoutCallingTheService()
    {
        _auth.Setup(a => a.CanInvokeTool("sorcha_instance_create")).Returns(false);
        var sut = new InstanceCreateTool(
            _auth.Object, _availability.Object, _client.Object,
            NullLogger<InstanceCreateTool>.Instance);

        var result = await sut.CreateInstanceAsync("bp-1", "reg-1");

        result.Status.Should().Be("Unauthorized");
        _client.Verify(c => c.CreateInstanceAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateInstanceAsync_MissingBlueprintId_ReturnsErrorWithoutCallingTheService()
    {
        var result = await CreateSut().CreateInstanceAsync(" ", "reg-1");

        result.Status.Should().Be("Error");
        _client.Verify(c => c.CreateInstanceAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateInstanceAsync_ServiceUnavailable_ReturnsUnavailableWithoutCallingTheService()
    {
        _auth.Setup(a => a.CanInvokeTool("sorcha_instance_create")).Returns(true);
        _availability.Setup(a => a.IsServiceAvailable("Blueprint")).Returns(false);
        var sut = new InstanceCreateTool(
            _auth.Object, _availability.Object, _client.Object,
            NullLogger<InstanceCreateTool>.Instance);

        var result = await sut.CreateInstanceAsync("bp-1", "reg-1");

        result.Status.Should().Be("Unavailable");
        _client.Verify(c => c.CreateInstanceAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
