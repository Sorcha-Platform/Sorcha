// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sorcha.CitizenWallet.Abstractions.Constants;

namespace Sorcha.Blueprint.Models.Tests.Credentials;

/// <summary>
/// Completeness guarantee for the credential-VCT-decoupling work: walks every blueprint JSON in the
/// corpus and asserts that each platform credential type uses its canonical <see cref="VctUris"/> URI.
///
/// It locates credential nodes STRUCTURALLY — recursively finding every property named
/// <c>credentialIssuanceConfig</c> (an object) or <c>credentialRequirements</c> (an array). It never
/// scans generic JSON-Schema <c>"type"</c> keys (blueprint JSON is full of <c>"type":"object"/"string"</c>),
/// so schema definitions cannot be mistaken for credential types.
/// </summary>
public class BlueprintVctConformanceTests
{
    /// <summary>
    /// Bare credential-type name (as written in blueprint JSON) → canonical <see cref="VctUris"/> URI.
    /// Values reference the real constants (never hardcoded URIs) — that is the anti-drift point.
    /// </summary>
    private static readonly Dictionary<string, string> Canonical = new(StringComparer.Ordinal)
    {
        ["AssuredIdentityCredential"] = VctUris.AssuredIdentityV1,
        ["DrivingLicenceCredential"] = VctUris.DrivingLicenceV1,
        ["BlueBadgeCredential"] = VctUris.BlueBadgeV1,
        ["MembershipCredential"] = VctUris.MembershipV1,
        ["LicenseCredential"] = VctUris.LicenceV1,
        ["CouncilDigitalIdCredential"] = VctUris.CouncilDigitalIdV1,
        ["VerifiedInvoiceCredential"] = VctUris.VerifiedInvoiceV1,
        ["TradeFinanceCredential"] = VctUris.TradeFinanceV1,
        ["PlanningPermissionCredential"] = VctUris.PlanningPermissionV1,
        ["BuildingWarrantCredential"] = VctUris.BuildingWarrantV1,
        ["CompletionCertificateCredential"] = VctUris.CompletionCertificateV1,
        ["JobAssignmentCredential"] = VctUris.JobAssignmentV1,
        ["ServiceCompletionCredential"] = VctUris.ServiceCompletionV1,
        ["ForestProductDPPCredential"] = VctUris.ForestProductDppV1,
        ["CyberEssentialsUacPosture"] = VctUris.CyberEssentialsUacV1,
        ["RefurbishmentCertificateCredential"] = VctUris.RefurbishmentCertificateV1,
        ["BuildingPermitCredential"] = VctUris.BuildingPermitV1,
        ["CyberLevelCredential"] = VctUris.CyberLevelV1,
        ["CredentialLifecycleConformance"] = VctUris.CredentialLifecycleConformanceV1,
    };

    private static readonly HashSet<string> CanonicalUris = new(Canonical.Values, StringComparer.Ordinal);

    private static readonly Regex VersionSuffix = new(@"/v\d+$", RegexOptions.Compiled);

    [Fact]
    public void EveryBlueprintCredentialType_UsesCanonicalVctUri_NoBareNamesRemain()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        var searchDirs = new[] { "demos", "walkthroughs", "blueprints" };

        var violations = new List<string>();

