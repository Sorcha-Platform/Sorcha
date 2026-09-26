// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sorcha.Blueprint.Models;
using Xunit;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// #1644 — the disclosure preview (POST /api/execution/disclose, behind sorcha_disclosure_analysis)
/// applied disclosure rules to the RAW input, while real execution applies them to the payload WITH
/// its calculated fields. A calculated field disclosed to a participant never appeared in the
/// preview, so the preview a designer checks their privacy boundaries against disagreed with what
/// the participant would actually receive.
/// </summary>
public class DisclosurePreviewTests : IClassFixture<BlueprintServiceWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DisclosurePreviewTests(BlueprintServiceWebApplicationFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Disclose_IncludesCalculatedFields_AsRealExecutionDoes()
    {
        var blueprint = new BlueprintModel
        {
            Title = "Invoice",
            Description = "Disclosure preview over a calculated field",
            Participants =
            [
                new ParticipantModel { Id = "seller", Name = "Seller" },
                new ParticipantModel { Id = "buyer", Name = "Buyer" },
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Issue invoice",
                    Description = "Seller issues an invoice",
                    Sender = "seller",
                    Calculations = new Dictionary<string, JsonNode>
                    {
                        ["total"] = JsonNode.Parse("""{"*": [{"var": "quantity"}, {"var": "unitPrice"}]}""")!,
                    },
                    Disclosures =
                    [
                        new Disclosure { ParticipantAddress = "buyer", DataPointers = ["/total"] },
                    ],
                },
            ],
        };
        var created = await (await _client.PostAsJsonAsync("/api/blueprints", blueprint))
            .EnsureSuccessStatusCode().Content.ReadFromJsonAsync<BlueprintModel>();

        var response = await _client.PostAsJsonAsync("/api/execution/disclose", new
        {
            blueprintId = created!.Id,
            actionId = "1",
            data = new Dictionary<string, object> { ["quantity"] = 3, ["unitPrice"] = 5 },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var buyer = body.GetProperty("disclosures").EnumerateArray()
            .Single(d => d.GetProperty("participantId").GetString() == "buyer");
        buyer.GetProperty("disclosedData").TryGetProperty("total", out var total)
            .Should().BeTrue("the buyer is disclosed /total, a calculated field, and real execution sends it");
        total.GetDouble().Should().Be(15);
    }
}
