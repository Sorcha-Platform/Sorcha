// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text.Json;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Service.Models.Chat;
using Sorcha.Blueprint.Service.Services.Interfaces;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Orchestrates chat sessions, AI interactions, and tool executions.
/// </summary>
public class ChatOrchestrationService : IChatOrchestrationService
{
    private readonly IChatSessionStore _sessionStore;
    private readonly IAIProviderService _aiProvider;
    private readonly IBlueprintToolExecutor _toolExecutor;
    private readonly IBlueprintStore _blueprintStore;
    private readonly ILogger<ChatOrchestrationService> _logger;

    // System prompt for the AI assistant
    private const string SystemPrompt = """
        You are a blueprint design assistant for the Sorcha distributed ledger platform.
        You help users create workflow blueprints by CALLING THE PROVIDED TOOLS.

        IMPORTANT: When users ask you to create blueprints, you MUST use the tools to actually build them.
        Do NOT just describe what you would do - CALL THE TOOLS to make it happen.

        ## Available Tools (YOU MUST USE THESE)

        - create_blueprint: Start a new blueprint (required first step - ALWAYS call this first)
        - add_participant: Add people or organizations to the workflow (minimum 2 required)
        - remove_participant: Remove an actor from the workflow
        - add_action: Add workflow steps with data collection requirements
        - update_action: Modify an existing action
        - set_disclosure: Control who can see what data (privacy rules)
        - add_routing: Add decision points (if/then logic)
        - validate_blueprint: Check if the blueprint is complete and valid (ALWAYS call this at the end)

        ## Workflow (YOU MUST FOLLOW THIS)

        When a user asks you to create ANY blueprint, follow these steps EXACTLY:

        1. IMMEDIATELY CALL create_blueprint with title and description (do NOT ask for confirmation first)
        2. IMMEDIATELY CALL add_participant for each participant (at least 2)
        3. IMMEDIATELY CALL add_action for each workflow step
        4. CALL set_disclosure if privacy rules are mentioned
        5. CALL add_routing if conditional logic is needed
        6. FINALLY CALL validate_blueprint to show results

        ## Example Conversation

        User: "Create a simple approval workflow"

        You MUST respond by CALLING TOOLS FIRST:
        1. create_blueprint(title="Approval Workflow", description="A simple approval workflow")
        2. add_participant(id="requester", name="Requester", role="person")
        3. add_participant(id="approver", name="Approver", role="person")
        4. add_action(id=0, title="Submit Request", sender="requester", isStartingAction=true)
        5. add_action(id=1, title="Approve", sender="approver")
        6. validate_blueprint()

        Then explain what you created. TOOLS FIRST, explanation second.

        ## Blueprint Rules

        - Every blueprint needs at least 2 participants
        - Every blueprint needs at least 1 action
        - Every action needs a sender (who performs it)
        - At least one action should be marked as a starting action
        - Use disclosure rules to control data privacy between participants

        ## Data Schema Guidelines

        When users describe data to collect, translate to JSON schema fields:

        **Field Types:**
        - string: Text data (names, descriptions, comments)
        - number: Decimal numbers (prices, percentages, rates)
        - integer: Whole numbers (quantities, counts, ages)
        - boolean: Yes/no values (approvals, confirmations)
        - date: Date values (use format: "date")
        - file: File uploads (documents, attachments)

        **Common Constraints:**
        - Email: type="string", format="email"
        - URL: type="string", format="uri"
        - Currency: type="number", minimum=0
        - Percentage: type="number", minimum=0, maximum=100
        - Required fields: Mark with isRequired=true
        - Text limits: Use minLength/maxLength (e.g., comments: min 10, max 1000)
        - Number ranges: Use minimum/maximum (e.g., loan: min 1000, max 50000)
        - Pattern validation: Use pattern for custom formats (e.g., phone: "^\\d{10}$")
        - Enumerated values: Use enumValues for dropdowns (e.g., ["approved", "rejected", "pending"])

        **Example Schema Translations:**
        - "Collect name and email" → name (string, required), email (string, format: email, required)
        - "Loan amount between 1000 and 50000" → loanAmount (number, min: 1000, max: 50000, required)
        - "Optional comments up to 500 characters" → comments (string, maxLength: 500)
        - "Status: approved, rejected, or pending" → status (string, enumValues: ["approved", "rejected", "pending"])
        - "Birth date" → birthDate (string, format: date)

        ## Disclosure Rules (Privacy)

        Disclosures control which participants can see which data fields:

        **Key Concepts:**
        - By default, only the sender can see data they submit
        - set_disclosure explicitly grants visibility to other participants
        - Use JSON pointer format for field paths: "/fieldName"
        - You can disclose specific fields or all fields

        **Privacy Best Practices:**
        - Only disclose what's necessary for each participant's role
        - Sensitive data (salary, SSN, medical) should have limited disclosure
        - Consider "need to know" - approvers may need summary, not details
        - Audit/compliance roles may need full visibility

        **Example Disclosure Patterns:**
        - "Only the manager should see the salary" → set_disclosure for manager on salary field only
        - "HR sees everything, manager sees name only" → Two disclosures: HR gets all fields, manager gets /name
        - "Approver sees request but not personal details" → Disclose /requestAmount, /requestReason; exclude /personalInfo

        ## Routing Rules (Conditional Logic)

        Use add_routing for decision points based on data values:

        **Supported Operators:**
        - equals, notEquals: Exact value matching
        - greaterThan, lessThan, greaterOrEqual, lessOrEqual: Numeric comparisons
        - contains: String contains substring
        - in: Value is in a list

        **Example Routing:**
        - "If amount > 10000, requires senior approval" → greaterThan on amount field, route to senior action
        - "If status is 'rejected', end workflow" → equals on status field, route to end action

        ## Workflow

        1. Start by creating a blueprint with title and description
        2. Add all participants (roles/people involved)
        3. Add actions for each step, including data to collect
        4. Configure disclosure rules for privacy
        5. Add routing for conditional logic (if needed)
        6. Validate to check for issues

        Always validate the blueprint after significant changes to show users any issues.
        When the user describes a workflow, break it down into participants and actions.
        Ask clarifying questions if the requirements are ambiguous.
        Guide users through the process step by step, explaining what you're doing.
        When the blueprint is complete and valid, remind users they can save it.
        """;

