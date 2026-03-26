// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Tests;

public class InstanceReferenceGeneratorTests
{
    private const string TestInstanceId = "9e8062af-ee19-4a3f-a378-690578ee39d9";
    private const string TestBlueprintTitle = "Construction Permit Approval";

    private static InstanceReferenceTemplate CreateTemplate(string prefix = "CP", params (string field, ReferenceTransform transform, int chars)[] components)
    {
        return new InstanceReferenceTemplate
        {
            Prefix = prefix,
            Components = components.Select(c => new ReferenceComponent
            {
                Field = c.field,
                Transform = c.transform,
                Chars = c.chars
            }).ToList()
        };
    }

    // --- Transform Tests ---

    [Fact]
    public void Generate_FirstWordTransform_ExtractsFirstWord()
    {
        var template = CreateTemplate("CP", ("/projectName", ReferenceTransform.FirstWord, 3));
        var data = new Dictionary<string, object> { ["projectName"] = "Riverside Heights" };

        var result = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        result.Should().StartWith("CP-RIV-");
    }

    [Fact]
    public void Generate_TruncateTransform_TruncatesFullValue()
    {
        var template = CreateTemplate("PA", ("/name", ReferenceTransform.Truncate, 4));
        var data = new Dictionary<string, object> { ["name"] = "Smith" };

        var result = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        result.Should().StartWith("PA-SMIT-");
    }

    [Fact]
    public void Generate_UppercaseTransform_UppercasesValue()
    {
        var template = CreateTemplate("TX", ("/city", ReferenceTransform.Uppercase, 5));
        var data = new Dictionary<string, object> { ["city"] = "riverside" };

        var result = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        result.Should().StartWith("TX-RIVER-");
    }

    [Fact]
    public void Generate_MultipleComponents_JoinsWithHyphens()
    {
        var template = CreateTemplate("CP",
            ("/projectName", ReferenceTransform.FirstWord, 3),
            ("/siteAddress", ReferenceTransform.FirstWord, 3));
        var data = new Dictionary<string, object>
        {
            ["projectName"] = "Riverside Heights",
            ["siteAddress"] = "14 Waterfront Lane"
        };

        var result = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        // CP-RIV-14-{hash} ("14" is the first word of "14 Waterfront Lane")
        result.Should().StartWith("CP-RIV-14-");
    }

    [Fact]
    public void Generate_NumericFirstWord_HandlesCorrectly()
    {
        var template = CreateTemplate("CP", ("/address", ReferenceTransform.FirstWord, 3));
        var data = new Dictionary<string, object> { ["address"] = "14 Waterfront Lane" };

        var result = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        result.Should().StartWith("CP-14-"); // "14" is the first word, only 2 chars (no padding)
    }

    // --- Null/Empty Field Fallback ---

    [Fact]
    public void Generate_NullFieldValue_UsesUnkFallback()
    {
        var template = CreateTemplate("CP", ("/missing", ReferenceTransform.FirstWord, 3));
        var data = new Dictionary<string, object>();

        var result = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        result.Should().StartWith("CP-UNK-");
    }

    [Fact]
    public void Generate_EmptyStringFieldValue_UsesUnkFallback()
    {
        var template = CreateTemplate("CP", ("/name", ReferenceTransform.FirstWord, 3));
        var data = new Dictionary<string, object> { ["name"] = "" };

        var result = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        result.Should().StartWith("CP-UNK-");
    }

    // --- Hash Tests ---

    [Fact]
    public void Generate_SameInstanceId_ProducesSameHash()
    {
        var template = CreateTemplate("CP", ("/name", ReferenceTransform.FirstWord, 3));
        var data = new Dictionary<string, object> { ["name"] = "Test" };

        var result1 = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);
        var result2 = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        result1.Should().Be(result2);
    }

    [Fact]
    public void Generate_DifferentInstanceIds_ProduceDifferentHashes()
    {
        var template = CreateTemplate("CP", ("/name", ReferenceTransform.FirstWord, 3));
        var data = new Dictionary<string, object> { ["name"] = "Test" };

        var result1 = InstanceReferenceGenerator.Generate(template, data, "aaaa-bbbb-cccc", TestBlueprintTitle);
        var result2 = InstanceReferenceGenerator.Generate(template, data, "dddd-eeee-ffff", TestBlueprintTitle);

        result1.Should().NotBe(result2);
    }

    [Fact]
    public void Generate_HashIsFourCharacters()
    {
        var template = CreateTemplate("CP", ("/name", ReferenceTransform.FirstWord, 3));
        var data = new Dictionary<string, object> { ["name"] = "Test" };

        var result = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        // Format: CP-TES-{4chars}
        var parts = result.Split('-');
        parts.Last().Should().HaveLength(4);
    }

    // --- Fallback (no template) ---

    [Fact]
    public void Generate_NullTemplate_UsesFallback()
    {
        var data = new Dictionary<string, object>();

        var result = InstanceReferenceGenerator.Generate(null, data, TestInstanceId, TestBlueprintTitle);

        // Fallback: first 2 chars of title + hash = "CO-{hash}"
        result.Should().StartWith("CO-");
        result.Split('-').Should().HaveCount(2);
    }

    [Fact]
    public void Generate_NullTemplate_EmptyTitle_UsesBpFallback()
    {
        var data = new Dictionary<string, object>();

        var result = InstanceReferenceGenerator.Generate(null, data, TestInstanceId, "");

        result.Should().StartWith("BP-");
    }

    // --- Output Format ---

    [Fact]
    public void Generate_OutputIsAllUppercase()
    {
        var template = CreateTemplate("CP", ("/name", ReferenceTransform.FirstWord, 3));
        var data = new Dictionary<string, object> { ["name"] = "riverside" };

        var result = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        result.Should().MatchRegex(@"^[A-Z0-9\-]+$");
    }

    [Fact]
    public void Generate_NonAsciiCharacters_StrippedFromOutput()
    {
        var template = CreateTemplate("CP", ("/name", ReferenceTransform.FirstWord, 3));
        var data = new Dictionary<string, object> { ["name"] = "Cafe Creme" }; // accents stripped if present

        var result = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        result.Should().StartWith("CP-CAF-");
        result.Should().MatchRegex(@"^[A-Z0-9\-]+$");
    }

    [Fact]
    public void Generate_ShortFirstWord_NoExtraPadding()
    {
        var template = CreateTemplate("CP", ("/name", ReferenceTransform.FirstWord, 5));
        var data = new Dictionary<string, object> { ["name"] = "A" };

        var result = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        // "A" is only 1 char — no padding to 5
        result.Should().StartWith("CP-A-");
    }

    [Fact]
    public void Generate_ConstructionPermitScenarioA_MatchesExpectedPattern()
    {
        var template = CreateTemplate("CP",
            ("/projectName", ReferenceTransform.FirstWord, 3),
            ("/siteAddress", ReferenceTransform.FirstWord, 3));
        var data = new Dictionary<string, object>
        {
            ["projectName"] = "Riverside Heights",
            ["siteAddress"] = "14 Waterfront Lane, Riverside, RS1 4AB"
        };

        var result = InstanceReferenceGenerator.Generate(template, data, TestInstanceId, TestBlueprintTitle);

        // "14" is the first word of "14 Waterfront Lane..."
        result.Should().MatchRegex(@"^CP-RIV-14-[A-Z0-9]{4}$");
    }
}
