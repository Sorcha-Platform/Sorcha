// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Sorcha.Agent.Persona;

namespace Sorcha.Agent.Tests.Persona;

public class PayloadTokenResolverTests
{
    private sealed class StubRandom : IRandomSource
    {
        public int IntValue { get; init; } = 42;
        public decimal DecimalValue { get; init; } = 99.99m;
        public int ChoiceIndex { get; init; } = 0;

        public int NextInt(int minInclusive, int maxInclusive) => IntValue;
        public decimal NextDecimal(decimal minInclusive, decimal maxInclusive, int precision) => DecimalValue;
        public T Choose<T>(IReadOnlyList<T> options) => options[ChoiceIndex];
    }

    private static PersonaFireContext Ctx(int iteration = 1, IRandomSource? random = null) => new()
    {
        Iteration = iteration,
        Now = new DateTimeOffset(2026, 4, 22, 12, 0, 0, TimeSpan.Zero),
        RandomSource = random ?? new StubRandom()
    };

    [Fact]
    public void Resolve_CounterTokenAlone_PreservesIntegerType()
    {
        var template = JsonNode.Parse("""{ "n": "${counter}" }""")!;
        var result = new PayloadTokenResolver().Resolve(template, Ctx(iteration: 5));
        result["n"]!.GetValue<int>().Should().Be(5);
    }

    [Fact]
    public void Resolve_CounterTokenEmbedded_InterpolatesAsString()
    {
        var template = JsonNode.Parse("""{ "ref": "INV-${counter}" }""")!;
        var result = new PayloadTokenResolver().Resolve(template, Ctx(iteration: 7));
        result["ref"]!.GetValue<string>().Should().Be("INV-7");
    }

    [Fact]
    public void Resolve_RandomDecimalAlone_ProducesJsonNumber()
    {
        var template = JsonNode.Parse("""{ "amount": "${random.decimal(0, 100, 2)}" }""")!;
        var result = new PayloadTokenResolver().Resolve(template, Ctx(random: new StubRandom { DecimalValue = 42.50m }));
        result["amount"]!.GetValue<decimal>().Should().Be(42.50m);
    }

    [Fact]
    public void Resolve_RandomIntAlone_ProducesJsonNumber()
    {
        var template = JsonNode.Parse("""{ "q": "${random.int(1, 100)}" }""")!;
        var result = new PayloadTokenResolver().Resolve(template, Ctx(random: new StubRandom { IntValue = 77 }));
        result["q"]!.GetValue<int>().Should().Be(77);
    }

    [Fact]
    public void Resolve_RandomChoiceString_PicksElement()
    {
        var template = JsonNode.Parse("""{ "ccy": "${random.choice([\"EUR\",\"GBP\",\"USD\"])}" }""")!;
        var result = new PayloadTokenResolver().Resolve(template, Ctx(random: new StubRandom { ChoiceIndex = 2 }));
        result["ccy"]!.GetValue<string>().Should().Be("USD");
    }

    [Fact]
    public void Resolve_NowTokenAlone_ProducesIso8601String()
    {
        var template = JsonNode.Parse("""{ "at": "${now}" }""")!;
        var result = new PayloadTokenResolver().Resolve(template, Ctx());
        result["at"]!.GetValue<string>().Should().Be("2026-04-22T12:00:00.0000000+00:00");
    }

    [Fact]
    public void Resolve_UuidToken_ProducesNewGuid()
    {
        var template = JsonNode.Parse("""{ "id": "${uuid}" }""")!;
        var result = new PayloadTokenResolver().Resolve(template, Ctx());
        Guid.TryParse(result["id"]!.GetValue<string>(), out _).Should().BeTrue();
    }

    [Fact]
    public void ValidateTokens_UnknownToken_ReturnsError()
    {
        var template = JsonNode.Parse("""{ "x": "${randm.int(1,2)}" }""")!;
        var errors = new PayloadTokenResolver().ValidateTokens(template);
        errors.Should().ContainSingle().Which.Should().Contain("Unknown token 'randm.int'");
    }

    [Fact]
    public void ValidateTokens_RandomIntMissingArgs_ReturnsError()
    {
        var template = JsonNode.Parse("""{ "x": "${random.int}" }""")!;
        var errors = new PayloadTokenResolver().ValidateTokens(template);
        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void ValidateTokens_EmptyChoiceList_ReturnsError()
    {
        var template = JsonNode.Parse("""{ "x": "${random.choice([])}" }""")!;
        var errors = new PayloadTokenResolver().ValidateTokens(template);
        errors.Should().ContainSingle().Which.Should().Contain("non-empty");
    }

    [Fact]
    public void ValidateTokens_ValidTemplate_ReturnsNoErrors()
    {
        var template = JsonNode.Parse("""
            {
              "id": "${uuid}",
              "n": "${counter}",
              "at": "${now}",
              "price": "${random.decimal(1, 100, 2)}",
              "qty": "${random.int(1, 10)}",
              "ccy": "${random.choice([\"EUR\",\"GBP\"])}",
              "label": "Invoice-${counter}"
            }
            """)!;
        new PayloadTokenResolver().ValidateTokens(template).Should().BeEmpty();
    }

    [Fact]
    public void Resolve_DeterministicGivenSeed_ProducesSameOutput()
    {
        var random = new RandomSource(new Random(42));
        var template = JsonNode.Parse("""{ "n": "${random.int(0, 1000)}" }""")!;
        var a = new PayloadTokenResolver().Resolve(template, new PersonaFireContext
        {
            Iteration = 1, Now = DateTimeOffset.UtcNow, RandomSource = random
        });

        var random2 = new RandomSource(new Random(42));
        var b = new PayloadTokenResolver().Resolve(template, new PersonaFireContext
        {
            Iteration = 1, Now = DateTimeOffset.UtcNow, RandomSource = random2
        });

        a["n"]!.GetValue<int>().Should().Be(b["n"]!.GetValue<int>());
    }
}
