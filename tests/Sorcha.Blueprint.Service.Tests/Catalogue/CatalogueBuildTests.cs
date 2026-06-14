// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Sorcha.Blueprint.Service.Endpoints;
using Xunit;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using BpAction = Sorcha.Blueprint.Models.Action;
using Participant = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Catalogue;

/// <summary>
/// Feature B (154) — BuildCatalogue lists only startable services that have a register, mapped +
/// sorted by title.
/// </summary>
public sealed class CatalogueBuildTests
{
    private static PublishedBlueprint Pub(string id, string title, string? register, bool open) => new()
    {
        BlueprintId = id,
        RegisterId = register,
        Blueprint = new BlueprintModel
        {
            Title = title,
            Actions = [new BpAction { Id = 1, Sender = "p" }],
            Participants = [new Participant { Id = "p", WalletAddress = open ? null : "ws1qbound" }],
        },
    };

    [Fact]
    public void IncludesOnlyStartableWithRegister_SortedByTitle()
    {
        var published = new List<PublishedBlueprint>
        {
            Pub("b-zebra", "Zebra service", "reg-1", open: true),
            Pub("b-apple", "Apple service", "reg-1", open: true),
            Pub("b-bound", "Bound service", "reg-1", open: false),   // not startable
            Pub("b-noreg", "No-register service", null, open: true), // no register
        };

        var items = CatalogueEndpoints.BuildCatalogue(published);

        items.Select(i => i.Title).Should().Equal("Apple service", "Zebra service");
        items.Should().OnlyContain(i => i.RegisterId == "reg-1");
    }

    [Fact]
    public void MapsFields_TitleFallsBackToBlueprintId()
    {
        var p = Pub("b-1", "", "reg-1", open: true);
        var items = CatalogueEndpoints.BuildCatalogue(new[] { p });

        items.Should().ContainSingle();
        items[0].BlueprintId.Should().Be("b-1");
        items[0].Title.Should().Be("b-1", "blank title falls back to the blueprint id");
        items[0].RegisterId.Should().Be("reg-1");
    }
}
