// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

public class NavigationStateServiceTests
{
    private sealed record SamplePayload(string Name, int Value);

    private sealed record OtherPayload(string Description);

    [Fact]
    public void Set_ThenGet_ReturnsValueAndRemovesEntry()
    {
        // Arrange
        var service = new NavigationStateService();
        var payload = new SamplePayload("alpha", 42);

        // Act
        service.Set("test-key", payload);
        var first = service.Get<SamplePayload>("test-key");
        var second = service.Get<SamplePayload>("test-key");

        // Assert
        first.Should().Be(payload);
        second.Should().BeNull("Get removes the entry on read");
    }

    [Fact]
    public void Get_WithoutSet_ReturnsNull()
    {
        var service = new NavigationStateService();

        var result = service.Get<SamplePayload>("missing-key");

        result.Should().BeNull();
    }

    [Fact]
    public void Get_WithWrongType_ReturnsNull()
    {
        var service = new NavigationStateService();
        service.Set("key", new SamplePayload("alpha", 1));

        var result = service.Get<OtherPayload>("key");

        result.Should().BeNull();
    }

    [Fact]
    public void Set_OverwritesPreviousValueForSameKey()
    {
        var service = new NavigationStateService();
        service.Set("key", new SamplePayload("first", 1));

        service.Set("key", new SamplePayload("second", 2));
        var result = service.Get<SamplePayload>("key");

        result.Should().NotBeNull();
        result!.Name.Should().Be("second");
        result.Value.Should().Be(2);
    }

    [Fact]
    public void Get_AfterTwoConsecutiveCalls_FirstReturnsValueSecondReturnsNull()
    {
        var service = new NavigationStateService();
        service.Set("key", new SamplePayload("alpha", 1));

        var first = service.Get<SamplePayload>("key");
        var second = service.Get<SamplePayload>("key");

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public void Set_WithEmptyKey_Throws()
    {
        var service = new NavigationStateService();

        var act = () => service.Set("", new SamplePayload("x", 1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Get_WithEmptyKey_ReturnsNull()
    {
        var service = new NavigationStateService();

        var result = service.Get<SamplePayload>("");

        result.Should().BeNull();
    }

    [Fact]
    public void Set_WithNullValue_RemovesExistingEntry()
    {
        var service = new NavigationStateService();
        service.Set("key", new SamplePayload("alpha", 1));

        service.Set<SamplePayload>("key", null!);
        var result = service.Get<SamplePayload>("key");

        result.Should().BeNull();
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var service = new NavigationStateService();
        service.Set("a", new SamplePayload("one", 1));
        service.Set("b", new SamplePayload("two", 2));

        service.Clear();

        service.Get<SamplePayload>("a").Should().BeNull();
        service.Get<SamplePayload>("b").Should().BeNull();
    }

    [Fact]
    public void MultipleKeys_AreIndependent()
    {
        var service = new NavigationStateService();
        var p1 = new SamplePayload("first", 1);
        var p2 = new SamplePayload("second", 2);

        service.Set("key1", p1);
        service.Set("key2", p2);

        service.Get<SamplePayload>("key1").Should().Be(p1);
        service.Get<SamplePayload>("key2").Should().Be(p2);
    }
}
