// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.Blueprint.Service.Tests.Integration;
using Sorcha.ServiceClients.Register;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Endpoints;

/// <summary>
/// Feature 142 (T054 / US6) — integration tests for <c>POST /api/blueprints/from-published</c>
/// (the amend / clone-to-draft endpoint). Verifies the four documented behaviours:
/// <list type="bullet">
/// <item>201 returns a NEW draft id (≠ source) carrying lineage metadata.</item>
/// <item>Source-register governance is enforced (403 when the caller lacks rights).</item>
/// <item>Unknown (registerId, blueprintId, version) → 404.</item>
/// <item>The amend draft is a structural clone — same title/participants/actions/schemas.</item>
/// </list>
/// </summary>
public class FromPublishedEndpointTests : IClassFixture<BlueprintServiceWebApplicationFactory>
{
    private readonly BlueprintServiceWebApplicationFactory _factory;
    private readonly HttpClient _client;

    /// <summary>
    /// Lineage metadata keys carried on the amend draft so the designer can repopulate
    /// <c>LifecycleState.AmendContext</c> when the new draft is opened. Mirror keys must
    /// stay aligned with <c>DesignerContext.SetBlueprint</c> in the UI.
    /// </summary>
    public const string SourceRegisterMetadataKey = "x-source-register";
    public const string SourceBlueprintMetadataKey = "x-source-blueprint-id";
    public const string SourceVersionMetadataKey = "x-source-version";

    public FromPublishedEndpointTests(BlueprintServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task FromPublished_KnownVersion_Returns201AndNewDraftIdWithLineage()
    {
        // Arrange — create + publish a source blueprint so the endpoint has something to clone.
        var source = await CreateAndPublishSourceAsync("from-pub-known", "registers-known-source");

        // Act — clone-to-draft.
        var response = await _client.PostAsJsonAsync(
            "/api/blueprints/from-published",
            new
            {
                registerId = source.RegisterId,
                blueprintId = source.BlueprintId,
                version = source.Version,
            });

        // Assert — 201 + lineage on a NEW draft.
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CloneResponseDto>();
        body.Should().NotBeNull();
        body!.DraftBlueprintId.Should().NotBeNullOrWhiteSpace();
        body.DraftBlueprintId.Should().NotBe(source.BlueprintId,
            "the amend flow MUST yield a fresh draft id distinct from the published source.");
        body.SourceVersion.Should().Be(source.Version);
        body.RegisterId.Should().Be(source.RegisterId);

        // The draft persisted on the draft store carries the lineage metadata so the designer can
        // repopulate AmendContext on open.
        var draftResponse = await _client.GetAsync($"/api/blueprints/{body.DraftBlueprintId}");
        draftResponse.EnsureSuccessStatusCode();
        var draft = await draftResponse.Content.ReadFromJsonAsync<BlueprintModel>();
        draft.Should().NotBeNull();
        draft!.Metadata.Should().NotBeNull("the amend draft MUST carry lineage metadata.");
        draft.Metadata!.Should().ContainKey(SourceRegisterMetadataKey)
            .WhoseValue.Should().Be(source.RegisterId);
        draft.Metadata.Should().ContainKey(SourceBlueprintMetadataKey)
            .WhoseValue.Should().Be(source.BlueprintId);
        draft.Metadata.Should().ContainKey(SourceVersionMetadataKey)
            .WhoseValue.Should().Be(source.Version.ToString());
    }

    [Fact]
    public async Task FromPublished_CallerLacksGovernance_Returns403()
    {
        var source = await CreateAndPublishSourceAsync("from-pub-403", "registers-403-source");

        // Replace the per-test register-client governance response so the caller's org_id no longer
        // matches the roster subject. Substring-match means swapping out the embedded org id is enough.
        using var scope = _factory.Services.CreateScope();
        var mockRegisterClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();
        Mock.Get(mockRegisterClient)
            .Setup(c => c.GetGovernanceRosterAsync(source.RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GovernanceRosterResponse
            {
                RegisterId = source.RegisterId,
                MemberCount = 1,
                Members =
                {
                    new RosterMember
                    {
                        // A different org → the test principal does not match the subject.
                        Subject = "did:sorcha:org:99999999-9999-9999-9999-999999999999",
                        Role = "Owner",
                        Algorithm = "ED25519",
                        GrantedAt = DateTimeOffset.UtcNow,
                    }
                },
            });

        try
        {
            var response = await _client.PostAsJsonAsync(
                "/api/blueprints/from-published",
                new { registerId = source.RegisterId, blueprintId = source.BlueprintId, version = source.Version });

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "the source-register governance check MUST refuse callers without a publish-governance role.");
        }
        finally
        {
            // Restore the default roster mock so this test does not pollute the shared fixture.
            Mock.Get(mockRegisterClient)
                .Setup(c => c.GetGovernanceRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string regId, CancellationToken _) => new GovernanceRosterResponse
                {
                    RegisterId = regId,
                    MemberCount = 1,
                    Members =
                    {
                        new RosterMember
                        {
                            Subject = "did:sorcha:org:00000000-0000-0000-0000-000000000456",
                            Role = "Owner",
                            Algorithm = "ED25519",
                            GrantedAt = DateTimeOffset.UtcNow,
                        }
                    },
                });
        }
    }

