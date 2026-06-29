// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Decision.Checks;

namespace Sorcha.Agent.Tests.Decision.Checks;

public class EmailVerifiedCheckTests
{
    [Fact]
    public async Task EvaluateAsync_BooleanTrue_ReturnsTrue()
    {
        var check = new EmailVerifiedCheck("emailVerified");
        var result = await check.EvaluateAsync(CheckTestSupport.Payload("""{ "emailVerified": true }"""), default);

        result.Name.Should().Be("emailVerified");
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_StringTrue_ReturnsTrue()
    {
        var check = new EmailVerifiedCheck("emailVerified");
        var result = await check.EvaluateAsync(CheckTestSupport.Payload("""{ "emailVerified": "true" }"""), default);

        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_False_ReturnsFalse()
    {
        var check = new EmailVerifiedCheck("emailVerified");
        var result = await check.EvaluateAsync(CheckTestSupport.Payload("""{ "emailVerified": false }"""), default);

        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_FieldAbsent_ReturnsFalse()
    {
        var check = new EmailVerifiedCheck("emailVerified");
        var result = await check.EvaluateAsync(CheckTestSupport.Payload("""{ "name": "Alice" }"""), default);

        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_CustomFieldAndEmailDetail_ReadsBothPointers()
    {
        var check = new EmailVerifiedCheck("emailVerified", field: "/contact/verified", emailField: "/contact/email");
        var result = await check.EvaluateAsync(
            CheckTestSupport.Payload("""{ "contact": { "verified": true, "email": "alice@example.com" } }"""), default);

        result.Value.Should().BeTrue();
        result.Detail.Should().Be("alice@example.com");
    }
}
