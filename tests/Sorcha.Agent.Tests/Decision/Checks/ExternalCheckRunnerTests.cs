// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Decision.Checks;

namespace Sorcha.Agent.Tests.Decision.Checks;

public class ExternalCheckRunnerTests
{
    /// <summary>A check that returns a fixed result — for composing runner scenarios.</summary>
    private sealed class FixedCheck(string name, bool value, string? detail = null) : IExternalCheck
    {
        public string Name => name;
        public Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
            => Task.FromResult(new ExternalCheckResult(name, value, detail));
    }

    /// <summary>A check that always throws — for verifying fault containment.</summary>
    private sealed class ThrowingCheck(string name) : IExternalCheck
    {
        public string Name => name;
        public Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyPayload = new Dictionary<string, object?>();

    [Fact]
    public void HasChecks_NoChecks_ReturnsFalse()
    {
        new ExternalCheckRunner([]).HasChecks.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_MergesMultipleChecks_WithDetails()
    {
        var runner = new ExternalCheckRunner(
        [
            new FixedCheck("postcodeExists", true, "SW1A 1AA"),
            new FixedCheck("profane", false),
            new FixedCheck("emailVerified", true)
        ]);

        var facts = await runner.RunAsync(EmptyPayload, default);

        facts["postcodeExists"].Should().Be(true);
        facts["postcodeExistsDetail"].Should().Be("SW1A 1AA");
        facts["profane"].Should().Be(false);
        facts.Should().NotContainKey("profaneDetail", "no detail was supplied");
        facts["emailVerified"].Should().Be(true);
    }

    [Fact]
    public async Task RunAsync_FaultingCheck_ResolvesToFalse_AndDoesNotThrow()
    {
        var runner = new ExternalCheckRunner(
        [
            new FixedCheck("postcodeExists", true),
            new ThrowingCheck("profane")
        ]);

        var facts = await runner.RunAsync(EmptyPayload, default);

        facts["postcodeExists"].Should().Be(true);
        facts["profane"].Should().Be(false, "an unexpectedly faulting check resolves to a safe default");
    }
}
