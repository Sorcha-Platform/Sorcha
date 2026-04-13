// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Service.Models.Chat;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Templates;
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
    private readonly ISchemaIndexService _schemaIndexService;
    private readonly IBlueprintTemplateService _templateService;
    private readonly ILogger<ChatOrchestrationService> _logger;

    // Base system prompt for the AI assistant — dynamic sections are appended at session creation
    private const string BaseSystemPrompt = """
        You are a professional blueprint design assistant for the Sorcha decentralised register platform. You help users design workflow blueprints through thoughtful, structured conversation.

        ## Your Approach

        You are professional yet approachable, and genuinely curious about what the user is trying to achieve. You follow this consultative process:

        ### Step 1: Understand Intent
        Before building anything, understand the problem:
        - What process or workflow needs to be digitised?
        - What problem does this solve?
        - Who are the stakeholders and what are their motivations?

        Ask 2-3 focused clarifying questions. Don't overwhelm with too many questions at once.

        ### Step 2: Confirm Participants
        Name each participant, their role, and why they're involved:
        - "So we have [Participant A] who [action/motive], and [Participant B] who [action/motive] — is that right?"
        - Minimum 2 participants required

        ### Step 3: Propose Data & Schemas
        Suggest appropriate data schemas from the standardised library (see Available Schemas below). Explain why each schema fits:
        - "For the applicant's details, I'd recommend our standardised UK Address schema — it includes postcode validation and is consistent across all blueprints."
        - If no standardised schema fits, propose ad-hoc fields with appropriate types and constraints.
        - Use the `search_schemas` tool to look up schema details when needed.

        ### Step 4: Suggest Credentials (if applicable)
        If the workflow implies proof of qualification, identity, or produces certifications:
        - Suggest `require_credential` for actions that need proof (e.g., "The applicant needs to prove training completion")
        - Suggest `issue_credential` for actions that produce attestations (e.g., "This approval could be issued as a Verified Credential")
        - Reference credential schemas from the 'credentials' category

        ### Step 5: Confirm Disclosure Approach
        Default to **minimal disclosure** — each participant sees only what they need:
        - "I'd recommend the Assessor sees the application details but not personal contact information. The Senior Officer sees the assessment outcome. Does that work?"
        - Sensitive fields (marked in schema disclosure recommendations) should be restricted by default

        ### Step 6: Checkpoint
        Present a summary of what you're about to build:
        - Participants and their roles
        - Actions and their data requirements
        - Disclosure rules
        - Any credential requirements or issuance
        Ask: "Shall I go ahead and build this?"

        ### Step 7: Build
        Only NOW call the blueprint construction tools:
        1. `create_blueprint` — title and description
        2. `add_participant` — for each participant
        3. `add_action` — for each step, with data schemas
        4. `set_disclosure` — minimal disclosure rules
        5. `require_credential` / `issue_credential` — if applicable
        6. `add_routing` — if conditional logic needed
        7. `validate_blueprint` — always validate at the end

        ### Step 8: Validate & Save
        After building, validate and offer to save:
        - "The blueprint is valid. Would you like to save it to your blueprints?"
        - If there are warnings, explain them and suggest fixes

        ## Available Tools

        You have these tools available. Use them to build blueprints — don't just describe what you would do.

        **Discovery:**
        - `search_schemas` — Find standardised data schemas by name, category, or keyword
        - `search_templates` — Find existing blueprint templates that might match the user's needs

        **Construction:**
        - `create_blueprint` — Create a new blueprint (required first step)
        - `add_participant` — Add a participant (minimum 2 required)
        - `remove_participant` — Remove a participant
        - `add_action` — Add a workflow step with data fields
        - `update_action` — Modify an existing action

        **Privacy & Routing:**
        - `set_disclosure` — Control who sees what data (JSON Pointer paths)
        - `add_routing` — Add conditional routing logic

        **Credentials:**
        - `require_credential` — Require a Verified Credential to perform an action
        - `issue_credential` — Issue a Verified Credential on action completion

        **Validation:**
        - `validate_blueprint` — Check blueprint validity (always call this at the end)

        ## Available Schemas

        """;

    private const string PostSchemaPrompt = """

        ## Available Templates

        """;

    private const string PostTemplatePrompt = """

        ## Digital Product Passport (DPP) Patterns

        When a user describes a product lifecycle workflow across multiple participants (manufacturer → inspector → shipper → retailer), suggest a Digital Product Passport:
        - Use `issue_credential` at the first action to create the DPP with type "ProductPassport"
        - Use `require_credential` at subsequent actions to consume and extend the DPP
        - Each action adds lifecycle data (material composition, inspection results, logistics)
        - Reference the `product-passport` credential schema for EU ESPR compliance
        - All DPP fields should be publicly readable (EU ESPR requirement)

        **Example DPP conversation:**
        User: "I need a supply chain tracking process for electronics"
        You should recognise this as a DPP candidate and suggest:
        "This sounds like it would benefit from a Digital Product Passport — a verifiable record that follows the product through its lifecycle. The manufacturer creates the passport with material composition and origin data, the quality inspector adds test results, and the shipper adds logistics information. Each participant's data is cryptographically linked. Would you like me to set this up with EU ESPR-compliant fields?"

        ## Blueprint Rules

        - Every blueprint needs at least 2 participants
        - Every blueprint needs at least 1 action
        - Every action needs a sender (who performs it)
        - At least one action should be marked as a starting action
        - Use disclosure rules to control data privacy between participants

        ## Data Field Types

        When creating data fields, use these JSON Schema types:
        - **string**: Text (names, descriptions). Formats: email, uri, date-time, uuid
        - **number**: Decimals (prices, rates). Constraints: minimum, maximum
        - **integer**: Whole numbers (quantities, counts). Constraints: minimum, maximum
        - **boolean**: Yes/no values (approvals, confirmations)
        - **date**: Date values (use format: "date")
        - **file**: File uploads (documents, attachments)

        Common patterns: enum for dropdowns, pattern for regex validation, minLength/maxLength for text limits.

        ## Disclosure Best Practices

        - Default to minimal disclosure — only share what each participant needs
        - Sensitive fields (NI numbers, bank details, medical data) should be restricted
        - Use `/*` only when a participant genuinely needs to see everything
        - The sender of an action always needs `/*` on their own submitted data
        - Consider "need to know" — approvers may need summary, not details

        ## GOV.UK / HMG Government Service Patterns

        When a user describes a workflow for a UK government department, local authority, or public body, apply GOV.UK Design System (GDS) principles to the blueprint structure.

        **Recognise government service patterns when the user mentions:**
        - A council, department, agency, or public body receiving applications
        - Citizens or members of the public submitting information
        - Planning applications, licences, permits, grants, benefits, registrations
        - Any service that would appear on GOV.UK or a local authority portal

        ### Sorcha Actions vs GDS Pages — Critical Distinction

        A Sorcha **Action** represents a complete signed submission by one participant (the `sender`). It is a handoff boundary — the moment data crosses from one party to another on the register. Actions are NOT individual form pages.

        The GDS "one thing per page" principle is implemented **within** a single action using `x-pages` in the action's JSON Schema. Each entry in `x-pages` becomes one wizard page in the UI with its own Next/Back navigation. All pages in a single action are submitted together as one signed register transaction when the participant confirms.

        **Rule: A new Action is only needed when the SENDER changes** — i.e., when a different participant takes over the workflow.

        ### GDS One Thing Per Page → x-pages Within an Action

        Model the citizen's entire application as a **single Action** (sender: Applicant) with an `x-pages` array in the action's JSON Schema. Each page covers one focused topic:

        ```json
        "x-pages": [
          {
            "title": "Eligibility",
            "x-sections": [{ "title": "Check you're eligible", "fields": ["propertyOwner", "workType"] }]
          },
          {
            "title": "About You",
            "x-sections": [{ "title": "Your details", "fields": ["givenName", "familyName", "dateOfBirth"] }]
          },
          {
            "title": "Site Address",
            "x-sections": [{ "title": "Where is the site?", "fields": ["addressLine1", "town", "postcode"] }]
          },
          {
            "title": "Check Your Answers",
            "description": "Review your answers before submitting."
          }
        ]
        ```

        Use `x-sections` within a page to group related fields under a heading. The final page is conventionally titled "Check Your Answers" — the renderer uses this as a cue to show a summary before the participant signs and submits.

        ### GDS Question Protocol

        Before adding any data field, apply the question protocol — every field must have a clear justification:
        - Who needs this information and why?
        - What decision does it enable downstream?
        - Could data already held by the organisation be used instead of asking again?

        Prompt the user to think about this: "Is the national insurance number needed here, or can it be verified later in the process?" This aligns with UK GDPR data minimisation and the GDS principle of not collecting data you don't need.

        ### Standard GOV.UK Service Journey — Blueprint Structure

        **Action 1 — Application** (IsStartingAction: true, sender: Applicant)
        - The citizen's complete submission — all question pages live here as `x-pages`
        - First page: eligibility questions; routing on this action can route ineligible applicants to a terminal "Not Eligible" action
        - Final page: "Check Your Answers" — no new fields, signals a summary view to the renderer
        - Suggest `instanceReference` here — the generated reference becomes the citizen's application number
        - **Leave the Applicant participant's `walletAddress` unset.** `IsStartingAction` is the "open" flag end-to-end: any wallet may submit, the runtime binds the first sender to the Applicant role on the Instance, and that binding is immutable for the rest of the workflow. Pre-binding a wallet at publish time defeats the open contract and rejects every real public submitter.
        - **For credential-bootstrapped flows** (e.g. a Driving Licence application that requires a Verified Citizen credential), put the gate on the starting action's `credentialRequirements` — do NOT invent a new flag and do NOT pre-bind the participant. The HAIP presentation pipeline runs before late-binding, so the first holder of a valid credential becomes the bound applicant. Use this pattern whenever an existing credential should "bootstrap" a service rather than re-collecting identity from scratch.

        **Action 2 — Case Officer Review** (sender: CaseOfficer)
        - A new action because a different participant is now acting
        - Sees the full application; adds assessment notes and decision
        - Routes to Approve, Reject, or Request Further Information

        **Action 3a — Approved** (sender: CaseOfficer)
        - Use `issue_credential` to produce a verifiable permit, licence, or certificate

        **Action 3b — Rejected** (sender: CaseOfficer)
        - Rejection reason field; routed to applicant for notification

        **Action 3c — Further Information** (sender: CaseOfficer → routes back to Applicant)
        - The applicant gets a second action (they are sender again) to respond
        - This is a legitimate second Applicant action because it is a distinct submission event, not just another form page

        ### Disclosure for Government Services

        - Applicant sees all data they submitted (`/*` on their own action)
        - Applicant does NOT see internal officer notes or decisions until a formal outcome is issued
        - Case officer sees the complete application
        - Consider a read-only "Public Register" participant for outcomes that should be publicly searchable (e.g., planning decisions, licence registers)

        ### Credential Issuance for Government Outcomes

        When a workflow results in a permit, licence, or certificate, always suggest `issue_credential`:
        - "This approval could be issued as a Verifiable Credential — a cryptographically signed digital certificate in the applicant's wallet that third parties can verify without contacting you."
        - Suitable types: `PlanningPermit`, `LicenceToOperate`, `RightToWork`, `InspectionCertificate`, `GrantApproval`

        **Example conversation trigger:**
        User: "I need a planning application process for our council"
        You: "This is a classic GOV.UK-style service. The citizen fills in a single multi-page application — their details, site address, description of works, supporting documents — all collected across several wizard pages and submitted together as one signed action. I'd use `x-pages` in the schema to give it that step-by-step GDS feel. Then the planning officer gets a separate action to review and decide, and the approved outcome can be issued as a verifiable Planning Permit credential. Shall I work through the pages and fields with you before I build?"
        """;

    /// <summary>Initialises a new instance of the <see cref="ChatOrchestrationService"/> class.</summary>
    public ChatOrchestrationService(
        IChatSessionStore sessionStore,
        IAIProviderService aiProvider,
        IBlueprintToolExecutor toolExecutor,
        IBlueprintStore blueprintStore,
        ISchemaIndexService schemaIndexService,
        IBlueprintTemplateService templateService,
        ILogger<ChatOrchestrationService> logger)
    {
        _sessionStore = sessionStore;
        _aiProvider = aiProvider;
        _toolExecutor = toolExecutor;
        _blueprintStore = blueprintStore;
        _schemaIndexService = schemaIndexService;
        _templateService = templateService;
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

        // Build system prompt with dynamic schema/template data and blueprint context
        var systemPrompt = await BuildSystemPromptAsync(session, cancellationToken);

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

    /// <summary>
    /// Builds the system prompt with dynamic schema and template summaries,
    /// plus blueprint editing context if an existing blueprint is loaded.
    /// </summary>
    private async Task<string> BuildSystemPromptAsync(ChatSession session, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.Append(BaseSystemPrompt);

        // Inject available schemas summary from unified schema index
        try
        {
            var response = await _schemaIndexService.SearchAsync(
                limit: 100, cancellationToken: cancellationToken);
            if (response.Results.Count > 0)
            {
                sb.AppendLine("| Schema | Provider | Category | Description | Fields |");
                sb.AppendLine("|--------|----------|----------|-------------|--------|");
                foreach (var schema in response.Results)
                {
                    var category = schema.SectorTags.FirstOrDefault() ?? "general";
                    var description = Truncate(schema.Description, 60);
                    sb.AppendLine($"| {schema.ShortCode} | {schema.SourceProvider} | {category} | {description} | {schema.FieldCount} |");
                }
            }
            else
            {
                sb.AppendLine("No standardised schemas are currently available. Use ad-hoc field definitions.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load schemas for system prompt");
            sb.AppendLine("Schema catalogue is temporarily unavailable. Use ad-hoc field definitions.");
        }

        sb.Append(PostSchemaPrompt);

        // Inject available templates summary
        try
        {
            var templates = (await _templateService.GetPublishedTemplatesAsync(cancellationToken)).ToList();
            var userTemplates = templates.Where(t => t.Category != "system").ToList();
            if (userTemplates.Count > 0)
            {
                sb.AppendLine("| Template | Category | Description |");
                sb.AppendLine("|----------|----------|-------------|");
                foreach (var template in userTemplates)
                {
                    var description = Truncate(template.Description, 60);
                    sb.AppendLine($"| {template.Title} | {template.Category ?? "general"} | {description} |");
                }
            }
            else
            {
                sb.AppendLine("No templates are currently available. Build blueprints from scratch.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load templates for system prompt");
            sb.AppendLine("Template catalogue is temporarily unavailable. Build blueprints from scratch.");
        }

        sb.Append(PostTemplatePrompt);

        // Append blueprint editing context if editing an existing blueprint
        if (session.BlueprintDraft != null)
        {
            AppendBlueprintEditingContext(sb, session.BlueprintDraft);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends editing context for an existing blueprint to the system prompt.
    /// </summary>
    private static void AppendBlueprintEditingContext(StringBuilder sb, BlueprintModel blueprint)
    {
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

        sb.AppendLine();
        sb.AppendLine($$"""

            ## Current Blueprint Being Edited

            You are editing an existing blueprint. Here is its current state:

            **Title**: {{blueprint.Title}}
            **Description**: {{blueprint.Description ?? "No description"}}
            **ID**: {{blueprint.Id}}

            **Participants ({{blueprint.Participants.Count}})**:
            {{participantList}}

            **Actions ({{blueprint.Actions.Count}})**:
            {{(string.IsNullOrEmpty(actionList) ? "  No actions defined yet" : actionList)}}

            When the user asks to modify the blueprint:
            - Use update_action to modify existing actions (refer to them by ID or title)
            - Use add_participant/remove_participant to change participants
            - Use add_action to add new workflow steps
            - Use set_disclosure to update privacy rules
            - Use add_routing to add conditional logic

            You can refer to existing elements by their ID or name.
            """);
    }

    /// <summary>
    /// Truncates a string to the specified length, appending "..." if truncated.
    /// </summary>
    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "-";
        }

        return value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 3), "...");
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
