// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;
using Json.Schema;
using Sorcha.Blueprint.Models.Schemas;
using Xunit;

namespace Sorcha.Blueprint.Models.Tests.Schemas;

/// <summary>
/// End-to-end tests for <c>formatMaximum</c> / <c>formatMinimum</c> enforcement, driven through a
/// real <see cref="JsonSchema"/> evaluation under the Sorcha dialect rather than by calling the
/// handler directly — the handler is only reachable in production via dialect resolution, so
/// testing it in isolation would prove the logic and not the wiring.
/// </summary>
/// <remarks>
/// Regression cover for the gap measured on n1 2026-08-17: <c>DateOfBirth.v1</c> declares
/// <c>"formatMaximum": "today"</c>, yet a date of birth of <c>2035-06-15</c> was accepted and
/// sealed into a docket because the keyword was unknown to the dialect and therefore an annotation.
/// </remarks>
public class FormatBoundEnforcementTests
{
    private static readonly string Today = DateTime.UtcNow.ToString("yyyy-MM-dd");

    private static JsonSchema BuildSchema(string propertySchema)
    {
        SorchaSchemaDialect.EnsureRegistered();

        return JsonSchema.FromText($$"""
        {
          "$schema": "{{SorchaSchemaDialect.Id}}",
          "type": "object",
          "properties": { "value": {{propertySchema}} }
        }
        """);
    }