    public ChatOrchestrationService(
        IChatSessionStore sessionStore,
        IAIProviderService aiProvider,
        IBlueprintToolExecutor toolExecutor,
        IBlueprintStore blueprintStore,
        ILogger<ChatOrchestrationService> logger)
    {
        _sessionStore = sessionStore;
        _aiProvider = aiProvider;
        _toolExecutor = toolExecutor;
        _blueprintStore = blueprintStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ChatSession> CreateSessionAsync(ClaimsPrincipal user, string? blueprintId = null)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? throw new InvalidOperationException("User ID not found in claims");

        var orgId = user.FindFirst("org_id")?.Value
            ?? user.FindFirst("organization_id")?.Value
            ?? "default";

        // Check for existing active session
        var existingSession = await _sessionStore.GetActiveSessionForUserAsync(userId);
        if (existingSession != null && !existingSession.IsExpired)
        {
            _logger.LogInformation("Resuming existing session {SessionId} for user {UserId}",
                existingSession.Id, userId);
            return existingSession;
        }

        // Create new session
        var session = await _sessionStore.CreateSessionAsync(userId, orgId, blueprintId);

        // If editing existing blueprint, load it
        if (!string.IsNullOrEmpty(blueprintId))
        {
            var existingBlueprint = await _blueprintStore.GetAsync(blueprintId);
            if (existingBlueprint != null)
            {
                session.BlueprintDraft = existingBlueprint;
                await _sessionStore.UpdateSessionAsync(session);
            }
        }

        _logger.LogInformation("Created new chat session {SessionId} for user {UserId}",
            session.Id, userId);

        return session;
    }

    /// <inheritdoc />
    public Task<ChatSession?> GetSessionAsync(string sessionId)
    {
        return _sessionStore.GetSessionAsync(sessionId);
    }

