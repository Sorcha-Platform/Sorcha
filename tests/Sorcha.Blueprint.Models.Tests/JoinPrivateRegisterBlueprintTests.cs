// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

using FluentAssertions;

using Xunit;

namespace Sorcha.Blueprint.Models.Tests;

/// <summary>
/// Spec master Phase 2 US11 — shape contract for the join-private-register
/// governance blueprint. Parses the JSON, confirms the on-ledger audit-trail
/// actions exist with the right sender participants and required schema fields.
/// The SystemRegisterBootstrapper seeds this blueprint at startup; if the shape
/// drifts, acceptance invitations will fail to create instances against it.
/// </summary>
public class JoinPrivateRegisterBlueprintTests
{
    private const string BlueprintId = "join-private-register-v1";

    private static JsonElement LoadTemplate()
    {
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = FindRepoRoot(baseDir);
        var path = Path.Combine(repoRoot, "blueprints", "templates", $"{BlueprintId}.json");
        File.Exists(path).Should().BeTrue($"blueprint catalog must carry {BlueprintId} at {path}");

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("template").Clone();
    }

    private static string FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git"))
                || Directory.Exists(Path.Combine(dir, "blueprints")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Path.GetFullPath(Path.Combine(startDir, "..", "..", "..", "..", "..", ".."));
    }

    [Fact]
    public void Template_IdentifiesAsJoinPrivateRegisterV1()
    {
        var template = LoadTemplate();
        template.GetProperty("id").GetString().Should().Be(BlueprintId);
        template.GetProperty("version").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Template_HasSourceAndTargetParticipants()
    {
        var template = LoadTemplate();
        var participants = template.GetProperty("participants");
        participants.GetArrayLength().Should().Be(2);

        var ids = new HashSet<string>();
        foreach (var p in participants.EnumerateArray())
            ids.Add(p.GetProperty("id").GetString()!);

        ids.Should().Contain("sourceOrg");
        ids.Should().Contain("targetOrg");
    }

    [Fact]
    public void Template_HasCreateAndAcceptActions()
    {
        var template = LoadTemplate();
        var actions = template.GetProperty("actions");
        actions.GetArrayLength().Should().Be(2);

        var create = actions[0];
        create.GetProperty("title").GetString().Should().Be("Create Invitation");
        create.GetProperty("sender").GetString().Should().Be("sourceOrg");
        create.GetProperty("isStartingAction").GetBoolean().Should().BeTrue();

        var accept = actions[1];
        accept.GetProperty("title").GetString().Should().Be("Accept Invitation");
        accept.GetProperty("sender").GetString().Should().Be("targetOrg");
    }

    [Fact]
    public void CreateAction_Schema_RequiresAuditFields()
    {
        var template = LoadTemplate();
        var create = template.GetProperty("actions")[0];
        var required = create.GetProperty("schema").GetProperty("required");

        var requiredFields = new HashSet<string>();
        foreach (var r in required.EnumerateArray())
            requiredFields.Add(r.GetString()!);

        requiredFields.Should().Contain("invitationId",
            "acceptance must be correlatable to a specific invitation record");
        requiredFields.Should().Contain("registerId");
        requiredFields.Should().Contain("sourceOrgDid");
        requiredFields.Should().Contain("targetOrgDid");
        requiredFields.Should().Contain("nonce",
            "nonce must be recorded on-ledger so replay detection spans nodes");
        requiredFields.Should().Contain("expiresAt");
    }

    [Fact]
    public void AcceptAction_Schema_ReferencesOriginalNonce()
    {
        var template = LoadTemplate();
        var accept = template.GetProperty("actions")[1];
        var required = accept.GetProperty("schema").GetProperty("required");

        var requiredFields = new HashSet<string>();
        foreach (var r in required.EnumerateArray())
            requiredFields.Add(r.GetString()!);

        requiredFields.Should().Contain("invitationId");
        requiredFields.Should().Contain("nonce",
            "acceptance MUST echo the nonce so a replay attempt is detectable on-chain");
    }

    [Fact]
    public void Template_RegisterIdPattern_Matches32HexChars()
    {
        // Both actions constrain registerId to 32-char lowercase hex, matching
        // Sorcha's canonical register ID format. A tighter pattern guards
        // against accidental cross-register transactions.
        var template = LoadTemplate();
        foreach (var action in template.GetProperty("actions").EnumerateArray())
        {
            var props = action.GetProperty("schema").GetProperty("properties");
            props.TryGetProperty("registerId", out var registerIdProp).Should().BeTrue();
            registerIdProp.GetProperty("pattern").GetString()
                .Should().Be("^[a-f0-9]{32}$");
        }
    }

    [Fact]
    public void Template_Metadata_MarksAsSystemGovernance()
    {
        // isSystem=true + category=governance are read by the bootstrapper to
        // decide publishing privileges. If these drift the blueprint ends up
        // in the non-privileged catalog and can't be seeded to the system register.
        var template = LoadTemplate();
        var metadata = template.GetProperty("metadata");
        metadata.GetProperty("category").GetString().Should().Be("governance");
        metadata.GetProperty("type").GetString().Should().Be("join-private-register");
        metadata.GetProperty("isSystem").GetString().Should().Be("true");
    }
}
