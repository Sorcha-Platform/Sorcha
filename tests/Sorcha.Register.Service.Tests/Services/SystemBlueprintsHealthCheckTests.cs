// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceDefaults;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

/// <summary>Feature 197: the system-blueprints health check's status mapping (data-model.md).</summary>
public class SystemBlueprintsHealthCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FakeSnapshot(IReadOnlyList<SystemBlueprintDriftEntry>? entries) : ISystemBlueprintDriftSnapshot
    {
        public IReadOnlyList<SystemBlueprintDriftEntry>? Entries { get; } = entries;
    }

    private static SystemBlueprintDriftEntry Entry(string id, SystemBlueprintDriftState state)
        => new(id, state, "cur", 2, "img", null, Now);

    private static async Task<HealthCheckResult> RunAsync(
        IReadOnlyList<SystemBlueprintDriftEntry>? entries, BootstrapMode mode, bool completed)
    {
        var status = new SystemRegisterBootstrapStatus();
        if (completed) status.MarkCompleted(mode);
        var check = new SystemBlueprintsHealthCheck(new FakeSnapshot(entries), status);
        return await check.CheckHealthAsync(new HealthCheckContext());
    }

    [Fact]
    public async Task CheckHealthAsync_NoSnapshotYet_HealthyNotYetComputed()
    {
        var result = await RunAsync(null, BootstrapMode.Auto, completed: false);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("not yet computed");
    }

    [Fact]
    public async Task CheckHealthAsync_EmptySnapshot_HealthyNotYetComputed()
    {
        var result = await RunAsync([], BootstrapMode.Auto, completed: true);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("not yet computed");
    }

    [Fact]
    public async Task CheckHealthAsync_AllInSync_HealthyWithPerBlueprintData()
    {
        var result = await RunAsync(
            [Entry("a", SystemBlueprintDriftState.InSync), Entry("b", SystemBlueprintDriftState.InSync)],
            BootstrapMode.Auto, completed: true);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("a").WhoseValue.Should().Be("InSync");
        result.Data.Should().ContainKey("b").WhoseValue.Should().Be("InSync");
    }

    [Theory]
    [InlineData(SystemBlueprintDriftState.ImageBehind)]
    [InlineData(SystemBlueprintDriftState.ImageAhead)]
    [InlineData(SystemBlueprintDriftState.Unknown)]
    public async Task CheckHealthAsync_DriftOrUnknown_DegradedNamingOffender(SystemBlueprintDriftState state)
    {
        var result = await RunAsync(
            [Entry("ok", SystemBlueprintDriftState.InSync), Entry("bad", state)],
            BootstrapMode.Auto, completed: true);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("bad").And.NotContain("ok (");
        result.Data["bad"].Should().Be(state.ToString());
        result.Data["ok"].Should().Be("InSync");
    }

    [Theory]
    [InlineData(SystemBlueprintDriftState.ImageBehind)]
    [InlineData(SystemBlueprintDriftState.ImageAhead)]
    [InlineData(SystemBlueprintDriftState.Unknown)]
    public async Task CheckHealthAsync_DriftOrUnknownOnSyncOnly_StillDegraded(SystemBlueprintDriftState state)
    {
        var result = await RunAsync([Entry("bad", state)], BootstrapMode.SyncOnly, completed: true);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_MissingOnOwnerAfterBootstrap_Degraded()
    {
        var result = await RunAsync([Entry("gone", SystemBlueprintDriftState.Missing)], BootstrapMode.Auto, completed: true);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("gone");
    }

    [Fact]
    public async Task CheckHealthAsync_MissingOnSyncOnlyNode_Healthy()
    {
        var result = await RunAsync([Entry("gone", SystemBlueprintDriftState.Missing)], BootstrapMode.SyncOnly, completed: true);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data["gone"].Should().Be("Missing");
    }

    [Fact]
    public async Task CheckHealthAsync_MissingOnOwnerBeforeBootstrapCompletes_Healthy()
    {
        var result = await RunAsync([Entry("gone", SystemBlueprintDriftState.Missing)], BootstrapMode.Auto, completed: false);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_EveryStateCombination_NeverUnhealthy()
    {
        foreach (var mode in Enum.GetValues<BootstrapMode>())
        foreach (var completed in new[] { true, false })
        foreach (var state in Enum.GetValues<SystemBlueprintDriftState>())
        {
            var result = await RunAsync([Entry("x", state)], mode, completed);
            result.Status.Should().NotBe(HealthStatus.Unhealthy, $"{mode}/{completed}/{state}");
        }
    }
}