    /// <inheritdoc />
    public async Task ProcessMessageAsync(
        string sessionId,
        string message,
        Func<string, Task> onChunk,
        Func<string, ToolResult, Task> onToolResult,
        Func<BlueprintModel, ValidationResultDto, Task> onBlueprintUpdate,
        CancellationToken cancellationToken = default)
    {
        var session = await _sessionStore.GetSessionAsync(sessionId)
            ?? throw new InvalidOperationException("Session not found");

        if (session.IsExpired)
        {
            throw new InvalidOperationException("Session has expired");
        }

        if (session.IsMessageLimitReached)
        {
            throw new InvalidOperationException("Message limit reached (100 messages per session)");
        }

        // Validate message
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message cannot be empty");
        }

        if (message.Length > 10000)
        {
            throw new ArgumentException("Message too long (max 10000 characters)");
        }

        // Add user message
        var userMessage = new ChatMessage
        {
            SessionId = sessionId,
            Role = MessageRole.User,
            Content = message
        };
        await _sessionStore.AddMessageAsync(sessionId, userMessage);

        // Create or get blueprint builder
        var builder = session.BlueprintDraft != null
            ? CreateBuilderFromBlueprint(session.BlueprintDraft)
            : BlueprintBuilder.Create();

        // Get conversation history
        var messages = await _sessionStore.GetMessagesAsync(sessionId);
        var toolDefinitions = _toolExecutor.GetToolDefinitions();

        // Build system prompt with blueprint context if editing
        var systemPrompt = BuildSystemPrompt(session);

        // Stream AI response with tool-use continuation loop.
        // When Claude calls tools, stop_reason is "tool_use" — we must send the tool results
        // back and stream another turn until Claude finishes with "end_turn".
        const int maxContinuationTurns = 10; // Safety limit to prevent infinite loops

        for (var turn = 0; turn < maxContinuationTurns; turn++)
        {
            var responseContent = "";
            var toolCalls = new List<ToolCall>();
            var toolResults = new List<ToolResult>();
            string? stopReason = null;

            await foreach (var evt in _aiProvider.StreamCompletionAsync(
                messages, toolDefinitions, systemPrompt, cancellationToken))
            {
                _logger.LogInformation("Stream event: {EventType}", evt.GetType().Name);
                switch (evt)
                {
                    case TextChunk chunk:
                        responseContent += chunk.Text;
                        await onChunk(chunk.Text);
                        break;

                    case ToolUse toolUse:
                        var toolCall = new ToolCall
                        {
                            Id = toolUse.Id,
                            ToolName = toolUse.Name,
                            Arguments = toolUse.Arguments
                        };
                        toolCalls.Add(toolCall);

                        // Execute the tool
                        var result = await _toolExecutor.ExecuteAsync(
                            toolUse.Name, toolUse.Arguments, builder, cancellationToken);

                        // Update result with correct tool call ID
                        result = result with { ToolCallId = toolUse.Id };
                        toolResults.Add(result);

                        await onToolResult(toolUse.Name, result);

                        // If blueprint changed, notify and validate
                        if (result.BlueprintChanged)
                        {
                            var draft = builder.BuildDraft();
                            session.BlueprintDraft = draft;
                            await _sessionStore.UpdateSessionAsync(session);

                            var validation = ValidateBlueprint(draft);
                            await onBlueprintUpdate(draft, validation);
                        }
                        break;

                    case StreamEnd end:
                        stopReason = end.StopReason;
                        break;

                    case StreamError error:
                        _logger.LogError("AI stream error: {Message}", error.Message);
                        throw new InvalidOperationException($"AI service error: {error.Message}");
                }
            }

            // Store assistant message with tool calls and results
            _logger.LogInformation("Stream loop ended. StopReason={StopReason}, tools={ToolCount}, text={TextLen}",
                stopReason, toolCalls.Count, responseContent.Length);

            // Store assistant message with tool calls only (NOT tool results).
            // Tool results go in a separate user message for correct Anthropic API format:
            // assistant: [text + tool_use blocks] → user: [tool_result blocks]
            var assistantMessage = new ChatMessage
            {
                SessionId = sessionId,
                Role = MessageRole.Assistant,
                Content = responseContent,
                ToolCalls = toolCalls.Count > 0 ? toolCalls : null
            };

            try
            {
                await _sessionStore.AddMessageAsync(sessionId, assistantMessage);
                _logger.LogInformation("Stored assistant message for session {SessionId}", sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FAILED to store assistant message for session {SessionId}", sessionId);
                throw;
            }

            // If Claude stopped because it used tools, send results back and continue
            if (stopReason == "tool_use" && toolResults.Count > 0)
            {
                _logger.LogInformation(
                    "Turn {Turn}: AI used {ToolCount} tools, sending results back for continuation",
                    turn, toolResults.Count);

                // Add tool results as a user message for the next turn
                var toolResultMessage = new ChatMessage
                {
                    SessionId = sessionId,
                    Role = MessageRole.User,
                    Content = "",
                    ToolResults = toolResults
                };
                _logger.LogInformation("Storing tool result message for session {SessionId}", sessionId);
                await _sessionStore.AddMessageAsync(sessionId, toolResultMessage);

                // Refresh messages for next iteration
                _logger.LogInformation("Refreshing message history for continuation turn {Turn}", turn + 1);
                messages = await _sessionStore.GetMessagesAsync(sessionId);
                _logger.LogInformation("Continuation turn {Turn} starting with {MessageCount} messages", turn + 1, messages.Count);
                continue;
            }

            // end_turn or max_tokens — we're done
            _logger.LogDebug(
                "Processed message in session {SessionId}, response length: {Length}, tools used: {ToolCount}, turns: {Turns}",
                sessionId, responseContent.Length, toolCalls.Count, turn + 1);
            break;
        }

        // Update session activity
        await _sessionStore.UpdateSessionAsync(session);
    }

