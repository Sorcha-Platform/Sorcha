// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Resources;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Register;

namespace Sorcha.McpServer.Tests.Resources;

/// <summary>
/// <c>sorcha://instances</c> and <c>sorcha://registers</c> — caller-scoped live state, read
/// instead of spending a tool call. Both must degrade to a note-carrying empty JSON body rather
/// than throwing, since an MCP resource read has no equivalent of a tool's structured error
/// result — an unhandled exception here would surface as a raw protocol error, not a useful
/// "not authenticated" / "service unavailable" message.
/// </summary>
public class LiveStateResourcesTests
{
    private readonly Mock<IBlueprintServiceClient> _blueprintClientMock = new();
    private readonly Mock<IRegisterServiceClient> _registerClientMock = new();
    private readonly Mock<ICallerContext> _callerMock = new();

    private LiveStateResources CreateSut() => new(
        _blueprintClientMock.Object,
        _registerClientMock.Object,
        _callerMock.Object,
        Mock.Of<ILogger<LiveStateResources>>());

    private void Authenticate() => _callerMock.SetupGet(c => c.IsAuthenticated).Returns(true);

    [Fact]
    public async Task InstancesAsync_Unauthenticated_ReturnsNoteWithoutCallingBackend()
    {
        _callerMock.SetupGet(c => c.IsAuthenticated).Returns(false);

        var body = await CreateSut().InstancesAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("instances").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();
        _blueprintClientMock.Verify(
            c => c.GetWorkflowInstancesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InstancesAsync_Authenticated_ReturnsTheRawBodyVerbatim()
    {
        Authenticate();
        const string rawBody = """{"items":[{"id":"wf-1"}],"totalCount":1,"pageNumber":1,"pageSize":20}""";
        _blueprintClientMock
            .Setup(c => c.GetWorkflowInstancesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawBody);

        var body = await CreateSut().InstancesAsync(CancellationToken.None);

        body.Should().Be(rawBody);
    }

    [Fact]
    public async Task InstancesAsync_ServiceReturnsNull_ReturnsNote()
    {
        Authenticate();
        _blueprintClientMock
            .Setup(c => c.GetWorkflowInstancesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var body = await CreateSut().InstancesAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("instances").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task InstancesAsync_BackendThrows_ReturnsNoteInsteadOfThrowing()
    {
        Authenticate();
        _blueprintClientMock
            .Setup(c => c.GetWorkflowInstancesAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var body = await CreateSut().InstancesAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("instances").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RegistersAsync_Unauthenticated_ReturnsNoteWithoutCallingBackend()
    {
        _callerMock.SetupGet(c => c.IsAuthenticated).Returns(false);

        var body = await CreateSut().RegistersAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("registers").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();
        _registerClientMock.Verify(
            c => c.GetRecentRegistersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegistersAsync_Authenticated_ReturnsTheRegisterList()
    {
        Authenticate();
        _registerClientMock
            .Setup(c => c.GetRecentRegistersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new RegisterSummaryInfo { Id = "reg-1", Name = "Assured Identity Register", Status = "Active", TenantId = "org-1", Height = 42 }
            ]);

        var body = await CreateSut().RegistersAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(body);
        var registers = doc.RootElement.GetProperty("registers");
        registers.GetArrayLength().Should().Be(1);
        registers[0].GetProperty("id").GetString().Should().Be("reg-1");
    }

    [Fact]
    public async Task RegistersAsync_BackendThrows_ReturnsNoteInsteadOfThrowing()
    {
        Authenticate();
        _registerClientMock
            .Setup(c => c.GetRecentRegistersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var body = await CreateSut().RegistersAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("registers").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("note").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