    private static EvaluationResults Evaluate(JsonSchema schema, string json) =>
        schema.Evaluate(
            JsonSerializer.Deserialize<JsonElement>(json),
            new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });

    private static IEnumerable<string> ErrorKeys(EvaluationResults results) =>
        (results.Details ?? [])
            .Where(d => !d.IsValid && d.Errors != null)
            .SelectMany(d => d.Errors!)
            .Select(e => e.Key);

    private const string DateOfBirthProperty =
        """{ "type": "string", "format": "date", "formatMaximum": "today" }""";

    private const string StartDateProperty =
        """{ "type": "string", "format": "date", "formatMinimum": "today" }""";

    [Fact]
    public void FormatMaximum_RefusesAFutureDate()
    {
        // The defect this whole change exists for.
        var results = Evaluate(BuildSchema(DateOfBirthProperty), """{"value":"2035-06-15"}""");

        results.IsValid.Should().BeFalse("a date of birth in the future is not a date of birth");
        ErrorKeys(results).Should().Contain("formatMaximum");
    }

    [Fact]
    public void FormatMaximum_AcceptsAPastDate()
    {
        Evaluate(BuildSchema(DateOfBirthProperty), """{"value":"1990-01-01"}""")
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void FormatMaximum_IsInclusiveOfTheBound()
    {
        // 'today' must accept today — a person born today has a valid date of birth.
        Evaluate(BuildSchema(DateOfBirthProperty), $$"""{"value":"{{Today}}"}""")
            .IsValid.Should().BeTrue("the bound is inclusive");
    }

    [Fact]
    public void FormatMinimum_RefusesAPastDate()
    {
        var results = Evaluate(BuildSchema(StartDateProperty), """{"value":"1990-01-01"}""");

        results.IsValid.Should().BeFalse();
        ErrorKeys(results).Should().Contain("formatMinimum");
    }

    [Fact]
    public void FormatMinimum_AcceptsAFutureDate()
    {
        Evaluate(BuildSchema(StartDateProperty), """{"value":"2035-06-15"}""")
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void AMalformedDate_IsReportedByFormatOnly_NotAlsoByTheBound()
    {
        // One defect should produce one error. `format: date` owns malformed dates.
        var results = Evaluate(BuildSchema(DateOfBirthProperty), """{"value":"not-a-date"}""");

        results.IsValid.Should().BeFalse();
        ErrorKeys(results).Should().Contain("format");
        ErrorKeys(results).Should().NotContain("formatMaximum",
            "the bound must not double-report a value that is not a date at all");
    }

    [Fact]
    public void ANonStringValue_IsLeftToTheTypeKeyword()
    {
        var results = Evaluate(BuildSchema(DateOfBirthProperty), """{"value":123}""");

        results.IsValid.Should().BeFalse();
        ErrorKeys(results).Should().Contain("type");
        ErrorKeys(results).Should().NotContain("formatMaximum");
    }

    [Fact]
    public void AnUnresolvableBound_FailsClosed()
    {
        // A cutoff nobody can resolve must not quietly admit every value — that would be the
        // original bug wearing a different hat.
        var schema = BuildSchema("""{ "type": "string", "format": "date", "formatMaximum": "whenever" }""");

        var results = Evaluate(schema, """{"value":"1990-01-01"}""");

        results.IsValid.Should().BeFalse("an unresolvable bound is a schema fault, not a pass");
        ErrorKeys(results).Should().Contain("formatMaximum");
    }

    [Fact]
    public void RelativeTokens_AreSupported()
    {
        // today-18Y is the age-gate case SorchaDateTokenResolver was built for.
        var schema = BuildSchema("""{ "type": "string", "format": "date", "formatMaximum": "today-18Y" }""");

        var bornTwenty = DateTime.UtcNow.AddYears(-20).ToString("yyyy-MM-dd");
        var bornTen = DateTime.UtcNow.AddYears(-10).ToString("yyyy-MM-dd");

        Evaluate(schema, $$"""{"value":"{{bornTwenty}}"}""").IsValid.Should().BeTrue();
        Evaluate(schema, $$"""{"value":"{{bornTen}}"}""").IsValid.Should().BeFalse();
    }

    [Fact]
    public void ASchemaNotUsingTheBounds_EvaluatesIdenticallyToPlain202012()
    {
        // The Sorcha dialect is a strict superset: it must change nothing for the ~all schemas
        // that never mention these keywords.
        const string property = """{ "type": "string", "format": "email", "minLength": 3 }""";

        var sorcha = Evaluate(BuildSchema(property), """{"value":"nonsense"}""");

        SorchaSchemaDialect.EnsureRegistered();
        var plain = JsonSchema.FromText($$"""
        {
          "$schema": "{{SorchaSchemaDialect.Draft202012Id}}",
          "type": "object",
          "properties": { "value": {{property}} }
        }
        """);
        var baseline = Evaluate(plain, """{"value":"nonsense"}""");

        sorcha.IsValid.Should().Be(baseline.IsValid);
        ErrorKeys(sorcha).Should().BeEquivalentTo(ErrorKeys(baseline));
    }

    [Theory]
    [InlineData(null, SorchaSchemaDialect.Id)]
    [InlineData("", SorchaSchemaDialect.Id)]
    [InlineData(SorchaSchemaDialect.Draft202012Id, SorchaSchemaDialect.Id)]
    [InlineData(SorchaSchemaDialect.Id, SorchaSchemaDialect.Id)]
    [InlineData("http://json-schema.org/draft-07/schema#", "http://json-schema.org/draft-07/schema#")]
    public void ResolveReadDialect_UpgradesOnly202012AndUndeclared(string? declared, string expected)
    {
        // Upgrading undeclared/2020-12 is what reaches blueprints ALREADY SEALED, whose baked-in
        // schemas will forever declare 2020-12. Another draft is left alone — it was authored
        // against different semantics.
        SorchaSchemaDialect.ResolveReadDialect(declared).Should().Be(expected);
    }

    [Fact]
    public void EnsureRegistered_IsIdempotent()
    {
        // Called on every schema parse; registering twice would throw.
        var act = () =>
        {
            SorchaSchemaDialect.EnsureRegistered();
            SorchaSchemaDialect.EnsureRegistered();
            SorchaSchemaDialect.EnsureRegistered();
        };

        act.Should().NotThrow();
    }
}
