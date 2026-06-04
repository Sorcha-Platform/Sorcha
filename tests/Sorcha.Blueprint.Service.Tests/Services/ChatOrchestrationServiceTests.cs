// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Models.Chat;
using Sorcha.Blueprint.Service.Services;
using Sorcha.Blueprint.Service.Templates;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="ChatOrchestrationService"/>.
/// </summary>
public class ChatOrchestrationServiceTests
{
    private readonly Mock<IChatSessionStore> _sessionStoreMock;
    private readonly Mock<IAIProviderService> _aiProviderMock;
    private readonly Mock<IBlueprintToolExecutor> _toolExecutorMock;
    private readonly Mock<IBlueprintStore> _blueprintStoreMock;
    private readonly Mock<ISchemaIndexService> _schemaIndexServiceMock;
    private readonly Mock<IBlueprintTemplateService> _templateServiceMock;
    private readonly Mock<ILogger<ChatOrchestrationService>> _loggerMock;
    private readonly ChatOrchestrationService _service;

    public ChatOrchestrationServiceTests()
    {
        _sessionStoreMock = new Mock<IChatSessionStore>();
        _aiProviderMock = new Mock<IAIProviderService>();
        _toolExecutorMock = new Mock<IBlueprintToolExecutor>();
        _blueprintStoreMock = new Mock<IBlueprintStore>();
        _schemaIndexServiceMock = new Mock<ISchemaIndexService>();
        _templateServiceMock = new Mock<IBlueprintTemplateService>();
        _loggerMock = new Mock<ILogger<ChatOrchestrationService>>();

        // Default: return empty schema search results so BuildSystemPromptAsync succeeds
        _schemaIndexServiceMock
            .Setup(s => s.SearchAsync(
                It.IsAny<string?>(),
                It.IsAny<string[]?>(),
                It.IsAny<string?>(),
                It.IsAny<Sorcha.Blueprint.Schemas.Models.SchemaIndexStatus?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Blueprint.Service.Models.SchemaIndexSearchResponse(
                Results: Array.Empty<Sorcha.Blueprint.Service.Models.SchemaIndexEntryDto>(),
                TotalCount: 0,
                NextCursor: null,
                LoadingProviders: null));

        _templateServiceMock
            .Setup(s => s.GetPublishedTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sorcha.Blueprint.Models.BlueprintTemplate>());

        _service = new ChatOrchestrationService(
            _sessionStoreMock.Object,
            _aiProviderMock.Object,
            _toolExecutorMock.Object,
            _blueprintStoreMock.Object,
            _schemaIndexServiceMock.Object,
            _templateServiceMock.Object,
            _loggerMock.Object);
    }

    #region CreateSessionAsync Tests

    [Fact]
    public async Task CreateSessionAsync_CreatesNewSession_WithValidUser()
    {
        // Arrange
        var user = CreateUser("user-123", "org-456");
        var expectedSession = CreateSession("session-789", "user-123", "org-456");

        _sessionStoreMock
            .Setup(s => s.GetActiveSessionForUserAsync("user-123"))
            .ReturnsAsync((ChatSession?)null);

        _sessionStoreMock
            .Setup(s => s.CreateSessionAsync("user-123", "org-456", null))
            .ReturnsAsync(expectedSession);

        // Act
        var result = await _service.CreateSessionAsync(user);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("session-789");
        result.UserId.Should().Be("user-123");
    }

    [Fact]
    public async Task CreateSessionAsync_ResumesExistingSession_WhenActive()
    {
        // Arrange
        var user = CreateUser("user-123", "org-456");
        var existingSession = CreateSession("existing-session", "user-123", "org-456");

        _sessionStoreMock
            .Setup(s => s.GetActiveSessionForUserAsync("user-123"))
            .ReturnsAsync(existingSession);

        // Act
        var result = await _service.CreateSessionAsync(user);

        // Assert
        result.Id.Should().Be("existing-session");
        _sessionStoreMock.Verify(s => s.CreateSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task CreateSessionAsync_LoadsExistingBlueprint_WhenProvided()
    {
        // Arrange
        var user = CreateUser("user-123", "org-456");
        var blueprintId = "blueprint-123";
        var existingBlueprint = BlueprintBuilder.Create()
            .WithId(blueprintId)
            .WithTitle("Existing Blueprint")
            .BuildDraft();

        var session = CreateSession("session-789", "user-123", "org-456");

        _sessionStoreMock
            .Setup(s => s.GetActiveSessionForUserAsync("user-123"))
            .ReturnsAsync((ChatSession?)null);

        _sessionStoreMock
            .Setup(s => s.CreateSessionAsync("user-123", "org-456", blueprintId))
            .ReturnsAsync(session);

        _blueprintStoreMock
            .Setup(s => s.GetAsync(blueprintId))
            .ReturnsAsync(existingBlueprint);

        _sessionStoreMock
            .Setup(s => s.UpdateSessionAsync(It.IsAny<ChatSession>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateSessionAsync(user, blueprintId);

        // Assert
        _blueprintStoreMock.Verify(s => s.GetAsync(blueprintId), Times.Once);
    }

    [Fact]
    public async Task CreateSessionAsync_ThrowsException_WhenUserIdMissing()
    {
        // Arrange
        var user = new ClaimsPrincipal(new ClaimsIdentity()); // No claims

        // Act
        var act = () => _service.CreateSessionAsync(user);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*User ID*");
    }

    #endregion

    #region GetSessionAsync Tests

    [Fact]
    public async Task GetSessionAsync_ReturnsSession_WhenExists()
    {
        // Arrange
        var sessionId = "session-123";
        var session = CreateSession(sessionId, "user-123", "org-456");

        _sessionStoreMock
            .Setup(s => s.GetSessionAsync(sessionId))
            .ReturnsAsync(session);

        // Act
        var result = await _service.GetSessionAsync(sessionId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(sessionId);
    }

    [Fact]
    public async Task GetSessionAsync_ReturnsNull_WhenNotExists()
    {
        // Arrange
        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("nonexistent"))
            .ReturnsAsync((ChatSession?)null);

        // Act
        var result = await _service.GetSessionAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region ProcessMessageAsync Tests

    [Fact]
    public async Task ProcessMessageAsync_ThrowsException_WhenSessionNotFound()
    {
        // Arrange
        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("nonexistent"))
            .ReturnsAsync((ChatSession?)null);

        // Act
        var act = () => _service.ProcessMessageAsync(
            "nonexistent",
            "hello",
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task ProcessMessageAsync_ThrowsException_WhenSessionExpired()
    {
        // Arrange - create an expired session with old CreatedAt via object initializer
        var session = new ChatSession
        {
            Id = "session-123",
            UserId = "user-123",
            OrganizationId = "org-456",
            Status = SessionStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-25), // Expired
            LastActivityAt = DateTimeOffset.UtcNow.AddHours(-25)
        };

        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("session-123"))
            .ReturnsAsync(session);

        // Act
        var act = () => _service.ProcessMessageAsync(
            "session-123",
            "hello",
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*expired*");
    }

    [Fact]
    public async Task ProcessMessageAsync_ThrowsException_WhenMessageEmpty()
    {
        // Arrange
        var session = CreateSession("session-123", "user-123", "org-456");

        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("session-123"))
            .ReturnsAsync(session);

        // Act
        var act = () => _service.ProcessMessageAsync(
            "session-123",
            "",
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public async Task ProcessMessageAsync_ThrowsException_WhenMessageTooLong()
    {
        // Arrange
        var session = CreateSession("session-123", "user-123", "org-456");
        var longMessage = new string('x', 10001);

        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("session-123"))
            .ReturnsAsync(session);

        // Act
        var act = () => _service.ProcessMessageAsync(
            "session-123",
            longMessage,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*too long*");
    }

    [Fact]
    public async Task ProcessMessageAsync_AddsUserMessageToStore()
    {
        // Arrange
        var session = CreateSession("session-123", "user-123", "org-456");
        ChatMessage? capturedMessage = null;

        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("session-123"))
            .ReturnsAsync(session);

        _sessionStoreMock
            .Setup(s => s.AddMessageAsync("session-123", It.IsAny<ChatMessage>()))
            .Callback<string, ChatMessage>((_, msg) => capturedMessage ??= msg)
            .Returns(Task.CompletedTask);

        _sessionStoreMock
            .Setup(s => s.GetMessagesAsync("session-123"))
            .ReturnsAsync([]);

        _toolExecutorMock
            .Setup(t => t.GetToolDefinitions())
            .Returns([]);

        _aiProviderMock
            .Setup(a => a.StreamCompletionAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<IReadOnlyList<ToolDefinition>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateEmptyStream());

        _sessionStoreMock
            .Setup(s => s.UpdateSessionAsync(It.IsAny<ChatSession>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.ProcessMessageAsync(
            "session-123",
            "Hello AI!",
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        // Assert
        capturedMessage.Should().NotBeNull();
        capturedMessage!.Role.Should().Be(MessageRole.User);
        capturedMessage.Content.Should().Be("Hello AI!");
    }

    #endregion

    #region SaveBlueprintAsync Tests

    [Fact]
    public async Task SaveBlueprintAsync_ThrowsException_WhenSessionNotFound()
    {
        // Arrange
        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("nonexistent"))
            .ReturnsAsync((ChatSession?)null);

        // Act
        var act = () => _service.SaveBlueprintAsync("nonexistent");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task SaveBlueprintAsync_ThrowsException_WhenNoBlueprintDraft()
    {
        // Arrange
        var session = CreateSession("session-123", "user-123", "org-456");
        session.BlueprintDraft = null;

        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("session-123"))
            .ReturnsAsync(session);

        // Act
        var act = () => _service.SaveBlueprintAsync("session-123");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No blueprint draft*");
    }

    [Fact]
    public async Task SaveBlueprintAsync_SavesValidBlueprint()
    {
        // Arrange
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Valid Blueprint")
            .WithDescription("A test blueprint")
            .AddParticipant("alice", p => p.Named("Alice"))
            .AddParticipant("bob", p => p.Named("Bob"))
            .AddAction(1, a => a
                .WithTitle("Submit")
                .WithDescription("Submit data")
                .SentBy("alice"))
            .BuildDraft();

        var session = CreateSession("session-123", "user-123", "org-456");
        session.BlueprintDraft = blueprint;

        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("session-123"))
            .ReturnsAsync(session);

        _blueprintStoreMock
            .Setup(s => s.AddAsync(It.IsAny<BlueprintModel>()))
            .ReturnsAsync(blueprint);

        _sessionStoreMock
            .Setup(s => s.UpdateSessionAsync(It.IsAny<ChatSession>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.SaveBlueprintAsync("session-123");

        // Assert
        result.Should().NotBeNull();
        _blueprintStoreMock.Verify(s => s.AddAsync(It.IsAny<BlueprintModel>()), Times.Once);
    }

    #endregion

    #region ExportBlueprintAsync Tests

    [Fact]
    public async Task ExportBlueprintAsync_ReturnsJson_WhenFormatIsJson()
    {
        // Arrange
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Export Test")
            .BuildDraft();

        var session = CreateSession("session-123", "user-123", "org-456");
        session.BlueprintDraft = blueprint;

        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("session-123"))
            .ReturnsAsync(session);

        // Act
        var result = await _service.ExportBlueprintAsync("session-123", "json");

        // Assert
        result.Should().Contain("Export Test");
        result.Should().StartWith("{");
    }

    [Fact]
    public async Task ExportBlueprintAsync_ReturnsYaml_WhenFormatIsYaml()
    {
        // Arrange
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Export Test")
            .BuildDraft();

        var session = CreateSession("session-123", "user-123", "org-456");
        session.BlueprintDraft = blueprint;

        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("session-123"))
            .ReturnsAsync(session);

        // Act
        var result = await _service.ExportBlueprintAsync("session-123", "yaml");

        // Assert
        result.Should().Contain("Export Test");
        result.Should().Contain("title:");
    }

    [Fact]
    public async Task ExportBlueprintAsync_ThrowsException_WhenInvalidFormat()
    {
        // Arrange
        var session = CreateSession("session-123", "user-123", "org-456");
        session.BlueprintDraft = BlueprintBuilder.Create().WithTitle("Test").BuildDraft();

        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("session-123"))
            .ReturnsAsync(session);

        // Act
        var act = () => _service.ExportBlueprintAsync("session-123", "xml");

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Invalid format*");
    }

    #endregion

    #region EndSessionAsync Tests

    [Fact]
    public async Task EndSessionAsync_ClearsActiveSessionAndDeletes()
    {
        // Arrange
        var session = CreateSession("session-123", "user-123", "org-456");

        _sessionStoreMock
            .Setup(s => s.GetSessionAsync("session-123"))
            .ReturnsAsync(session);

        _sessionStoreMock
            .Setup(s => s.ClearActiveSessionForUserAsync("user-123"))
            .Returns(Task.CompletedTask);

        _sessionStoreMock
            .Setup(s => s.DeleteSessionAsync("session-123"))
            .Returns(Task.CompletedTask);

        // Act
        await _service.EndSessionAsync("session-123");

        // Assert
        _sessionStoreMock.Verify(s => s.ClearActiveSessionForUserAsync("user-123"), Times.Once);
        _sessionStoreMock.Verify(s => s.DeleteSessionAsync("session-123"), Times.Once);
    }

    #endregion

    #region Dynamic System Prompt Tests (T017)

    [Fact]
    public async Task BuildSystemPrompt_IncludesSchemaTable()
    {
        // Arrange — return one schema from the index so the prompt builder writes a row for it.
        var entries = new List<Sorcha.Blueprint.Service.Models.SchemaIndexEntryDto>
        {
            new(
                ShortCode: "invoice-schema",
                SourceProvider: "internal",
                SourceUri: "urn:sorcha:schema:invoice-schema",
                Title: "Invoice Schema",
                Description: "Standard invoice fields",
                SectorTags: new[] { "finance" },
                FieldCount: 5,
                RequiredFieldCount: 0,
                SchemaVersion: "1.0.0",
                Status: "Active",
                LastFetchedAt: DateTimeOffset.UtcNow,
                FieldNames: null)
        };

        _schemaIndexServiceMock
            .Setup(s => s.SearchAsync(
                It.IsAny<string?>(),
                It.IsAny<string[]?>(),
                It.IsAny<string?>(),
                It.IsAny<Sorcha.Blueprint.Schemas.Models.SchemaIndexStatus?>(),
                It.IsAny<int>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sorcha.Blueprint.Service.Models.SchemaIndexSearchResponse(
                Results: entries,
                TotalCount: entries.Count,
                NextCursor: null,
                LoadingProviders: null));

        var user = CreateUser("user-1", "org-1");
        var session = CreateSession("s1", "user-1", "org-1");

        _sessionStoreMock.Setup(s => s.GetActiveSessionForUserAsync("user-1"))
            .ReturnsAsync((ChatSession?)null);
        _sessionStoreMock.Setup(s => s.CreateSessionAsync("user-1", "org-1", null))
            .ReturnsAsync(session);

        // Act — CreateSessionAsync triggers BuildSystemPromptAsync indirectly
        await _service.CreateSessionAsync(user);

        // Assert — Verify that schemas were loaded
        // Since BuildSystemPromptAsync is private, we verify indirectly via ProcessMessageAsync
        // which calls it. For this test, we verify the schema store was queried during session creation.
        // The system prompt is built during ProcessMessageAsync, so set up a full message flow.
        _sessionStoreMock.Setup(s => s.GetSessionAsync("s1")).ReturnsAsync(session);
        _sessionStoreMock.Setup(s => s.GetMessagesAsync("s1")).ReturnsAsync([]);
        _sessionStoreMock.Setup(s => s.AddMessageAsync("s1", It.IsAny<ChatMessage>())).Returns(Task.CompletedTask);
        _sessionStoreMock.Setup(s => s.UpdateSessionAsync(It.IsAny<ChatSession>())).Returns(Task.CompletedTask);

        _toolExecutorMock.Setup(t => t.GetToolDefinitions()).Returns([]);

        string? capturedPrompt = null;
        _aiProviderMock
            .Setup(a => a.StreamCompletionAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<IReadOnlyList<ToolDefinition>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyList<ToolDefinition>, string, CancellationToken>(
                (_, _, prompt, _) => capturedPrompt = prompt)
            .Returns(CreateEmptyStream());

        await _service.ProcessMessageAsync("s1", "Hello",
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        // Assert
        capturedPrompt.Should().NotBeNull();
        capturedPrompt.Should().Contain("invoice-schema");
    }

    [Fact]
    public async Task BuildSystemPrompt_IncludesTemplateTable()
    {
        // Arrange
        var templates = new List<Sorcha.Blueprint.Models.BlueprintTemplate>
        {
            new()
            {
                Id = "t1",
                Title = "Loan Application",
                Description = "Standard loan workflow",
                Category = "finance",
                Published = true
            }
        };

        _templateServiceMock
            .Setup(s => s.GetPublishedTemplatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(templates);

        var session = CreateSession("s1", "user-1", "org-1");
        _sessionStoreMock.Setup(s => s.GetSessionAsync("s1")).ReturnsAsync(session);
        _sessionStoreMock.Setup(s => s.GetMessagesAsync("s1")).ReturnsAsync([]);
        _sessionStoreMock.Setup(s => s.AddMessageAsync("s1", It.IsAny<ChatMessage>())).Returns(Task.CompletedTask);
        _sessionStoreMock.Setup(s => s.UpdateSessionAsync(It.IsAny<ChatSession>())).Returns(Task.CompletedTask);

        _toolExecutorMock.Setup(t => t.GetToolDefinitions()).Returns([]);

        string? capturedPrompt = null;
        _aiProviderMock
            .Setup(a => a.StreamCompletionAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<IReadOnlyList<ToolDefinition>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyList<ToolDefinition>, string, CancellationToken>(
                (_, _, prompt, _) => capturedPrompt = prompt)
            .Returns(CreateEmptyStream());

        // Act
        await _service.ProcessMessageAsync("s1", "Hello",
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        // Assert
        capturedPrompt.Should().NotBeNull();
        capturedPrompt.Should().Contain("Loan Application");
    }

    [Fact]
    public async Task BuildSystemPrompt_HandlesEmptySchemas()
    {
        // Arrange — empty schemas already set up in constructor defaults
        var session = CreateSession("s1", "user-1", "org-1");
        _sessionStoreMock.Setup(s => s.GetSessionAsync("s1")).ReturnsAsync(session);
        _sessionStoreMock.Setup(s => s.GetMessagesAsync("s1")).ReturnsAsync([]);
        _sessionStoreMock.Setup(s => s.AddMessageAsync("s1", It.IsAny<ChatMessage>())).Returns(Task.CompletedTask);
        _sessionStoreMock.Setup(s => s.UpdateSessionAsync(It.IsAny<ChatSession>())).Returns(Task.CompletedTask);

        _toolExecutorMock.Setup(t => t.GetToolDefinitions()).Returns([]);

        string? capturedPrompt = null;
        _aiProviderMock
            .Setup(a => a.StreamCompletionAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<IReadOnlyList<ToolDefinition>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyList<ToolDefinition>, string, CancellationToken>(
                (_, _, prompt, _) => capturedPrompt = prompt)
            .Returns(CreateEmptyStream());

        // Act
        await _service.ProcessMessageAsync("s1", "Hello",
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        // Assert
        capturedPrompt.Should().NotBeNull();
        capturedPrompt.Should().Contain("No standardised schemas");
    }

    [Fact]
    public async Task BuildSystemPrompt_IncludesConsultativeWorkflow()
    {
        // Arrange
        var session = CreateSession("s1", "user-1", "org-1");
        _sessionStoreMock.Setup(s => s.GetSessionAsync("s1")).ReturnsAsync(session);
        _sessionStoreMock.Setup(s => s.GetMessagesAsync("s1")).ReturnsAsync([]);
        _sessionStoreMock.Setup(s => s.AddMessageAsync("s1", It.IsAny<ChatMessage>())).Returns(Task.CompletedTask);
        _sessionStoreMock.Setup(s => s.UpdateSessionAsync(It.IsAny<ChatSession>())).Returns(Task.CompletedTask);

        _toolExecutorMock.Setup(t => t.GetToolDefinitions()).Returns([]);

        string? capturedPrompt = null;
        _aiProviderMock
            .Setup(a => a.StreamCompletionAsync(
                It.IsAny<IReadOnlyList<ChatMessage>>(),
                It.IsAny<IReadOnlyList<ToolDefinition>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyList<ToolDefinition>, string, CancellationToken>(
                (_, _, prompt, _) => capturedPrompt = prompt)
            .Returns(CreateEmptyStream());

        // Act
        await _service.ProcessMessageAsync("s1", "Hello",
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        // Assert
        capturedPrompt.Should().NotBeNull();
        capturedPrompt.Should().Contain("Understand Intent");
        capturedPrompt.Should().Contain("Confirm Participants");
    }

    #endregion

    #region Helpers

    private static ClaimsPrincipal CreateUser(string userId, string orgId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("org_id", orgId)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ChatSession CreateSession(string id, string userId, string orgId)
    {
        return new ChatSession
        {
            Id = id,
            UserId = userId,
            OrganizationId = orgId,
            Status = SessionStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActivityAt = DateTimeOffset.UtcNow
        };
    }

    private static async IAsyncEnumerable<AIStreamEvent> CreateEmptyStream()
    {
        yield return new StreamEnd();
        await Task.CompletedTask;
    }

    #endregion
}