        foreach (var relDir in searchDirs)
        {
            var dir = Path.Combine(repoRoot, relDir);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                JsonNode? root;
                try
                {
                    root = JsonNode.Parse(File.ReadAllText(file));
                }
                catch
                {
                    // A non-blueprint / malformed .json under these dirs is not a violation — skip it.
                    continue;
                }

                if (root is not JsonObject)
                {
                    continue;
                }

                var relFile = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                Walk(root, "$", relFile, violations);
            }
        }

        violations.Should().BeEmpty(
            "every platform credential type must use its canonical VctUris URI. Unconverted sites:\n"
            + string.Join("\n", violations));
    }

    /// <summary>
    /// Recursively descends the node tree. Whenever it meets a property named
    /// <c>credentialIssuanceConfig</c> (object) or <c>credentialRequirements</c> (array) it inspects that
    /// credential node. All other keys — including JSON-Schema <c>"type"</c> keys — are only recursed into,
    /// never interpreted as credential types.
    /// </summary>
    private static void Walk(JsonNode? node, string path, string relFile, List<string> violations)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kv in obj)
                {
                    var childPath = $"{path}.{kv.Key}";

                    if (kv.Key == "credentialIssuanceConfig" && kv.Value is JsonObject cfg)
                    {
                        InspectIssuance(cfg, childPath, relFile, violations);
                    }
                    else if (kv.Key == "credentialRequirements" && kv.Value is JsonArray reqs)
                    {
                        InspectRequirements(reqs, childPath, relFile, violations);
                    }

                    Walk(kv.Value, childPath, relFile, violations);
                }

                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    Walk(arr[i], $"{path}[{i}]", relFile, violations);
                }

                break;
        }
    }

    private static void InspectIssuance(JsonObject cfg, string path, string relFile, List<string> violations)
    {
        var credentialType = (cfg["credentialType"] as JsonValue)?.GetValue<string>();
        var vct = (cfg["vct"] as JsonValue)?.GetValue<string>();

        var (known, expected) = Resolve(credentialType);

        // A credentialIssuanceConfig with neither a credentialType nor a vct has nothing to check.
        if (string.IsNullOrEmpty(credentialType) && string.IsNullOrEmpty(vct))
        {
            return;
        }

        if (string.IsNullOrEmpty(vct))
        {
            // credentialType is populated but not recognised, and there is no vct to fall back on — this is
            // either a brand-new platform type that hasn't been added to VctUris/Canonical yet, or a typo.
            // Either way it must be surfaced, not silently skipped, so future bare types can't slip through.
            violations.Add(known
                ? $"{relFile}: {path} — credentialType '{credentialType}' is missing its canonical 'vct' (expected '{expected}')"
                : $"{relFile}: {path} — credentialType '{credentialType}' is not a known VctUris type — add it to VctUris + the Canonical map");
            return;
        }

        if (known)
        {
            if (!string.Equals(vct, expected, StringComparison.Ordinal))
            {
                violations.Add($"{relFile}: {path} — vct '{vct}' does not equal canonical '{expected}' for credentialType '{credentialType}'");
            }
        }
        else if (!CanonicalUris.Contains(vct))
        {
            violations.Add($"{relFile}: {path} — vct '{vct}' is not a recognised canonical VctUris value");
        }
    }

    private static void InspectRequirements(JsonArray reqs, string path, string relFile, List<string> violations)
    {
        for (var i = 0; i < reqs.Count; i++)
        {
            if (reqs[i] is not JsonObject req)
            {
                continue;
            }

            var type = (req["type"] as JsonValue)?.GetValue<string>();
            var (known, expected) = Resolve(type);

            if (known && !string.Equals(type, expected, StringComparison.Ordinal))
            {
                violations.Add($"{path}[{i}].type — requirement type '{type}' should be canonical '{expected}' (in {relFile})");
            }
        }
    }

    /// <summary>
    /// Resolves a raw credential-type string to whether it names a known platform type and its canonical URI.
    /// Handles the bare name, the <c>&lt;BareName&gt;/vN</c> suffix form, and an already-canonical URI.
    /// </summary>
    private static (bool Known, string? Expected) Resolve(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return (false, null);
        }

        // Already a URI: canonical iff it is one of the known VctUris values. Do NOT strip a trailing
        // /vN from a URI (that would mangle ".../assured-identity/v1").
        if (raw.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return CanonicalUris.Contains(raw) ? (true, raw) : (false, null);
        }

        var bare = VersionSuffix.Replace(raw, string.Empty);
        return Canonical.TryGetValue(bare, out var uri) ? (true, uri) : (false, null);
    }

    private static string FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "Sorcha.sln")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repo root (dir containing Sorcha.sln) walking up from '{startDir}'.");
    }
}