    [Fact]
    public async Task FromPublished_UnknownTriple_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/blueprints/from-published",
            new
            {
                registerId = "registers-unknown-from-pub",
                blueprintId = $"missing-{Guid.NewGuid():N}",
                version = 1,
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an unknown (registerId, blueprintId, version) triple MUST resolve to 404 — no draft is written.");
    }

    [Fact]
    public async Task FromPublished_StructuralClone_TitleParticipantsActionsMatchSource()
    {
        var source = await CreateAndPublishSourceAsync("from-pub-clone", "registers-clone-source");

        var response = await _client.PostAsJsonAsync(
            "/api/blueprints/from-published",
            new { registerId = source.RegisterId, blueprintId = source.BlueprintId, version = source.Version });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CloneResponseDto>();
        body.Should().NotBeNull();

        var draftResponse = await _client.GetAsync($"/api/blueprints/{body!.DraftBlueprintId}");
        draftResponse.EnsureSuccessStatusCode();
        var draft = await draftResponse.Content.ReadFromJsonAsync<BlueprintModel>();
        draft.Should().NotBeNull();

        // The clone preserves the executable structure (title, participants, actions) so the
        // administrator amends, rather than restarts. Version resets so the new draft is "v1 of
        // the amend"; the published lineage lives in metadata.
        draft!.Title.Should().Be(source.Title);
        draft.Participants.Should().HaveCount(2);
        draft.Actions.Should().HaveCount(2);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<PublishedSource> CreateAndPublishSourceAsync(string titleSeed, string registerId)
    {
        var blueprint = new
        {
            title = $"{titleSeed}-{Guid.NewGuid():N}",
            description = "Amend-loop source blueprint.",
            version = 1,
            participants = new object[]
            {
                new { id = "applicant", name = "Applicant" },
                new { id = "reviewer", name = "Reviewer" },
            },
            actions = new object[]
            {
                new
                {
                    id = 0,
                    title = "Submit",
                    sender = "applicant",
                    isStartingAction = true,
                    dataSchemas = new object[]
                    {
                        new
                        {
                            type = "object",
                            properties = new { message = new { type = "string", minLength = 1 } },
                            required = new[] { "message" },
                        },
                    },
                    disclosures = new object[]
                    {
                        new { participantAddress = "reviewer", dataPointers = new[] { "/*" } },
                    },
                    routes = new object[]
                    {
                        new { id = "r", nextActionIds = new[] { 1 }, isDefault = true },
                    },
                },
                new
                {
                    id = 1,
                    title = "Review",
                    sender = "reviewer",
                    dataSchemas = new object[]
                    {
                        new
                        {
                            type = "object",
                            properties = new { decision = new { type = "string", @enum = new[] { "approved", "rejected" } } },
                            required = new[] { "decision" },
                        },
                    },
                    disclosures = new object[]
                    {
                        new { participantAddress = "applicant", dataPointers = new[] { "/*" } },
                    },
                    routes = new object[]
                    {
                        new { id = "d", nextActionIds = Array.Empty<int>(), isDefault = true },
                    },
                },
            },
        };

        var createResponse = await _client.PostAsJsonAsync("/api/blueprints", blueprint);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<BlueprintModel>();
        created.Should().NotBeNull();

        var publishResponse = await _client.PostAsJsonAsync(
            $"/api/blueprints/{created!.Id}/publish",
            new { registerId });
        publishResponse.EnsureSuccessStatusCode();
        var publishBody = await publishResponse.Content.ReadFromJsonAsync<JsonElement>();
        var version = publishBody.GetProperty("version").GetInt32();

        return new PublishedSource(created.Id, version, registerId, (string)blueprint.title);
    }

    private sealed record PublishedSource(string BlueprintId, int Version, string RegisterId, string Title);

    private sealed record CloneResponseDto(
        string DraftBlueprintId,
        int SourceVersion,
        string RegisterId);
}
