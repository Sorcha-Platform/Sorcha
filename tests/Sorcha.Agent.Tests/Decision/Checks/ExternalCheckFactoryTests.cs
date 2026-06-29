// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Decision.Checks;

namespace Sorcha.Agent.Tests.Decision.Checks;

public class ExternalCheckFactoryTests
{
    [Fact]
    public void Build_UnknownType_Throws()
    {
        var def = new CheckDefinition { Name = "x", Type = "nope" };
        var act = () => ExternalCheckFactory.Build(def, ".", new HttpClient());
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void BuildRunner_NullPath_ReturnsEmptyRunner()
    {
        var runner = ExternalCheckFactory.BuildRunner(null, new HttpClient());
        runner.HasChecks.Should().BeFalse();
    }

    [Fact]
    public void BuildRunner_EmptyPath_ReturnsEmptyRunner()
    {
        var runner = ExternalCheckFactory.BuildRunner("   ", new HttpClient());
        runner.HasChecks.Should().BeFalse();
    }

    [Fact]
    public void BuildRunner_MissingFile_ThrowsFileNotFoundException()
    {
        var act = () => ExternalCheckFactory.BuildRunner("does-not-exist.json", new HttpClient());
        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*does-not-exist.json*");
    }

    [Fact]
    public async Task Build_ProfanityCheck_MissingWordlistFile_LogsWarning()
    {
        // Arrange — wordlistFile points to a file that does not exist; inline list is empty.
        var def = new CheckDefinition
        {
            Name = "profane",
            Type = "profanity",
            Fields = ["/text"],
            WordlistFile = "does-not-exist-wordlist.txt"
        };
        var warnings = new List<string>();
        var loggerFactory = LoggerFactory.Create(b => b.AddProvider(new CapturingLoggerProvider(
            (cat, level, msg) => { if (level >= LogLevel.Warning) warnings.Add(msg); })));

        // Act — should not throw; missing wordlist is a configuration warning, not a fatal error.
        var check = ExternalCheckFactory.Build(def, ".", new HttpClient(), loggerFactory);

        // Assert — one warning logged and the check still evaluates (returns false for empty wordlist).
        warnings.Should().ContainSingle(w => w.Contains("does-not-exist-wordlist") || w.Contains("profane"),
            "a warning must be emitted when the declared wordlistFile is absent");

        var result = await check.EvaluateAsync(
            CheckTestSupport.Payload("""{"text": "damn"}"""), default);
        result.Value.Should().BeFalse("empty wordlist means the check never fires");
    }

    [Fact]
    public async Task BuildRunner_FullConfig_BuildsAllChecks_AndLoadsFixture()
    {
        // Stage a checks config + offline postcode fixture in a temp directory.
        var dir = Path.Combine(Path.GetTempPath(), "aias-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "postcodes.offline.json"),
                """{ "postcodes": ["SW1A 1AA"] }""");

            var config = ChecksConfig.Parse("""
            {
              "checks": [
                { "name": "emailVerified", "type": "email-verified", "field": "/emailVerified" },
                { "name": "photoPresent",  "type": "field-present", "field": "/portrait/tokenImageBase64" },
                { "name": "postcodeExists", "type": "uk-postcode", "addressField": "/address",
                  "offlineFixture": "postcodes.offline.json", "offlineMode": "always" },
                { "name": "profane", "type": "profanity",
                  "fields": ["/name/fullName"], "wordlistInline": ["bugger"] }
              ]
            }
            """);

            var runner = ExternalCheckFactory.BuildRunner(config, dir, new HttpClient());
            runner.HasChecks.Should().BeTrue();

            var payload = CheckTestSupport.Payload("""
            {
              "emailVerified": true,
              "portrait": { "tokenImageBase64": "abc" },
              "address": { "postcode": "SW1A 1AA" },
              "name": { "fullName": "Alice Smith" }
            }
            """);

            var facts = await runner.RunAsync(payload, default);

            facts["emailVerified"].Should().Be(true);
            facts["photoPresent"].Should().Be(true);
            facts["postcodeExists"].Should().Be(true, "the offline fixture contains SW1A 1AA");
            facts["profane"].Should().Be(false);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
