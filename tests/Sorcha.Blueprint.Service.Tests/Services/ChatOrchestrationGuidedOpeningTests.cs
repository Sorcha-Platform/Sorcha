// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Chat;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Templates;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 142 US4 (T042 / T043 / FR-010 / FR-012) — verifies <see cref="ChatOrchestrationService"/>
/// opens as a guided interviewer for brand-new sessions and recognises the directed-build
/// sentinel <c>__directed-start:&lt;id&gt;</c> to seed a deterministic starting blueprint.
/// </summary>
public class ChatOrchestrationGuidedOpeningTests
{
    private readonly Mock<IChatSessionStore> _sessionStore = new();
    private readonly Mock<IAIProviderService> _aiProvider = new();
    private readonly Mock<IBlueprintToolExecutor> _toolExecutor = new();
    private readonly Mock<IBlueprintStore> _blueprintStore = new();
    private readonly Mock<ISchemaIndexService> _schemaIndex = new();
    private readonly Mock<IBlueprintTemplateService> _templates = new();
    private readonly Mock<ILogger<ChatOrchestrationService>> _logger = new();
    private readonly ChatOrchestrationService _service;

    public ChatOrchestrationGuidedOpeningTests()
    {
        _schemaIndex
            .Setup(s => s.SearchAsync(
                It.IsAny<string?>(), It.IsAny<string[]?>(), It.IsAny<string?>(),
                It.IsAny<Sorcha.Blueprint.Schemas.Models.SchemaIndexStatus?>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemaIndexSearchResponse(
                Results: Array.Empty<SchemaIndexEntryDto>(),
                TotalCount: 0,
                NextCursor: null,
                LoadingProviders: null));

        _templates
            .Setup(s => s.GetPublishedTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sorcha.Blueprint.Models.BlueprintTemplate>());

        _toolExecutor.Setup(t => t.GetToolDefinitions()).Returns([]);

        _service = new ChatOrchestrationService(
            _sessionStore.Object, _aiProvider.Object, _toolExecutor.Object,
            _blueprintStore.Object, _schemaIndex.Object, _templates.Object, _logger.Object);
    }

    [Fact]
    public async Task NoBlueprintSession_FirstTurn_SystemPromptIncludesGuidedOpeningSection()
    {
        var session = NewSession("s-guide-1");
        WireSession(session);

        var capturedPrompt = await CaptureSystemPromptAsync(session.Id, "Hello");

        // The guided-opening section is what differentiates US4 (FR-010) from the existing
        // consultative prompt — it tells the assistant to behave as an interviewer for new services.
        capturedPrompt.Should().NotBeNullOrEmpty();
        capturedPrompt!.Should().Contain("Guided Opening",
            "the system prompt must include a Guided Opening section for brand-new sessions (FR-010)");
        capturedPrompt.Should().Contain("directed-build",
            "the guided opening should reference the directed-build choices the chips correspond to");
    }

    [Fact]
    public async Task EditingExistingBlueprint_SystemPromptOmitsGuidedOpeningSection()
    {
        var session = NewSession("s-edit-1");
        session.BlueprintDraft = new Sorcha.Blueprint.Models.Blueprint
        {
            Id = "existing-bp",
            Title = "Existing Workflow",
            Description = "An existing blueprint already being edited.",
            Version = 1,
            Participants =
            [
                new Sorcha.Blueprint.Models.Participant { Id = "a", Name = "A" },
                new Sorcha.Blueprint.Models.Participant { Id = "b", Name = "B" },
            ],
        };
        WireSession(session);

        var capturedPrompt = await CaptureSystemPromptAsync(session.Id, "Add a date field");

        capturedPrompt.Should().NotBeNullOrEmpty();
        capturedPrompt!.Should().NotContain("Guided Opening",
            "once a blueprint is loaded the assistant is in edit mode, not in the on-ramp");
        capturedPrompt.Should().Contain("Current Blueprint Being Edited",
            "edit-flow context still applies");
    }

    [Fact]
    public async Task DirectedStartSentinel_KnownStarter_SeedsBlueprintAndEmitsUpdate()
    {
        var session = NewSession("s-directed-1");
        WireSession(session);

        Sorcha.Blueprint.Models.Blueprint? emittedBlueprint = null;

        await _service.ProcessMessageAsync(
            session.Id,
            "__directed-start:grant",
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (bp, _) => { emittedBlueprint = bp; return Task.CompletedTask; });

        emittedBlueprint.Should().NotBeNull(
            "the directed-start sentinel must seed a starting blueprint and emit a BlueprintUpdated event without invoking the AI");
        emittedBlueprint!.Participants.Count.Should().BeGreaterOrEqualTo(2);
        emittedBlueprint.Actions.Should().Contain(a => a.IsStartingAction);

        // No AI round-trip needed for the sentinel — the seed is deterministic.
        _aiProvider.Verify(
            a => a.StreamCompletionAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<IReadOnlyList<ToolDefinition>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DirectedStartSentinel_UnknownStarter_FallsBackToNormalAiTurn()
    {
        var session = NewSession("s-directed-bad");
        WireSession(session);

        string? capturedPrompt = null;
        _aiProvider
            .Setup(a => a.StreamCompletionAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyList<ToolDefinition>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyList<ToolDefinition>, string, CancellationToken>(
                (_, _, prompt, _) => capturedPrompt = prompt)
            .Returns(EmptyStream());

        await _service.ProcessMessageAsync(
            session.Id,
            "__directed-start:not-a-real-starter",
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        // Unknown sentinel must not seed a blueprint or short-circuit — it falls through to the AI.
        capturedPrompt.Should().NotBeNull(
            "an unrecognised __directed-start id should fall through to the normal AI turn");
    }

    // ----------------- helpers -----------------

    private static ChatSession NewSession(string id) => new()
    {
        Id = id,
        UserId = "user-1",
        OrganizationId = "org-1",
        Status = SessionStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        LastActivityAt = DateTimeOffset.UtcNow,
    };

    private void WireSession(ChatSession session)
    {
        _sessionStore.Setup(s => s.GetSessionAsync(session.Id)).ReturnsAsync(session);
        _sessionStore.Setup(s => s.GetMessagesAsync(session.Id)).ReturnsAsync([]);
        _sessionStore.Setup(s => s.AddMessageAsync(session.Id, It.IsAny<ChatMessage>())).Returns(Task.CompletedTask);
        _sessionStore.Setup(s => s.UpdateSessionAsync(It.IsAny<ChatSession>())).Returns(Task.CompletedTask);
    }

    private async Task<string?> CaptureSystemPromptAsync(string sessionId, string userMessage)
    {
        string? captured = null;
        _aiProvider
            .Setup(a => a.StreamCompletionAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyList<ToolDefinition>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyList<ToolDefinition>, string, CancellationToken>(
                (_, _, prompt, _) => captured = prompt)
            .Returns(EmptyStream());

        await _service.ProcessMessageAsync(sessionId, userMessage,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        return captured;
    }

    private static async IAsyncEnumerable<AIStreamEvent> EmptyStream()
    {
        yield return new StreamEnd();
        await Task.CompletedTask;
    }
}
