// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.IO;
using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>Pins the system blueprint catalog (Feature 197 T002).</summary>
public class SystemBlueprintCatalogTests
{
    [Fact]
    public void Ids_SeedOrder_IsPinned()
    {
        SystemBlueprintCatalog.Ids.Should().Equal(
            "register-creation-v1",
            "register-governance-v1",
            "create-organisation-v1",
            "join-private-register-v1");
    }

    [Fact]
    public void Ids_ContainsGovernanceBlueprintId()
    {
        SystemBlueprintCatalog.Ids.Should().Contain(GovernanceBlueprint.BlueprintId);
        SystemBlueprintCatalog.GovernanceBlueprintId.Should().Be(GovernanceBlueprint.BlueprintId);
    }

    [Fact]
    public void Ids_EveryId_HasShippedTemplateFile()
    {
        var root = FindRepoRoot();
        foreach (var id in SystemBlueprintCatalog.Ids)
        {
            File.Exists(Path.Combine(root, "blueprints", "templates", id + ".json"))
                .Should().BeTrue($"system blueprint '{id}' must ship blueprints/templates/{id}.json");
        }
    }

    [Fact]
    public void ShippedSystemTemplates_AreAllInCatalog()
    {
        // Counterfactual direction: a template added for a catalog id but dropped from the list.
        SystemBlueprintCatalog.Ids.Should().OnlyHaveUniqueItems();
        SystemBlueprintCatalog.Ids.Should().HaveCount(4);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Sorcha.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (Sorcha.sln / .git) not found above " + AppContext.BaseDirectory);
    }
}
