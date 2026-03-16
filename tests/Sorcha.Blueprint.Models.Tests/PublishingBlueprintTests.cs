// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Models.Tests;

public class PublishingBlueprintTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static JsonElement LoadTemplate()
    {
        // Navigate to the blueprints/templates directory from the test output
        var baseDir = AppContext.BaseDirectory;
        var repoRoot = FindRepoRoot(baseDir);
        var templatePath = Path.Combine(repoRoot, "blueprints", "templates", "blueprint-publishing-v1.json");

        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Publishing blueprint template not found at {templatePath}");
        }

        var json = File.ReadAllText(templatePath);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("template").Clone();
    }

    private static string FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")) ||
                Directory.Exists(Path.Combine(dir, "blueprints")))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        // Fallback: walk up from bin/Debug/net10.0
        return Path.GetFullPath(Path.Combine(startDir, "..", "..", "..", "..", "..", ".."));
    }

    [Fact]
    public void Template_HasThreeParticipants()
    {
        var template = LoadTemplate();
        var participants = template.GetProperty("participants");
        participants.GetArrayLength().Should().Be(3);

        var names = new List<string>();
        foreach (var p in participants.EnumerateArray())
        {
            names.Add(p.GetProperty("name").GetString()!);
        }

        names.Should().Contain("Author");
        names.Should().Contain("Reviewer");
        names.Should().Contain("Publisher");
    }

    [Fact]
    public void Template_HasFiveActions()
    {
        var template = LoadTemplate();
        var actions = template.GetProperty("actions");
        actions.GetArrayLength().Should().Be(5);
    }

    [Fact]
    public void Template_FirstActionIsStarting()
    {
        var template = LoadTemplate();
        var firstAction = template.GetProperty("actions")[0];
        firstAction.GetProperty("isStartingAction").GetBoolean().Should().BeTrue();
        firstAction.GetProperty("title").GetString().Should().Be("Submit Draft");
    }

    [Fact]
    public void Template_ClassifyRoutes_SplitByChangeType()
    {
        var template = LoadTemplate();
        var classifyAction = template.GetProperty("actions")[1];
        classifyAction.GetProperty("title").GetString().Should().Be("Classify Change");

        var routes = classifyAction.GetProperty("routes");
        routes.GetArrayLength().Should().Be(2, "should have structural and documentation routes");
    }

    [Fact]
    public void Template_ReviewActions_HaveApproveAndRejectRoutes()
    {
        var template = LoadTemplate();

        // Full Review (action 2) and Documentation Review (action 3) both have approve/reject
        foreach (var actionIndex in new[] { 2, 3 })
        {
            var action = template.GetProperty("actions")[actionIndex];
            var routes = action.GetProperty("routes");
            routes.GetArrayLength().Should().Be(2,
                $"action {actionIndex} should have approve and reject routes");

            // One route should go to action 4 (Sign & Publish)
            var routeDescriptions = new List<string>();
            foreach (var route in routes.EnumerateArray())
            {
                if (route.TryGetProperty("description", out var desc))
                    routeDescriptions.Add(desc.GetString()!);
            }

            routeDescriptions.Should().Contain(d => d.Contains("Approved") || d.Contains("approved"));
            routeDescriptions.Should().Contain(d => d.Contains("Rejected") || d.Contains("rejected") || d.Contains("return"));
        }
    }

    [Fact]
    public void Template_RejectRoutes_CycleBackToSubmit()
    {
        var template = LoadTemplate();

        // Full Review reject → action 0 (Submit Draft)
        var fullReview = template.GetProperty("actions")[2];
        var fullReviewRoutes = fullReview.GetProperty("routes");
        var rejectRoute = GetDefaultRoute(fullReviewRoutes);
        rejectRoute.Should().NotBeNull();
        GetNextActionIds(rejectRoute!.Value).Should().Contain(0, "reject should cycle back to Submit Draft");

        // Doc Review reject → action 0
        var docReview = template.GetProperty("actions")[3];
        var docReviewRoutes = docReview.GetProperty("routes");
        var docReject = GetDefaultRoute(docReviewRoutes);
        docReject.Should().NotBeNull();
        GetNextActionIds(docReject!.Value).Should().Contain(0);
    }

    [Fact]
    public void Template_SignAndPublish_IsTerminal()
    {
        var template = LoadTemplate();
        var signAction = template.GetProperty("actions")[4];
        signAction.GetProperty("title").GetString().Should().Be("Sign & Publish");

        var routes = signAction.GetProperty("routes");
        var route = routes[0];
        var nextIds = route.GetProperty("nextActionIds");
        nextIds.GetArrayLength().Should().Be(0, "terminal action should have empty nextActionIds");
    }

    [Fact]
    public void Template_HasDisclosuresOnAllActions()
    {
        var template = LoadTemplate();
        var actions = template.GetProperty("actions");

        foreach (var action in actions.EnumerateArray())
        {
            action.TryGetProperty("disclosures", out var disclosures).Should().BeTrue(
                $"action '{action.GetProperty("title").GetString()}' should have disclosures");
            disclosures.GetArrayLength().Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Template_HasInstructions()
    {
        var template = LoadTemplate();
        template.TryGetProperty("instructions", out var instructions).Should().BeTrue();
        instructions.TryGetProperty("overview", out _).Should().BeTrue();
        instructions.TryGetProperty("locale", out var locale).Should().BeTrue();
        locale.GetString().Should().Be("en-GB");
    }

    [Fact]
    public void Template_HasGovernanceRoles()
    {
        var template = LoadTemplate();
        var instructions = template.GetProperty("instructions");
        instructions.TryGetProperty("governanceRoles", out var roles).Should().BeTrue();
        roles.TryGetProperty("reviewer", out _).Should().BeTrue();
        roles.TryGetProperty("publisher", out _).Should().BeTrue();
    }

    private static JsonElement? GetDefaultRoute(JsonElement routes)
    {
        foreach (var route in routes.EnumerateArray())
        {
            if (route.TryGetProperty("isDefault", out var isDefault) && isDefault.GetBoolean())
                return route;
        }
        return null;
    }

    private static List<int> GetNextActionIds(JsonElement route)
    {
        var ids = new List<int>();
        if (route.TryGetProperty("nextActionIds", out var nextIds))
        {
            foreach (var id in nextIds.EnumerateArray())
            {
                ids.Add(id.GetInt32());
            }
        }
        return ids;
    }
}
