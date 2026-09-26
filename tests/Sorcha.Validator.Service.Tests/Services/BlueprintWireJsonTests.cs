// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.Serialization;
using Sorcha.Validator.Service.Services;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// #1700 — the validator could not read a blueprint the Blueprint Service served. The service writes
/// through SorchaJson (kebab-case enums, <c>"first-word"</c>); the Redis cache copy is PascalCase
/// (<c>"FirstWord"</c>); the validator read both with plain options, which accept only PascalCase.
/// Every cold-cache fetch of a pinned definition with an instanceReference transform threw.
/// <para>
/// The bytes here are produced with the PRODUCER's own options (SorchaJson, which the Blueprint
/// Service's Program.cs configures) — not with matched options on both sides, which is exactly the
/// test shape that let this survive.
/// </para>
/// </summary>
public class BlueprintWireJsonTests
{
    private static BlueprintModel WithTransform(ReferenceTransform transform) => new()
    {
        Id = "bp-1700",
        Title = "Wire test",
        InstanceReference = new InstanceReferenceTemplate
        {
            Prefix = "WT",
            Components = [new ReferenceComponent { Field = "/name", Transform = transform, Chars = 3 }],
        },
    };

    [Theory]
    [InlineData(ReferenceTransform.FirstWord)]
    [InlineData(ReferenceTransform.Truncate)]
    public void TheValidatorReads_WhatTheBlueprintServiceServes(ReferenceTransform transform)
    {
        var served = JsonSerializer.Serialize(WithTransform(transform), SorchaJson.Options);
        served.Should().Contain(transform == ReferenceTransform.FirstWord ? "\"first-word\"" : "\"truncate\"",
            "the Blueprint Service's options put the kebab-case name on the wire — if this changes, the test's premise has");

        var read = JsonSerializer.Deserialize<BlueprintModel>(served, BlueprintWireJson.Options);

        read!.InstanceReference!.Components[0].Transform.Should().Be(transform);
    }

    [Fact]
    public void TheValidatorReads_WhatTheRedisCacheHolds()
    {
        // The cache copy is written without SorchaJson, so the type-level attribute wins: PascalCase.
        var cached = JsonSerializer.Serialize(WithTransform(ReferenceTransform.FirstWord),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        cached.Should().Contain("\"FirstWord\"");

        var read = JsonSerializer.Deserialize<BlueprintModel>(cached, BlueprintWireJson.Options);

        read!.InstanceReference!.Components[0].Transform.Should().Be(ReferenceTransform.FirstWord);
    }

    [Fact]
    public void TheOldPlainOptions_CouldNotReadTheServedForm()
    {
        // The counterfactual: the options both readers used before, against the served bytes.
        var served = JsonSerializer.Serialize(WithTransform(ReferenceTransform.FirstWord), SorchaJson.Options);
        var plain = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

        var act = () => JsonSerializer.Deserialize<BlueprintModel>(served, plain);

        act.Should().Throw<JsonException>().WithMessage("*ReferenceTransform*");
    }
}
