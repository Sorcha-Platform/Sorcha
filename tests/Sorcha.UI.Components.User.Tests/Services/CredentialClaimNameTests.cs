// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Sorcha.UI.Core.Services.Credentials;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Services;

/// <summary>
/// Issue #1278 (UT-015) — the claim-label split. A citizen opening an Assured Identity credential
/// saw a humanised ID card ("Date of birth") sitting directly above a table of the SAME claims
/// labelled with their raw wire keys ("dateOfBirth", "age_over_18"). Two independent humanisers had
/// each solved half the problem and neither knew about the other:
/// <list type="bullet">
///   <item><c>CredentialClaimDisplayFormatter.HumaniseClaimName</c> split camelCase but treated
///   <c>_</c> as an ordinary character, so <c>age_over_18</c> became "Age_over_18".</item>
///   <item>The PWA's private <c>PrettyName</c> replaced <c>_</c> and <c>-</c> but never split
///   camelCase, so <c>dateOfBirth</c> became "DateOfBirth".</item>
/// </list>
/// Neither could label the real AIAS claim set, which contains BOTH shapes.
/// </summary>
public sealed class CredentialClaimNameTests
{
    [Theory]
    [InlineData("dateOfBirth", "Date of birth")]
    [InlineData("givenName", "Given name")]
    [InlineData("familyName", "Family name")]
    [InlineData("fullName", "Full name")]
    [InlineData("portrait", "Portrait")]
    [InlineData("address", "Address")]
    [InlineData("email", "Email")]
    // The two shapes that each old humaniser got wrong:
    [InlineData("age_over_18", "Age over 18")]
    [InlineData("issuing-country", "Issuing country")]
    public void ClaimNamesHumaniseToSentenceCase(string key, string expected)
        => CredentialClaimDisplayFormatter.HumaniseClaimName(key).Should().Be(expected);

    [Fact]
    public void AnAlreadyReadableLabelIsLeftAlone()
        => CredentialClaimDisplayFormatter.HumaniseClaimName("Date of birth").Should().Be("Date of birth");

    [Fact]
    public void AnEmptyKeyIsReturnedUnchanged_RatherThanThrowing()
        => CredentialClaimDisplayFormatter.HumaniseClaimName("").Should().Be("");

    /// <summary>
    /// Derived, not hand-kept: reads the claim names the live AIAS blueprint actually maps into the
    /// credential. Adding a claim to that blueprint with a shape this humaniser mangles fails here
    /// rather than shipping a machine token to a citizen's screen.
    /// </summary>
    [Fact]
    public void EveryClaimTheAiasCredentialIssues_HumanisesToSomethingReadable()
    {
        var claimNames = AiasClaimNames();
        claimNames.Should().NotBeEmpty("the derivation is worthless if it silently finds nothing");

        using var scope = new AssertionScopeShim();
        foreach (var name in claimNames)
        {
            var label = CredentialClaimDisplayFormatter.HumaniseClaimName(name);

            label.Should().NotContain("_", $"'{name}' must not reach a citizen with wire punctuation");
            label.Should().NotContain("-", $"'{name}' must not reach a citizen with wire punctuation");
            label[1..].Should().NotMatchRegex("[A-Z]",
                $"'{name}' must not reach a citizen with an unsplit camelCase hump");
            label.Should().NotBeNullOrWhiteSpace();
            char.IsUpper(label[0]).Should().BeTrue($"'{name}' should render sentence-cased");
        }
    }

    private static IReadOnlyList<string> AiasClaimNames()
    {
        var path = Path.Combine(
            FindRepoRoot(AppContext.BaseDirectory),
            "demos", "AIAS", "blueprints", "aias-assured-identity.template.json");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var names = new List<string>();
        Collect(doc.RootElement, names);
        return names;
    }

    private static void Collect(JsonElement el, List<string> into)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject())
                {
                    if (p.NameEquals("claimMappings") && p.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in p.Value.EnumerateArray())
                        {
                            if (m.TryGetProperty("claimName", out var n) && n.GetString() is { Length: > 0 } s)
                            {
                                into.Add(s);
                            }
                        }
                    }

                    Collect(p.Value, into);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray()) Collect(item, into);
                break;
        }
    }

    private static string FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Sorcha.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repo root (dir containing Sorcha.sln) walking up from '{startDir}'.");
    }

    /// <summary>Minimal stand-in so every claim is reported, not just the first failure.</summary>
    private sealed class AssertionScopeShim : IDisposable
    {
        private readonly FluentAssertions.Execution.AssertionScope _scope = new();
        public void Dispose() => _scope.Dispose();
    }
}
