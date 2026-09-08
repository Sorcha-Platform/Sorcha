// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Resources;
using Sorcha.ServiceClients.Blueprint;
using Sorcha.ServiceClients.Register;
using Sorcha.Register.Models.Enums;

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
    private readonly Mock<IServiceAvailabilityTracker> _availabilityMock = new();

    public LiveStateResourcesTests()
    {
        _availabilityMock.Setup(a => a.IsServiceAvailable(It.IsAny<string>())).Returns(true);
    }

    private LiveStateResources CreateSut() => new(
        _blueprintClientMock.Object,
        _registerClientMock.Object,
        _callerMock.Object,
        _availabilityMock.Object,
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
                new RegisterSummaryInfo { Id = "reg-1", Name = "Assured Identity Register", Status = RegisterStatus.Online, Height = 42 }
            ]);

        var body = await CreateSut().RegistersAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(body);
        var registers = doc.RootElement.GetProperty("registers");
        registers.GetArrayLength().Should().Be(1);
        registers[0].GetProperty("id").GetString().Should().Be("reg-1");
        doc.RootElement.GetProperty("count").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse(
            "one register well under the display cap is not truncation");
    }

    // Regression guard for the "silent truncation" defect: GetRecentRegistersAsync truncates
    // client-side with no total count available from the endpoint, so an agent seeing fewer
    // registers than it expected has no way to tell "that's really all of them" apart from
    // "there were more and they got cut" UNLESS the response says which. If this test is ever
    // made to pass by simply omitting the `truncated`/`count` fields again, it is wrong to relax
    // it — the fix is to keep reporting them honestly, matching the [Description] on RegistersAsync.
    [Fact]
    public async Task RegistersAsync_MoreRegistersThanDisplayLimit_ReportsTruncatedTrueAndCapsResultAtTheLimit()
    {
        Authenticate();
        // One more than LiveStateResources' internal display cap (50) — the resource must detect
        // this from the extra item coming back, not guess from a round number.
        var oneOverTheLimit = Enumerable.Range(1, 51)
            .Select(i => new RegisterSummaryInfo { Id = $"reg-{i}", Name = $"Register {i}", Status = RegisterStatus.Online, Height = i })
            .ToList();
        _registerClientMock
            .Setup(c => c.GetRecentRegistersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(oneOverTheLimit);

        var body = await CreateSut().RegistersAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("registers").GetArrayLength().Should().Be(50,
            "the response must still be capped, just not silently");
        doc.RootElement.GetProperty("count").GetInt32().Should().Be(50);
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue(
            "an agent must be able to tell 'more exist' from 'that really is all of them'");
    }

    [Fact]
    public async Task RegistersAsync_RequestsOneMoreThanTheDisplayLimit_SoTruncationCanBeDetectedExactly()
    {
        Authenticate();
        _registerClientMock
            .Setup(c => c.GetRecentRegistersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateSut().RegistersAsync(CancellationToken.None);

        // 51 = the 50-entry display cap + 1: asking for exactly the cap would make "got back
        // fewer than requested" and "got back exactly the cap, more may exist" indistinguishable.
        _registerClientMock.Verify(
            c => c.GetRecentRegistersAsync(51, It.IsAny<CancellationToken>()), Times.Once);
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

    // Important 5 (final-fix-report): GetRecentRegistersAsync itself swallows every failure into
    // the SAME empty list a genuinely empty org would produce, and never throws — the two
    // catch clauses above are defensive, not reachable in practice. The availability tracker is
    // the one honest (if partial) signal available without changing that client's contract: if
    // another Register-service tool call already recorded a failure in this process, this
    // resource must say so rather than silently reporting "registers: []".
    [Fact]
    public async Task RegistersAsync_RegisterServiceMarkedUnavailable_ReturnsHonestNoteWithoutCallingBackend()
    {
        Authenticate();
        _availabilityMock.Setup(a => a.IsServiceAvailable("Register")).Returns(false);

        var body = await CreateSut().RegistersAsync(CancellationToken.None);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("registers").GetArrayLength().Should().Be(0);
        var note = doc.RootElement.GetProperty("note").GetString();
        note.Should().NotBeNullOrWhiteSpace();
        note.Should().ContainEquivalentOf("unreachable",
            "the note must say the service was unreachable, not just that the list is empty");
        note.Should().ContainEquivalentOf("NOT confirmation",
            "an unreachable service must not be reported as a confirmed-empty organisation");
        _registerClientMock.Verify(
            c => c.GetRecentRegistersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