    /// <inheritdoc />
    public async Task<BlueprintModel?> SaveBlueprintAsync(string sessionId)
    {
        var session = await _sessionStore.GetSessionAsync(sessionId)
            ?? throw new InvalidOperationException("Session not found");

        if (session.BlueprintDraft == null)
        {
            throw new InvalidOperationException("No blueprint draft to save");
        }

        // Validate before saving
        var validation = ValidateBlueprint(session.BlueprintDraft);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Blueprint is invalid: {string.Join(", ", validation.Errors.Select(e => e.Message))}");
        }

        // Save to blueprint store
        BlueprintModel saved;
        if (!string.IsNullOrEmpty(session.ExistingBlueprintId))
        {
            saved = await _blueprintStore.UpdateAsync(session.ExistingBlueprintId, session.BlueprintDraft)
                ?? throw new InvalidOperationException("Failed to update blueprint");
        }
        else
        {
            saved = await _blueprintStore.AddAsync(session.BlueprintDraft);
        }

        // Mark session as completed
        session.Status = SessionStatus.Completed;
        await _sessionStore.UpdateSessionAsync(session);

        _logger.LogInformation("Saved blueprint {BlueprintId} from session {SessionId}",
            saved.Id, sessionId);

        return saved;
    }

    /// <inheritdoc />
    public async Task<string> ExportBlueprintAsync(string sessionId, string format)
    {
        var session = await _sessionStore.GetSessionAsync(sessionId)
            ?? throw new InvalidOperationException("Session not found");

        if (session.BlueprintDraft == null)
        {
            throw new InvalidOperationException("No blueprint draft to export");
        }

        return format.ToLowerInvariant() switch
        {
            "json" => JsonSerializer.Serialize(session.BlueprintDraft, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }),
            "yaml" => new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Serialize(session.BlueprintDraft),
            _ => throw new ArgumentException($"Invalid format: {format}. Use 'json' or 'yaml'.")
        };
    }

    /// <inheritdoc />
    public async Task EndSessionAsync(string sessionId)
    {
        var session = await _sessionStore.GetSessionAsync(sessionId);
        if (session != null)
        {
            await _sessionStore.ClearActiveSessionForUserAsync(session.UserId);
        }

        await _sessionStore.DeleteSessionAsync(sessionId);

        _logger.LogInformation("Ended chat session {SessionId}", sessionId);
    }

    private static string BuildSystemPrompt(ChatSession session)
    {
        // If no blueprint is loaded, use standard system prompt
        if (session.BlueprintDraft == null)
        {
            return SystemPrompt;
        }

        // Build context about the loaded blueprint for editing
        var blueprint = session.BlueprintDraft;
        var participantList = string.Join(", ", blueprint.Participants.Select(p =>
            $"{p.Name} (ID: {p.Id}){(!string.IsNullOrEmpty(p.Organisation) ? $" from {p.Organisation}" : "")}"));

        var actionList = string.Join("\n", blueprint.Actions.Select(a =>
        {
            var sender = blueprint.Participants.FirstOrDefault(p => p.Id == a.Sender || p.WalletAddress == a.Sender)?.Name ?? a.Sender ?? "Unknown";
            var schemaCount = a.DataSchemas?.Count() ?? 0;
            var routeCount = a.Routes?.Count() ?? 0;
            var schemaInfo = schemaCount > 0 ? $", {schemaCount} schema(s)" : "";
            var routeInfo = routeCount > 0 ? $", {routeCount} route(s)" : "";
            return $"  - {a.Title} (ID: {a.Id}, sender: {sender}{schemaInfo}{routeInfo})";
        }));

        var blueprintContext = $"""

            ## Current Blueprint Being Edited

            You are editing an existing blueprint. Here is its current state:

            **Title**: {blueprint.Title}
            **Description**: {blueprint.Description ?? "No description"}
            **ID**: {blueprint.Id}

            **Participants ({blueprint.Participants.Count})**:
            {participantList}

            **Actions ({blueprint.Actions.Count})**:
            {(string.IsNullOrEmpty(actionList) ? "  No actions defined yet" : actionList)}

            When the user asks to modify the blueprint:
            - Use update_action to modify existing actions (refer to them by ID or title)
            - Use add_participant/remove_participant to change participants
            - Use add_action to add new workflow steps
            - Use set_disclosure to update privacy rules
            - Use add_routing to add conditional logic

            You can refer to existing elements by their ID or name.
            """;

        return SystemPrompt + blueprintContext;
    }

    private static BlueprintBuilder CreateBuilderFromBlueprint(BlueprintModel blueprint)
    {
        var builder = BlueprintBuilder.Create()
            .WithId(blueprint.Id)
            .WithTitle(blueprint.Title)
            .WithDescription(blueprint.Description ?? "");

        foreach (var participant in blueprint.Participants)
        {
            builder.AddParticipant(participant.Id, p =>
            {
                p.Named(participant.Name);
                if (!string.IsNullOrEmpty(participant.Organisation))
                {
                    p.FromOrganisation(participant.Organisation);
                }
            });
        }

        // Note: Actions would need more complex reconstruction
        // For MVP, we rebuild from scratch with tool calls

        return builder;
    }

    private static ValidationResultDto ValidateBlueprint(BlueprintModel blueprint)
    {
        var errors = new List<ValidationErrorDto>();
        var warnings = new List<ValidationWarningDto>();

        // Check minimum participants
        if (blueprint.Participants.Count < 2)
        {
            errors.Add(new ValidationErrorDto(
                "MIN_PARTICIPANTS",
                "Blueprint requires at least 2 participants",
                "participants"));
        }

        // Check minimum actions
        if (blueprint.Actions.Count < 1)
        {
            errors.Add(new ValidationErrorDto(
                "MIN_ACTIONS",
                "Blueprint requires at least 1 action",
                "actions"));
        }

        // Check title
        if (string.IsNullOrWhiteSpace(blueprint.Title) || blueprint.Title.Length < 3)
        {
            errors.Add(new ValidationErrorDto(
                "INVALID_TITLE",
                "Blueprint title must be at least 3 characters",
                "title"));
        }

        // Check description
        if (string.IsNullOrWhiteSpace(blueprint.Description) || blueprint.Description.Length < 5)
        {
            errors.Add(new ValidationErrorDto(
                "INVALID_DESCRIPTION",
                "Blueprint description must be at least 5 characters",
                "description"));
        }

        // Check for starting action
        var hasStartingAction = blueprint.Actions.Any(a => a.IsStartingAction);
        if (!hasStartingAction && blueprint.Actions.Count > 0)
        {
            warnings.Add(new ValidationWarningDto(
                "NO_STARTING_ACTION",
                "No action is marked as a starting action",
                "actions"));
        }

        return new ValidationResultDto
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }
}
