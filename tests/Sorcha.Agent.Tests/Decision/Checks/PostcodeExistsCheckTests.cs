// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Decision.Checks;

namespace Sorcha.Agent.Tests.Decision.Checks;

public class PostcodeExistsCheckTests
{
    private static readonly string[] Fixture = ["SW1A 1AA", "EC1A 1BB"];

    private static IReadOnlyDictionary<string, object?> AddressPayload(string postcode) =>
        CheckTestSupport.Payload($$"""{ "address": { "town": "London", "postcode": "{{postcode}}" } }""");

    [Fact]
    public async Task EvaluateAsync_LiveLookupValid_ReturnsTrue()
    {
        var handler = StubHttpMessageHandler.Json("""{ "status": 200, "result": true }""");
        var check = new PostcodeExistsCheck("postcodeExists", "/address", handler.Client(), Fixture);

        var result = await check.EvaluateAsync(AddressPayload("SW1A 1AA"), default);

        result.Value.Should().BeTrue();
        result.Detail.Should().Be("SW1A 1AA");
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task EvaluateAsync_LiveLookupInvalid_ReturnsFalse()
    {
        var handler = StubHttpMessageHandler.Json("""{ "status": 200, "result": false }""");
        var check = new PostcodeExistsCheck("postcodeExists", "/address", handler.Client(), Fixture);

        var result = await check.EvaluateAsync(AddressPayload("ZZ99 9ZZ"), default);

        result.Value.Should().BeFalse();
        result.Detail.Should().Be("ZZ99 9ZZ");
    }

    [Fact]
    public async Task EvaluateAsync_NetworkFault_FallsBackToFixture_Hit()
    {
        var handler = StubHttpMessageHandler.Faulting();
        var check = new PostcodeExistsCheck("postcodeExists", "/address", handler.Client(), Fixture);

        var result = await check.EvaluateAsync(AddressPayload("ec1a 1bb"), default);

        result.Value.Should().BeTrue("the fixture contains EC1A 1BB regardless of spacing/casing");
    }

    [Fact]
    public async Task EvaluateAsync_NetworkFault_FallsBackToFixture_Miss()
    {
        var handler = StubHttpMessageHandler.Faulting();
        var check = new PostcodeExistsCheck("postcodeExists", "/address", handler.Client(), Fixture);

        var result = await check.EvaluateAsync(AddressPayload("ZZ99 9ZZ"), default);

        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_OfflineModeAlways_NeverCallsNetwork()
    {
        var handler = StubHttpMessageHandler.Faulting();
        var check = new PostcodeExistsCheck(
            "postcodeExists", "/address", handler.Client(), Fixture, PostcodeOfflineMode.Always);

        var result = await check.EvaluateAsync(AddressPayload("SW1A 1AA"), default);

        result.Value.Should().BeTrue();
        handler.Calls.Should().Be(0, "offlineMode=Always resolves purely against the fixture");
    }

    [Fact]
    public async Task EvaluateAsync_OfflineModeNever_FaultResolvesFalse()
    {
        var handler = StubHttpMessageHandler.Faulting();
        var check = new PostcodeExistsCheck(
            "postcodeExists", "/address", handler.Client(), Fixture, PostcodeOfflineMode.Never);

        var result = await check.EvaluateAsync(AddressPayload("SW1A 1AA"), default);

        result.Value.Should().BeFalse("offlineMode=Never has no fixture fallback");
    }

    [Fact]
    public async Task EvaluateAsync_NoPostcode_ReturnsFalse()
    {
        var handler = StubHttpMessageHandler.Json("""{ "status": 200, "result": true }""");
        var check = new PostcodeExistsCheck("postcodeExists", "/address", handler.Client(), Fixture);

        var result = await check.EvaluateAsync(CheckTestSupport.Payload("""{ "address": { "town": "London" } }"""), default);

        result.Value.Should().BeFalse();
        handler.Calls.Should().Be(0);
    }
}
