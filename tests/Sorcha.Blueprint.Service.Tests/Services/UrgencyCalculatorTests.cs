// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Service.Services.Implementation;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Tests for UrgencyCalculator deadline-based urgency computation.
/// </summary>
public class UrgencyCalculatorTests
{
    private static JsonElement Parse(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void Calculate_NullConfig_ReturnsNormal()
    {
        var result = UrgencyCalculator.Calculate(null, null);
        result.Should().Be("normal");
    }

    [Fact]
    public void Calculate_AlwaysUrgent_ReturnsUrgent()
    {
        var config = new NotificationConfig { UrgencyRule = "always-urgent" };
        var result = UrgencyCalculator.Calculate(config, null);
        result.Should().Be("urgent");
    }

    [Fact]
    public void Calculate_DeadlineInLessThan4Hours_ReturnsUrgent()
    {
        var deadline = DateTimeOffset.UtcNow.AddHours(2).ToString("O");
        var payload = Parse($$"""{"due": "{{deadline}}"}""");
        var config = new NotificationConfig
        {
            UrgencyRule = "deadline",
            DeadlineField = "payload.due"
        };

        var result = UrgencyCalculator.Calculate(config, payload);
        result.Should().Be("urgent");
    }

    [Fact]
    public void Calculate_DeadlineInLessThan24Hours_ReturnsWarning()
    {
        var deadline = DateTimeOffset.UtcNow.AddHours(12).ToString("O");
        var payload = Parse($$"""{"due": "{{deadline}}"}""");
        var config = new NotificationConfig
        {
            UrgencyRule = "deadline",
            DeadlineField = "payload.due"
        };

        var result = UrgencyCalculator.Calculate(config, payload);
        result.Should().Be("warning");
    }

    [Fact]
    public void Calculate_DeadlineMoreThan24Hours_ReturnsNormal()
    {
        var deadline = DateTimeOffset.UtcNow.AddDays(3).ToString("O");
        var payload = Parse($$"""{"due": "{{deadline}}"}""");
        var config = new NotificationConfig
        {
            UrgencyRule = "deadline",
            DeadlineField = "payload.due"
        };

        var result = UrgencyCalculator.Calculate(config, payload);
        result.Should().Be("normal");
    }

    [Fact]
    public void Calculate_PastDeadline_ReturnsUrgent()
    {
        var deadline = DateTimeOffset.UtcNow.AddHours(-1).ToString("O");
        var payload = Parse($$"""{"due": "{{deadline}}"}""");
        var config = new NotificationConfig
        {
            UrgencyRule = "deadline",
            DeadlineField = "payload.due"
        };

        var result = UrgencyCalculator.Calculate(config, payload);
        result.Should().Be("urgent");
    }

    [Fact]
    public void Calculate_MissingDeadlineField_ReturnsNormal()
    {
        var payload = Parse("""{"other": "value"}""");
        var config = new NotificationConfig
        {
            UrgencyRule = "deadline",
            DeadlineField = "payload.due"
        };

        var result = UrgencyCalculator.Calculate(config, payload);
        result.Should().Be("normal");
    }

    [Fact]
    public void Calculate_NoUrgencyRule_ReturnsNormal()
    {
        var config = new NotificationConfig { UrgencyRule = "none" };
        var result = UrgencyCalculator.Calculate(config, null);
        result.Should().Be("normal");
    }

    [Fact]
    public void ExtractDeadline_ValidField_ReturnsDateTimeOffset()
    {
        var expected = DateTimeOffset.UtcNow.AddDays(1);
        var payload = Parse($$"""{"due": "{{expected:O}}"}""");
        var config = new NotificationConfig { DeadlineField = "payload.due" };

        var result = UrgencyCalculator.ExtractDeadline(config, payload);
        result.Should().NotBeNull();
        result!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void ExtractDeadline_NullConfig_ReturnsNull()
    {
        var result = UrgencyCalculator.ExtractDeadline(null, null);
        result.Should().BeNull();
    }
}
