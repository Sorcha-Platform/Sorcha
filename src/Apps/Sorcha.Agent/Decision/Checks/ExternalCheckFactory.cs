// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.Agent.Decision.Checks;

/// <summary>
/// Builds <see cref="IExternalCheck"/> instances from a <see cref="ChecksConfig"/>, resolving any
/// fixture/wordlist file paths relative to the config file's directory and wiring the shared
/// <see cref="HttpClient"/> into the postcode check.
/// </summary>
public static class ExternalCheckFactory
{
    /// <summary>
    /// Builds a runner from the checks config file at <paramref name="configPath"/>. Returns a
    /// runner with no checks when <paramref name="configPath"/> is null or whitespace (agent has no
    /// checks configured). Throws <see cref="FileNotFoundException"/> when a non-blank path is
    /// supplied but the file does not exist — callers must treat this as a configuration error and
    /// abort rather than proceeding without checks.
    /// </summary>
    public static ExternalCheckRunner BuildRunner(
        string? configPath, HttpClient httpClient, ILoggerFactory? loggerFactory = null)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return new ExternalCheckRunner([], loggerFactory?.CreateLogger(typeof(ExternalCheckRunner).FullName!));

        if (!File.Exists(configPath))
            throw new FileNotFoundException($"Checks config file not found: {configPath}", configPath);

        var config = ChecksConfig.Load(configPath);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".";
        return BuildRunner(config, baseDir, httpClient, loggerFactory);
    }

    /// <summary>Builds a runner from an in-memory <paramref name="config"/>.</summary>
    public static ExternalCheckRunner BuildRunner(
        ChecksConfig config, string baseDir, HttpClient httpClient, ILoggerFactory? loggerFactory = null)
    {
        var checks = config.Checks.Select(def => Build(def, baseDir, httpClient, loggerFactory)).ToArray();
        return new ExternalCheckRunner(checks, loggerFactory?.CreateLogger(typeof(ExternalCheckRunner).FullName!));
    }

    /// <summary>Builds a single check from its definition.</summary>
    public static IExternalCheck Build(
        CheckDefinition def, string baseDir, HttpClient httpClient, ILoggerFactory? loggerFactory = null)
    {
        return def.Type switch
        {
            "email-verified" => new EmailVerifiedCheck(def.Name, def.Field, def.EmailField),
            "field-present" => new FieldPresentCheck(def.Name, RequireField(def)),
            "profanity" => new ProfanityCheck(def.Name, def.Fields ?? [], LoadWordlist(def, baseDir, loggerFactory)),
            "uk-postcode" => new PostcodeExistsCheck(
                def.Name,
                def.AddressField ?? "/address",
                httpClient,
                LoadPostcodeFixture(def, baseDir),
                ParseOfflineMode(def.OfflineMode),
                def.BaseUrl,
                loggerFactory?.CreateLogger(typeof(PostcodeExistsCheck).FullName!)),
            _ => throw new NotSupportedException($"Unknown check type '{def.Type}' for check '{def.Name}'")
        };
    }

    private static string RequireField(CheckDefinition def) =>
        def.Field ?? throw new InvalidOperationException($"Check '{def.Name}' (field-present) requires 'field'");

    private static PostcodeOfflineMode ParseOfflineMode(string? mode) => mode?.ToLowerInvariant() switch
    {
        "always" => PostcodeOfflineMode.Always,
        "never" => PostcodeOfflineMode.Never,
        _ => PostcodeOfflineMode.Auto
    };

    private static IEnumerable<string> LoadWordlist(CheckDefinition def, string baseDir, ILoggerFactory? loggerFactory)
    {
        var words = new List<string>();
        if (def.WordlistInline is not null)
            words.AddRange(def.WordlistInline);

        if (!string.IsNullOrWhiteSpace(def.WordlistFile))
        {
            var path = ResolvePath(baseDir, def.WordlistFile);
            if (File.Exists(path))
            {
                words.AddRange(File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#')));
            }
            else
            {
                // A declared wordlist file that is absent silently produces an empty wordlist,
                // which causes ProfanityCheck to always return false — log prominently so operators
                // see the misconfiguration rather than discovering it through missed rejections.
                loggerFactory?.CreateLogger(typeof(ExternalCheckFactory).FullName!)
                    .LogWarning(
                        "Profanity check '{CheckName}': declared wordlistFile '{Path}' not found — " +
                        "check will not fire. Verify the path is correct and the file is deployed.",
                        def.Name, path);
            }
        }

        return words;
    }

    private static IEnumerable<string> LoadPostcodeFixture(CheckDefinition def, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(def.OfflineFixture))
            return [];

        var path = ResolvePath(baseDir, def.OfflineFixture);
        if (!File.Exists(path))
            return [];

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("postcodes", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        return arr.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToArray();
    }

    private static string ResolvePath(string baseDir, string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDir, path));
}
