// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Models.Forms;
using Sorcha.Blueprint.Service.Models.Chat;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Templates;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Executes AI tool calls against a blueprint builder using the Fluent API.
/// </summary>
public class BlueprintToolExecutor : IBlueprintToolExecutor
{
    private readonly ILogger<BlueprintToolExecutor> _logger;
    private readonly ISchemaIndexService _schemaIndexService;
    private readonly IBlueprintTemplateService _templateService;
    private readonly IReadOnlyList<ToolDefinition> _toolDefinitions;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlueprintToolExecutor"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="schemaIndexService">Schema index service for unified schema search and retrieval.</param>
    /// <param name="templateService">Template service for searching blueprint templates.</param>
    public BlueprintToolExecutor(
        ILogger<BlueprintToolExecutor> logger,
        ISchemaIndexService schemaIndexService,
        IBlueprintTemplateService templateService)
    {
        _logger = logger;
        _schemaIndexService = schemaIndexService;
        _templateService = templateService;
        _toolDefinitions = CreateToolDefinitions();
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteAsync(
        string toolName,
        JsonDocument arguments,
        BlueprintBuilder builder,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing tool {ToolName} with arguments: {Arguments}",
            toolName, arguments.RootElement.GetRawText());

        try
        {
            return await (toolName switch
            {
                "create_blueprint" => Task.FromResult(ExecuteCreateBlueprint(arguments, builder)),
                "add_participant" => Task.FromResult(ExecuteAddParticipant(arguments, builder)),
                "remove_participant" => Task.FromResult(ExecuteRemoveParticipant(arguments, builder)),
                "add_action" => Task.FromResult(ExecuteAddAction(arguments, builder)),
                "update_action" => Task.FromResult(ExecuteUpdateAction(arguments, builder)),
                "set_disclosure" => Task.FromResult(ExecuteSetDisclosure(arguments, builder)),
                "add_routing" => Task.FromResult(ExecuteAddRouting(arguments, builder)),
                "validate_blueprint" => Task.FromResult(ExecuteValidateBlueprint(arguments, builder)),
                "search_schemas" => ExecuteSearchSchemasAsync(arguments, builder, cancellationToken),
                "use_standard_schema" => ExecuteUseStandardSchemaAsync(arguments, builder, cancellationToken),
                "search_templates" => ExecuteSearchTemplatesAsync(arguments, builder, cancellationToken),
                "require_credential" => Task.FromResult(ExecuteRequireCredential(arguments, builder)),
                "issue_credential" => Task.FromResult(ExecuteIssueCredential(arguments, builder)),
                "set_action_schema" => Task.FromResult(ExecuteSetActionSchema(arguments, builder)),
                "set_action_routes" => Task.FromResult(ExecuteSetActionRoutes(arguments, builder)),
                "set_action_metadata" => Task.FromResult(ExecuteSetActionMetadata(arguments, builder)),
                "set_form_layout" => Task.FromResult(ExecuteSetFormLayout(arguments, builder)),
                "set_field_autofill" => Task.FromResult(ExecuteSetFieldAutofill(arguments, builder)),
                "set_review_page" => Task.FromResult(ExecuteSetReviewPage(arguments, builder)),
                _ => Task.FromResult(ToolResult.Failed(Guid.NewGuid().ToString(), $"Unknown tool: {toolName}"))
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return ToolResult.Failed(Guid.NewGuid().ToString(), ex.Message);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolDefinition> GetToolDefinitions() => _toolDefinitions;

    private ToolResult ExecuteCreateBlueprint(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var title = root.GetProperty("title").GetString() ?? "Untitled Blueprint";
        var description = root.GetProperty("description").GetString() ?? "No description provided";

        builder.WithTitle(title).WithDescription(description);

        var draft = builder.BuildDraft();

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new { blueprintId = draft.Id, message = "Blueprint created successfully" },
            blueprintChanged: true);
    }

    private ToolResult ExecuteAddParticipant(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var id = root.GetProperty("id").GetString()!;
        var name = root.GetProperty("name").GetString()!;
        var organisation = root.TryGetProperty("organisation", out var orgProp) ? orgProp.GetString() : null;
        var role = root.TryGetProperty("role", out var roleProp) ? roleProp.GetString() : "person";

        builder.AddParticipant(id, p =>
        {
            p.Named(name);
            if (!string.IsNullOrEmpty(organisation))
            {
                p.FromOrganisation(organisation);
            }
            if (role == "organization")
            {
                p.AsOrganization();
            }
            else
            {
                p.AsPerson();
            }
        });

        var draft = builder.BuildDraft();

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                participantId = id,
                participantCount = draft.Participants.Count,
                message = $"Participant '{name}' added"
            },
            blueprintChanged: true);
    }

    private ToolResult ExecuteRemoveParticipant(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var id = root.GetProperty("id").GetString()!;

        // Note: BlueprintBuilder doesn't have a RemoveParticipant method
        // This would need to rebuild the blueprint without this participant
        // For MVP, return a message explaining the limitation
        return ToolResult.Failed(
            Guid.NewGuid().ToString(),
            "Removing participants is not yet supported. Please recreate the blueprint without this participant.");
    }

    private ToolResult ExecuteAddAction(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var id = root.GetProperty("id").GetInt32();
        var title = root.GetProperty("title").GetString()!;
        var sender = root.GetProperty("sender").GetString()!;
        var description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() : null;
        var isStartingAction = root.TryGetProperty("isStartingAction", out var startProp) && startProp.GetBoolean();
        var routeToNext = root.TryGetProperty("routeToNext", out var routeProp) ? routeProp.GetString() : null;

        builder.AddAction(id, a =>
        {
            a.WithTitle(title);
            a.SentBy(sender);

            if (!string.IsNullOrEmpty(description))
            {
                a.WithDescription(description);
            }

            if (!string.IsNullOrEmpty(routeToNext))
            {
                a.RouteToNext(routeToNext);
            }

            // Handle data fields if provided
            if (root.TryGetProperty("dataFields", out var fieldsArray))
            {
                a.RequiresData(d =>
                {
                    foreach (var field in fieldsArray.EnumerateArray())
                    {
                        var fieldName = field.GetProperty("name").GetString()!;
                        var fieldType = field.GetProperty("type").GetString()!;
                        var fieldTitle = field.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : fieldName;
                        var fieldDescription = field.TryGetProperty("description", out var descriptionProp) ? descriptionProp.GetString() : null;
                        var isRequired = !field.TryGetProperty("required", out var reqProp) || reqProp.GetBoolean();

                        // String constraints
                        var format = field.TryGetProperty("format", out var formatProp) ? formatProp.GetString() : null;
                        var minLength = field.TryGetProperty("minLength", out var minLenProp) ? minLenProp.GetInt32() : (int?)null;
                        var maxLength = field.TryGetProperty("maxLength", out var maxLenProp) ? maxLenProp.GetInt32() : (int?)null;
                        var pattern = field.TryGetProperty("pattern", out var patternProp) ? patternProp.GetString() : null;

                        // Numeric constraints
                        var minimum = field.TryGetProperty("minimum", out var minProp) ? minProp.GetDouble() : (double?)null;
                        var maximum = field.TryGetProperty("maximum", out var maxProp) ? maxProp.GetDouble() : (double?)null;

                        // Enum values
                        var enumValues = field.TryGetProperty("enumValues", out var enumProp)
                            ? enumProp.EnumerateArray().Select(e => e.GetString()!).ToArray()
                            : null;

                        switch (fieldType.ToLowerInvariant())
                        {
                            case "string":
                                d.AddString(fieldName, f =>
                                {
                                    if (fieldTitle != null) f.WithTitle(fieldTitle);
                                    if (fieldDescription != null) f.WithDescription(fieldDescription);
                                    if (isRequired) f.IsRequired();
                                    if (format != null) f.WithFormat(format);
                                    if (minLength.HasValue) f.WithMinLength(minLength.Value);
                                    if (maxLength.HasValue) f.WithMaxLength(maxLength.Value);
                                    if (pattern != null) f.WithPattern(pattern);
                                    if (enumValues != null) f.WithEnum(enumValues);
                                });
                                break;
                            case "number":
                                d.AddNumber(fieldName, f =>
                                {
                                    if (fieldTitle != null) f.WithTitle(fieldTitle);
                                    if (fieldDescription != null) f.WithDescription(fieldDescription);
                                    if (isRequired) f.IsRequired();
                                    if (minimum.HasValue) f.WithMinimum(minimum.Value);
                                    if (maximum.HasValue) f.WithMaximum(maximum.Value);
                                });
                                break;
                            case "integer":
                                d.AddInteger(fieldName, f =>
                                {
                                    if (fieldTitle != null) f.WithTitle(fieldTitle);
                                    if (fieldDescription != null) f.WithDescription(fieldDescription);
                                    if (isRequired) f.IsRequired();
                                    if (minimum.HasValue) f.WithMinimum((int)minimum.Value);
                                    if (maximum.HasValue) f.WithMaximum((int)maximum.Value);
                                });
                                break;
                            case "boolean":
                                d.AddBoolean(fieldName, f =>
                                {
                                    if (fieldTitle != null) f.WithTitle(fieldTitle);
                                    if (fieldDescription != null) f.WithDescription(fieldDescription);
                                    if (isRequired) f.IsRequired();
                                });
                                break;
                            case "date":
                                d.AddDate(fieldName, f =>
                                {
                                    if (fieldTitle != null) f.WithTitle(fieldTitle);
                                    if (fieldDescription != null) f.WithDescription(fieldDescription);
                                    if (isRequired) f.IsRequired();
                                });
                                break;
                            case "file":
                                d.AddFile(fieldName, f =>
                                {
                                    if (fieldTitle != null) f.WithTitle(fieldTitle);
                                    if (fieldDescription != null) f.WithDescription(fieldDescription);
                                    if (isRequired) f.IsRequired();
                                });
                                break;
                            default:
                                d.AddString(fieldName, f =>
                                {
                                    if (fieldTitle != null) f.WithTitle(fieldTitle);
                                    if (isRequired) f.IsRequired();
                                });
                                break;
                        }
                    }
                });
            }
        });

        // Note: IsStartingAction needs to be set after Build - update the action directly
        var draft = builder.BuildDraft();
        var action = draft.Actions.FirstOrDefault(a => a.Id == id);
        if (action != null && isStartingAction)
        {
            action.IsStartingAction = true;
        }

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                actionId = id,
                message = $"Action '{title}' added",
                actionCount = draft.Actions.Count
            },
            blueprintChanged: true);
    }

    private ToolResult ExecuteUpdateAction(JsonDocument arguments, BlueprintBuilder builder)
    {
        // Note: BlueprintBuilder doesn't have an UpdateAction method
        // This would need to rebuild the action with new properties
        return ToolResult.Failed(
            Guid.NewGuid().ToString(),
            "Updating actions is not yet fully supported. Please use add_action with the same ID to replace the action.");
    }

    private ToolResult ExecuteSetDisclosure(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var actionId = root.GetProperty("actionId").GetInt32();
        var participantId = root.GetProperty("participantId").GetString()!;
        var fields = root.GetProperty("fields").EnumerateArray()
            .Select(f => f.GetString()!)
            .ToList();

        // Build the draft to access actions
        var draft = builder.BuildDraft();
        var action = draft.Actions.FirstOrDefault(a => a.Id == actionId);

        if (action == null)
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                $"Action with ID {actionId} not found");
        }

        // Create or update disclosure for this participant
        var existingDisclosure = action.Disclosures
            .FirstOrDefault(d => d.ParticipantAddress == participantId);

        if (existingDisclosure != null)
        {
            // Update existing disclosure
            existingDisclosure.DataPointers = fields;
        }
        else
        {
            // Add new disclosure
            var disclosures = action.Disclosures.ToList();
            disclosures.Add(new Sorcha.Blueprint.Models.Disclosure(participantId, fields));
            action.Disclosures = disclosures;
        }

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                message = $"Disclosure configured for participant '{participantId}' on action '{action.Title}'",
                actionId,
                participantId,
                fieldsDisclosed = fields.Count,
                fields
            },
            blueprintChanged: true);
    }

    private ToolResult ExecuteAddRouting(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var actionId = root.GetProperty("actionId").GetInt32();
        var defaultRoute = root.TryGetProperty("defaultRoute", out var defaultProp) ? defaultProp.GetString() : null;

        // Build the draft to access actions
        var draft = builder.BuildDraft();
        var action = draft.Actions.FirstOrDefault(a => a.Id == actionId);

        if (action == null)
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                $"Action with ID {actionId} not found");
        }

        var routes = new List<Sorcha.Blueprint.Models.Route>();
        var routeCount = 0;

        // Process conditional routes
        if (root.TryGetProperty("conditions", out var conditionsArray))
        {
            foreach (var condition in conditionsArray.EnumerateArray())
            {
                var field = condition.GetProperty("field").GetString()!;
                var op = condition.GetProperty("operator").GetString()!;
                var value = condition.GetProperty("value");
                var routeTo = condition.GetProperty("routeTo").GetString()!;

                // Find target participant to get their action
                var targetParticipant = draft.Participants.FirstOrDefault(p => p.Id == routeTo || p.Name == routeTo);
                var targetAction = draft.Actions.FirstOrDefault(a =>
                    a.Sender == routeTo ||
                    (targetParticipant != null && a.Sender == targetParticipant.Id));

                var nextActionId = targetAction?.Id ?? actionId + 1;

                // Convert operator to JSON Logic
                var jsonLogicCondition = ConvertToJsonLogic(field, op, value);

                routes.Add(new Sorcha.Blueprint.Models.Route
                {
                    Id = $"route_{routeCount++}",
                    NextActionIds = [nextActionId],
                    Condition = jsonLogicCondition,
                    Description = $"Route to {routeTo} when {field} {op} {value}"
                });
            }
        }

        // Add default route if specified
        if (!string.IsNullOrEmpty(defaultRoute))
        {
            var defaultParticipant = draft.Participants.FirstOrDefault(p => p.Id == defaultRoute || p.Name == defaultRoute);
            var defaultAction = draft.Actions.FirstOrDefault(a =>
                a.Sender == defaultRoute ||
                (defaultParticipant != null && a.Sender == defaultParticipant.Id));

            routes.Add(new Sorcha.Blueprint.Models.Route
            {
                Id = $"route_default",
                NextActionIds = [defaultAction?.Id ?? actionId + 1],
                IsDefault = true,
                Description = $"Default route to {defaultRoute}"
            });
        }

        action.Routes = routes;

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                message = $"Routing configured for action '{action.Title}'",
                actionId,
                routeCount = routes.Count,
                hasDefaultRoute = !string.IsNullOrEmpty(defaultRoute)
            },
            blueprintChanged: true);
    }

    private static System.Text.Json.Nodes.JsonNode? ConvertToJsonLogic(string field, string op, JsonElement value)
    {
        var fieldRef = new { @var = field };
        var valueObj = value.ValueKind switch
        {
            JsonValueKind.String => (object)value.GetString()!,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => value.GetRawText()
        };

        var jsonLogic = op.ToLowerInvariant() switch
        {
            "equals" or "==" => new Dictionary<string, object> { ["=="] = new object[] { fieldRef, valueObj } },
            "notequals" or "!=" => new Dictionary<string, object> { ["!="] = new object[] { fieldRef, valueObj } },
            "greaterthan" or ">" => new Dictionary<string, object> { [">"] = new object[] { fieldRef, valueObj } },
            "lessthan" or "<" => new Dictionary<string, object> { ["<"] = new object[] { fieldRef, valueObj } },
            "greaterorequal" or ">=" => new Dictionary<string, object> { [">="] = new object[] { fieldRef, valueObj } },
            "lessorequal" or "<=" => new Dictionary<string, object> { ["<="] = new object[] { fieldRef, valueObj } },
            "contains" => new Dictionary<string, object> { ["in"] = new object[] { valueObj, fieldRef } },
            _ => new Dictionary<string, object> { ["=="] = new object[] { fieldRef, valueObj } }
        };

        return System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(jsonLogic));
    }

    private async Task<ToolResult> ExecuteSearchSchemasAsync(
        JsonDocument arguments,
        BlueprintBuilder builder,
        CancellationToken cancellationToken)
    {
        var root = arguments.RootElement;
        var query = root.GetProperty("query").GetString()!;
        var category = root.TryGetProperty("category", out var catProp) ? catProp.GetString() : null;

        _logger.LogDebug("Searching schemas with query '{Query}', category '{Category}'", query, category);

        // Search the unified schema index — covers local, external, and all providers
        var sectors = !string.IsNullOrEmpty(category) ? new[] { category } : null;
        var response = await _schemaIndexService.SearchAsync(
            search: query,
            sectors: sectors,
            limit: 50,
            cancellationToken: cancellationToken);

        var resultList = response.Results.Select(s => new
        {
            identifier = s.ShortCode,
            sourceUri = s.SourceUri,
            provider = s.SourceProvider,
            title = s.Title,
            category = s.SectorTags.FirstOrDefault() ?? "general",
            description = s.Description ?? string.Empty,
            fieldCount = s.FieldCount,
            fieldNames = s.FieldNames ?? [],
            tags = s.SectorTags
        }).ToList();

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                results = resultList,
                totalCount = resultList.Count,
                message = resultList.Count > 0
                    ? $"Found {resultList.Count} schema(s) matching '{query}'"
                    : $"No schemas found matching '{query}'"
            },
            blueprintChanged: false);
    }

    private async Task<ToolResult> ExecuteUseStandardSchemaAsync(
        JsonDocument arguments,
        BlueprintBuilder builder,
        CancellationToken cancellationToken)
    {
        var root = arguments.RootElement;
        var schemaId = root.GetProperty("schemaId").GetString()!;
        var actionId = root.GetProperty("actionId").GetInt32();
        var merge = !root.TryGetProperty("merge", out var mergeProp) || mergeProp.GetBoolean();

        _logger.LogDebug("Applying schema '{SchemaId}' to action {ActionId}, merge={Merge}", schemaId, actionId, merge);

        // Fetch full schema content from the unified index (by short code)
        var schemaContent = await _schemaIndexService.GetContentByShortCodeAsync(schemaId, cancellationToken);
        if (schemaContent == null)
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                $"Schema '{schemaId}' not found. Use search_schemas to find available schemas.");
        }

        var draft = builder.BuildDraft();
        var action = draft.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action == null)
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                $"Action with ID {actionId} not found");
        }

        // Extract properties from the schema content
        var content = schemaContent.RootElement;
        var fieldsAdded = new List<string>();
        string? disclosureRecommendation = null;

        if (content.TryGetProperty("properties", out var properties))
        {
            var requiredFields = content.TryGetProperty("required", out var reqArray)
                ? reqArray.EnumerateArray().Select(r => r.GetString()!).ToHashSet()
                : new HashSet<string>();

            // When merge=false, clear existing data schemas before applying
            if (!merge)
            {
                action.DataSchemas = [];
            }

            builder.AddAction(actionId, a =>
            {
                a.WithTitle(action.Title);
                a.SentBy(action.Sender ?? string.Empty);
                if (!string.IsNullOrEmpty(action.Description))
                {
                    a.WithDescription(action.Description);
                }

                a.RequiresData(d =>
                {
                    foreach (var prop in properties.EnumerateObject())
                    {
                        var fieldName = prop.Name;
                        var fieldDef = prop.Value;
                        var fieldType = fieldDef.TryGetProperty("type", out var typeProp)
                            ? typeProp.GetString() ?? "string"
                            : "string";
                        var fieldTitle = fieldDef.TryGetProperty("title", out var titleProp)
                            ? titleProp.GetString()
                            : fieldName;
                        var fieldDescription = fieldDef.TryGetProperty("description", out var descProp)
                            ? descProp.GetString()
                            : null;
                        var isRequired = requiredFields.Contains(fieldName);

                        switch (fieldType.ToLowerInvariant())
                        {
                            case "string":
                                d.AddString(fieldName, f =>
                                {
                                    if (fieldTitle != null) f.WithTitle(fieldTitle);
                                    if (fieldDescription != null) f.WithDescription(fieldDescription);
                                    if (isRequired) f.IsRequired();
                                    if (fieldDef.TryGetProperty("format", out var fmt))
                                        f.WithFormat(fmt.GetString()!);
                                    if (fieldDef.TryGetProperty("minLength", out var minLen))
                                        f.WithMinLength(minLen.GetInt32());
                                    if (fieldDef.TryGetProperty("maxLength", out var maxLen))
                                        f.WithMaxLength(maxLen.GetInt32());
                                    if (fieldDef.TryGetProperty("pattern", out var pat))
                                        f.WithPattern(pat.GetString()!);
                                });
                                break;
                            case "number":
                                d.AddNumber(fieldName, f =>
                                {
                                    if (fieldTitle != null) f.WithTitle(fieldTitle);
                                    if (fieldDescription != null) f.WithDescription(fieldDescription);
                                    if (isRequired) f.IsRequired();
                                    if (fieldDef.TryGetProperty("minimum", out var min))
                                        f.WithMinimum(min.GetDouble());
                                    if (fieldDef.TryGetProperty("maximum", out var max))
                                        f.WithMaximum(max.GetDouble());
                                });
                                break;
                            case "integer":
                                d.AddInteger(fieldName, f =>
                                {
                                    if (fieldTitle != null) f.WithTitle(fieldTitle);
                                    if (fieldDescription != null) f.WithDescription(fieldDescription);
                                    if (isRequired) f.IsRequired();
                                    if (fieldDef.TryGetProperty("minimum", out var min))
                                        f.WithMinimum(min.GetInt32());
                                    if (fieldDef.TryGetProperty("maximum", out var max))
                                        f.WithMaximum(max.GetInt32());
                                });
                                break;
                            case "boolean":
                                d.AddBoolean(fieldName, f =>
                                {
                                    if (fieldTitle != null) f.WithTitle(fieldTitle);
                                    if (fieldDescription != null) f.WithDescription(fieldDescription);
                                    if (isRequired) f.IsRequired();
                                });
                                break;
                            default:
                                d.AddString(fieldName, f =>
                                {
                                    if (fieldTitle != null) f.WithTitle(fieldTitle);
                                    if (isRequired) f.IsRequired();
                                });
                                break;
                        }

                        fieldsAdded.Add(fieldName);
                    }
                });
            });
        }

        // Extract disclosure recommendation if present
        if (content.TryGetProperty("x-sorcha-disclosure", out var disclosureProp)
            && disclosureProp.TryGetProperty("recommendation", out var recProp))
        {
            disclosureRecommendation = recProp.GetString();
        }

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                message = $"Applied '{(content.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : schemaId)}' schema to action '{action.Title}'",
                schemaId,
                actionId,
                fieldsAdded,
                disclosureRecommendation = disclosureRecommendation ?? string.Empty,
                blueprintChanged = true
            },
            blueprintChanged: true);
    }

    private async Task<ToolResult> ExecuteSearchTemplatesAsync(
        JsonDocument arguments,
        BlueprintBuilder builder,
        CancellationToken cancellationToken)
    {
        var root = arguments.RootElement;
        var query = root.GetProperty("query").GetString()!;
        var category = root.TryGetProperty("category", out var catProp) ? catProp.GetString() : null;

        _logger.LogDebug("Searching templates with query '{Query}', category '{Category}'", query, category);

        var allTemplates = await _templateService.GetPublishedTemplatesAsync(cancellationToken);

        var filtered = allTemplates.Where(t =>
            (t.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (t.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (t.Category?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (t.Tags?.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? false));

        if (!string.IsNullOrEmpty(category))
        {
            filtered = filtered.Where(t =>
                t.Category != null &&
                t.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        var resultList = filtered.Select(t => new
        {
            id = t.Id,
            title = t.Title,
            category = t.Category ?? string.Empty,
            description = t.Description,
            version = t.Version
        }).ToList();

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                results = resultList,
                totalCount = resultList.Count,
                message = resultList.Count > 0
                    ? $"Found {resultList.Count} template(s) matching '{query}'"
                    : $"No templates found matching '{query}'"
            },
            blueprintChanged: false);
    }

    private ToolResult ExecuteRequireCredential(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var actionId = root.GetProperty("actionId").GetInt32();
        var credentialType = root.GetProperty("credentialType").GetString()!;
        var description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() : null;

        // Parse revocation policy
        var revocationPolicy = RevocationCheckPolicy.FailClosed;
        if (root.TryGetProperty("revocationPolicy", out var revProp))
        {
            Enum.TryParse(revProp.GetString(), ignoreCase: true, out revocationPolicy);
        }

        // Parse accepted issuers
        var anyOfGroup = root.TryGetProperty("anyOfGroup", out var groupProp) ? groupProp.GetString() : null;
        var acceptedIssuers = root.TryGetProperty("acceptedIssuers", out var issuersProp)
            ? issuersProp.EnumerateArray().Select(i => i.GetString()!).ToList()
            : new List<string>();

        // Parse required claims
        List<ClaimConstraint>? requiredClaims = null;
        if (root.TryGetProperty("requiredClaims", out var claimsProp))
        {
            requiredClaims = claimsProp.EnumerateArray().Select(c =>
            {
                var constraint = new ClaimConstraint
                {
                    ClaimName = c.GetProperty("claimName").GetString()!
                };
                if (c.TryGetProperty("expectedValue", out var ev) && ev.ValueKind != JsonValueKind.Null)
                {
                    constraint.ExpectedValue = ev.ValueKind switch
                    {
                        JsonValueKind.String => ev.GetString(),
                        JsonValueKind.Number => ev.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => ev.GetRawText()
                    };
                }
                return constraint;
            }).ToList();
        }

        var draft = builder.BuildDraft();
        var action = draft.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action == null)
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                $"Action with ID {actionId} not found");
        }

        // Feature 135: accepted issuers become a did-allowlist trust source; when none are
        // supplied the requirement carries no policy and the verifier applies the default
        // (register/DID source at low assurance — FR-026).
        var requirement = new CredentialRequirement
        {
            Type = credentialType,
            AnyOfGroup = string.IsNullOrWhiteSpace(anyOfGroup) ? null : anyOfGroup,
            TrustPolicy = acceptedIssuers.Count > 0
                ? TrustPolicyExtensions.FromLegacyIssuers(acceptedIssuers)
                : null,
            RequiredClaims = requiredClaims,
            RevocationCheckPolicy = revocationPolicy,
            Description = description
        };

        // Add to action's credential requirements (convert to mutable list)
        var requirements = action.CredentialRequirements?.ToList() ?? [];
        requirements.Add(requirement);
        action.CredentialRequirements = requirements;

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                message = $"Added credential requirement '{credentialType}' to action '{action.Title}'",
                actionId,
                credentialType,
                acceptedIssuers,
                anyOfGroup = anyOfGroup ?? "(none — this requirement is independently required)",
                requiredClaims = requiredClaims?.Select(c => new { c.ClaimName, c.ExpectedValue }) ?? [],
                revocationPolicy = revocationPolicy.ToString()
            },
            blueprintChanged: true);
    }

    private ToolResult ExecuteIssueCredential(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var actionId = root.GetProperty("actionId").GetInt32();
        var credentialType = root.GetProperty("credentialType").GetString()!;
        var recipientParticipantId = root.GetProperty("recipientParticipantId").GetString()!;
        var expiryDuration = root.TryGetProperty("expiryDuration", out var expProp) ? expProp.GetString() : null;

        // Parse usage policy
        var usagePolicy = UsagePolicy.Reusable;
        if (root.TryGetProperty("usagePolicy", out var usageProp))
        {
            Enum.TryParse(usageProp.GetString(), ignoreCase: true, out usagePolicy);
        }

        // Parse claim mappings
        var claimMappings = root.GetProperty("claimMappings").EnumerateArray().Select(m =>
            new ClaimMapping
            {
                ClaimName = m.GetProperty("claimName").GetString()!,
                SourceField = m.GetProperty("sourceField").GetString()!
            }).ToList();

        var draft = builder.BuildDraft();
        var action = draft.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action == null)
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                $"Action with ID {actionId} not found");
        }

        // Validate recipient participant exists
        var participantIds = draft.Participants.Select(p => p.Id).ToHashSet();
        if (!participantIds.Contains(recipientParticipantId))
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                $"Recipient participant '{recipientParticipantId}' not found. " +
                $"Available participants: {string.Join(", ", participantIds)}");
        }

        // vct — SD-JWT VC §3.2.2.1 makes this the credential's SOLE type claim, and it is REQUIRED.
        // Until now this tool could not set it at all, so every AI-authored blueprint fell back to the
        // bare `credentialType` name. A credential with no absolute-URI vct cannot be matched to a
        // requested type by any conformant verifier (the #1540 failure, on the HAIP rail).
        var vct = root.TryGetProperty("vct", out var vctProp) ? vctProp.GetString() : null;
        if (string.IsNullOrWhiteSpace(vct))
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                "vct is required. Per SD-JWT VC it is the credential's ONLY type claim, so a credential " +
                "without one cannot be matched to a requested type by any conforming verifier. Pass an " +
                "absolute URI, e.g. 'https://sorcha.dev/vc/training-completion/v1'.");
        }
        if (!Uri.TryCreate(vct, UriKind.Absolute, out _))
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                $"vct '{vct}' is not an absolute URI. Use the canonical form, e.g. " +
                "https://sorcha.dev/vc/{type}/v1 — a relative or bare-name vct mints an unmatchable credential.");
        }

        var displayName = root.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() : null;

        // issuanceCondition — JSON Logic over the submitted action data, gating whether the credential
        // is actually minted. Without it a single decision action ALWAYS issues, so a rejection still
        // mints and delivers a credential: the fail-open defect Feature 176 exists to close. An
        // approve/reject workflow authored without this reproduces it.
        // A null Disclosable is expanded to EVERY claim name by SdJwtService, so leaving it unset is
        // not "disclose nothing" - it is "disclose everything" (#1550).
        List<string>? disclosable = null;
        if (root.TryGetProperty("disclosable", out var discProp) && discProp.ValueKind == JsonValueKind.Array)
        {
            disclosable = discProp.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
        }

        var holderKeySourceField = root.TryGetProperty("holderKeySourceField", out var hkProp)
            ? hkProp.GetString()
            : null;

        JsonNode? issuanceCondition = null;
        if (root.TryGetProperty("issuanceCondition", out var condProp) && condProp.ValueKind != JsonValueKind.Null)
        {
            try
            {
                issuanceCondition = JsonNode.Parse(condProp.GetRawText());
            }
            catch (JsonException ex)
            {
                return ToolResult.Failed(
                    Guid.NewGuid().ToString(),
                    $"issuanceCondition is not valid JSON Logic: {ex.Message}. " +
                    "Example: {\"==\": [{\"var\": \"decision\"}, \"approved\"]}");
            }
        }

        action.CredentialIssuanceConfig = new CredentialIssuanceConfig
        {
            CredentialType = credentialType,
            Vct = vct,
            DisplayName = displayName,
            ClaimMappings = claimMappings,
            RecipientParticipantId = recipientParticipantId,
            ExpiryDuration = expiryDuration,
            UsagePolicy = usagePolicy,
            IssuanceCondition = issuanceCondition,
            Disclosable = disclosable,
            HolderKeySourceField = holderKeySourceField
        };

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                message = $"Configured action '{action.Title}' to issue '{credentialType}' to '{recipientParticipantId}'",
                actionId,
                credentialType,
                claimCount = claimMappings.Count,
                recipientParticipantId,
                expiryDuration = expiryDuration ?? "none",
                usagePolicy = usagePolicy.ToString(),
                vct = vct ?? "(none — falls back to credentialType, not conformant)",
                displayName = displayName ?? "(none)",
                issuanceCondition = issuanceCondition?.ToJsonString() ?? "(none — ALWAYS issues, including on rejection)",
                disclosable = disclosable is { Count: > 0 }
                    ? string.Join(", ", disclosable)
                    : "(none set — EVERY claim is selectively disclosable)",
                holderKeySourceField = holderKeySourceField ?? "(none — an open/late-bound recipient cannot be delivered to)"
            },
            blueprintChanged: true);
    }

    /// <summary>
    /// Escape-hatch: replaces or appends a full raw JSON Schema document on the action's
    /// <c>dataSchemas</c>. Lets the AI emit shapes the typed tools cannot — nested objects,
    /// arrays, <c>$ref</c>, <c>x-pages</c>/<c>x-sections</c>/<c>x-introduction</c>/<c>x-width</c>,
    /// <c>x-persona</c>, <c>x-credential-offer</c>, <c>x-review</c>, <c>x-file</c>,
    /// <c>formatMinimum</c>/<c>formatMaximum</c>, etc.
    /// </summary>
    private ToolResult ExecuteSetActionSchema(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var actionId = root.GetProperty("actionId").GetInt32();
        var schemaElem = root.GetProperty("schema");
        var mode = root.TryGetProperty("mode", out var modeProp)
            ? (modeProp.GetString() ?? "replace").ToLowerInvariant()
            : "replace";

        if (schemaElem.ValueKind != JsonValueKind.Object)
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                "schema must be a JSON Schema object (with at minimum a 'type' property).");
        }

        var draft = builder.BuildDraft();
        var action = draft.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action == null)
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                $"Action with ID {actionId} not found");
        }

        var doc = JsonDocument.Parse(schemaElem.GetRawText());
        var schemas = mode == "append"
            ? (action.DataSchemas?.ToList() ?? [])
            : new List<JsonDocument>();
        schemas.Add(doc);
        action.DataSchemas = schemas;

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                message = $"{(mode == "append" ? "Appended" : "Replaced")} schema on action '{action.Title}'",
                actionId,
                mode,
                schemaCount = schemas.Count
            },
            blueprintChanged: true);
    }

    /// <summary>
    /// Escape-hatch: replaces the action's full <c>routes</c> array. Supports terminal routes
    /// (empty <c>nextActionIds</c>), parallel branches (multiple ids), raw JSON Logic conditions,
    /// <c>branchDeadline</c>, and <c>outputMapping</c> for payload carry-forward (Feature 104).
    /// </summary>
    private ToolResult ExecuteSetActionRoutes(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var actionId = root.GetProperty("actionId").GetInt32();
        var routesElem = root.GetProperty("routes");

        if (routesElem.ValueKind != JsonValueKind.Array)
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                "routes must be an array (use [] to clear all routes).");
        }

        var draft = builder.BuildDraft();
        var action = draft.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action == null)
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                $"Action with ID {actionId} not found");
        }

        var routes = new List<Sorcha.Blueprint.Models.Route>();
        var index = 0;
        foreach (var routeElem in routesElem.EnumerateArray())
        {
            var id = routeElem.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                ? idProp.GetString()!
                : $"route_{index}";

            var nextActionIds = routeElem.TryGetProperty("nextActionIds", out var nextProp)
                && nextProp.ValueKind == JsonValueKind.Array
                ? nextProp.EnumerateArray().Select(n => n.GetInt32()).ToList()
                : new List<int>();

            var route = new Sorcha.Blueprint.Models.Route
            {
                Id = id,
                NextActionIds = nextActionIds,
                IsDefault = routeElem.TryGetProperty("isDefault", out var defProp) && defProp.GetBoolean()
            };

            if (routeElem.TryGetProperty("description", out var descProp)
                && descProp.ValueKind == JsonValueKind.String)
            {
                route.Description = descProp.GetString();
            }

            if (routeElem.TryGetProperty("branchDeadline", out var deadlineProp)
                && deadlineProp.ValueKind == JsonValueKind.String)
            {
                route.BranchDeadline = deadlineProp.GetString();
            }

            if (routeElem.TryGetProperty("condition", out var condProp)
                && condProp.ValueKind != JsonValueKind.Null
                && condProp.ValueKind != JsonValueKind.Undefined)
            {
                route.Condition = System.Text.Json.Nodes.JsonNode.Parse(condProp.GetRawText());
            }

            if (routeElem.TryGetProperty("outputMapping", out var omProp)
                && omProp.ValueKind == JsonValueKind.Object)
            {
                var map = new Dictionary<string, string>();
                foreach (var prop in omProp.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        map[prop.Name] = prop.Value.GetString()!;
                    }
                }
                if (map.Count > 0)
                {
                    route.OutputMapping = map;
                }
            }

            routes.Add(route);
            index++;
        }

        action.Routes = routes;

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                message = $"Set {routes.Count} route(s) on action '{action.Title}'",
                actionId,
                routeCount = routes.Count,
                hasTerminal = routes.Any(r => !r.NextActionIds.Any()),
                hasOutputMapping = routes.Any(r => r.OutputMapping != null && r.OutputMapping.Count > 0)
            },
            blueprintChanged: true);
    }

    /// <summary>
    /// Escape-hatch: sparse update of action metadata that the typed tools cannot fully express —
    /// <c>isStartingAction</c>, <c>instructions</c>, <c>requiredPriorActions</c>, <c>rejectionConfig</c>,
    /// and the full <c>credentialRequirements</c> / <c>credentialIssuanceConfig</c> shapes including
    /// <c>presentationSource</c> and <c>targetAudience</c>. Only provided fields are updated.
    /// </summary>
    private ToolResult ExecuteSetActionMetadata(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var actionId = root.GetProperty("actionId").GetInt32();

        var draft = builder.BuildDraft();
        var action = draft.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action == null)
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                $"Action with ID {actionId} not found");
        }

        var updated = new List<string>();

        if (root.TryGetProperty("isStartingAction", out var startProp)
            && (startProp.ValueKind == JsonValueKind.True || startProp.ValueKind == JsonValueKind.False))
        {
            action.IsStartingAction = startProp.GetBoolean();
            updated.Add("isStartingAction");
        }

        if (root.TryGetProperty("instructions", out var instrProp)
            && instrProp.ValueKind == JsonValueKind.String)
        {
            action.Instructions = instrProp.GetString();
            updated.Add("instructions");
        }

        if (root.TryGetProperty("requiredPriorActions", out var rpaProp)
            && rpaProp.ValueKind == JsonValueKind.Array)
        {
            action.RequiredPriorActions = rpaProp.EnumerateArray().Select(n => n.GetInt32()).ToList();
            updated.Add("requiredPriorActions");
        }

        if (root.TryGetProperty("rejectionConfig", out var rejProp))
        {
            if (rejProp.ValueKind == JsonValueKind.Null)
            {
                action.RejectionConfig = null;
                updated.Add("rejectionConfig (cleared)");
            }
            else if (rejProp.ValueKind == JsonValueKind.Object)
            {
                var rc = JsonSerializer.Deserialize<Sorcha.Blueprint.Models.RejectionConfig>(rejProp.GetRawText())
                    ?? throw new InvalidOperationException("Failed to deserialize rejectionConfig");
                action.RejectionConfig = rc;
                updated.Add("rejectionConfig");
            }
        }

        if (root.TryGetProperty("credentialRequirements", out var crProp))
        {
            if (crProp.ValueKind == JsonValueKind.Null)
            {
                action.CredentialRequirements = null;
                updated.Add("credentialRequirements (cleared)");
            }
            else if (crProp.ValueKind == JsonValueKind.Array)
            {
                var reqs = JsonSerializer.Deserialize<List<CredentialRequirement>>(crProp.GetRawText())
                    ?? new List<CredentialRequirement>();
                action.CredentialRequirements = reqs;
                updated.Add($"credentialRequirements ({reqs.Count})");
            }
        }

        if (root.TryGetProperty("credentialIssuanceConfig", out var ciProp))
        {
            if (ciProp.ValueKind == JsonValueKind.Null)
            {
                action.CredentialIssuanceConfig = null;
                updated.Add("credentialIssuanceConfig (cleared)");
            }
            else if (ciProp.ValueKind == JsonValueKind.Object)
            {
                var cfg = JsonSerializer.Deserialize<CredentialIssuanceConfig>(ciProp.GetRawText())
                    ?? throw new InvalidOperationException("Failed to deserialize credentialIssuanceConfig");
                action.CredentialIssuanceConfig = cfg;
                updated.Add("credentialIssuanceConfig");
            }
        }

        if (updated.Count == 0)
        {
            return ToolResult.Failed(
                Guid.NewGuid().ToString(),
                "No metadata fields provided. Pass at least one of: isStartingAction, instructions, " +
                "requiredPriorActions, rejectionConfig, credentialRequirements, credentialIssuanceConfig.");
        }

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                message = $"Updated action '{action.Title}' metadata: {string.Join(", ", updated)}",
                actionId,
                updated
            },
            blueprintChanged: true);
    }

    private ToolResult ExecuteValidateBlueprint(JsonDocument arguments, BlueprintBuilder builder)
    {
        var errors = new List<object>();
        var warnings = new List<object>();

        try
        {
            var draft = builder.BuildDraft();

            // Check minimum participants
            if (draft.Participants.Count < 2)
            {
                errors.Add(new
                {
                    code = "MIN_PARTICIPANTS",
                    message = "Blueprint requires at least 2 participants",
                    location = "participants"
                });
            }

            // Duplicate participant ids (issue #1548/#1549). Nothing can disambiguate two
            // participants sharing an id — action.sender resolves by id — and the designer
            // produced exactly this when it rebuilt a draft. Checked regardless of routing style.
            foreach (var duplicateId in draft.Participants
                         .GroupBy(p => p.Id, StringComparer.Ordinal)
                         .Where(g => g.Count() > 1)
                         .Select(g => g.Key))
            {
                errors.Add(new
                {
                    code = "DUPLICATE_PARTICIPANT_ID",
                    message = $"Participant id '{duplicateId}' is declared more than once. " +
                              $"action.sender resolves participants by id, so a duplicate cannot be disambiguated.",
                    location = "participants"
                });
            }

            // Check minimum actions
            if (draft.Actions.Count < 1)
            {
                errors.Add(new
                {
                    code = "MIN_ACTIONS",
                    message = "Blueprint requires at least 1 action",
                    location = "actions"
                });
            }

            // Check title
            if (string.IsNullOrWhiteSpace(draft.Title) || draft.Title.Length < 3)
            {
                errors.Add(new
                {
                    code = "INVALID_TITLE",
                    message = "Blueprint title must be at least 3 characters",
                    location = "title"
                });
            }

            // Check description
            if (string.IsNullOrWhiteSpace(draft.Description) || draft.Description.Length < 5)
            {
                errors.Add(new
                {
                    code = "INVALID_DESCRIPTION",
                    message = "Blueprint description must be at least 5 characters",
                    location = "description"
                });
            }

            // Check for starting action
            var hasStartingAction = draft.Actions.Any(a => a.IsStartingAction);
            if (!hasStartingAction && draft.Actions.Count > 0)
            {
                warnings.Add(new
                {
                    code = "NO_STARTING_ACTION",
                    message = "No action is marked as a starting action",
                    location = "actions"
                });
            }

            // ---- Route reachability (issue #1548) -------------------------------------------
            // The chat validator used to answer VALID for a blueprint that cannot execute: a
            // starting action with no routes, and every other route looping back to it. /publish
            // then refused it, so the author's first sight of the problem was at Go-live.
            //
            // The reachability graph below is GATED on the blueprint actually using route-based
            // routing, because legacy and platform-driven blueprints (complex-sme-invoice-finance,
            // register-governance-v1) declare no routes and are advanced by other means.
            //
            // ⚠ That gate had a hole, found by a live designer run: a multi-action blueprint with
            // NO routes anywhere skipped every check and validated clean — then PUBLISHED, because
            // the publish path reports unreachability only as a warning. A workflow that cannot
            // advance past its starting action reached a register. Corpus-testing the rule against
            // 45 shipped blueprints could not surface this, because none of them had that shape.
            //
            // So zero-route is checked FIRST and explicitly, rather than silently exempted.
            var usesRouteBasedRouting = draft.Actions.Any(a => a.Routes?.Any() == true);

            // add_action's routeToNext parameter populates Action.Participants (a Condition list),
            // NOT Routes — it is the legacy participant-based model, and the chat tools still offer
            // it. A blueprint routed that way is coherent, so it must not trip the check below.
            var usesParticipantRouting = draft.Actions.Any(a => a.Participants?.Any() == true);

            if (!usesRouteBasedRouting && !usesParticipantRouting && draft.Actions.Count > 1)
            {
                errors.Add(new
                {
                    code = "NO_ROUTING_DEFINED",
                    message = $"The blueprint has {draft.Actions.Count} actions but declares no routes on any of " +
                              $"them, so nothing can advance past the starting action. Add routes linking each " +
                              $"action to the next, ending with an empty nextActionIds to finish the workflow.",
                    location = "actions"
                });
            }

            if (usesRouteBasedRouting && draft.Actions.Count > 0)
            {
                static List<Sorcha.Blueprint.Models.Route> RoutesOf(Sorcha.Blueprint.Models.Action a)
                    => a.Routes?.ToList() ?? [];

                var startingActions = draft.Actions.Where(a => a.IsStartingAction).ToList();

                // A starting action with nowhere to go cannot advance the workflow. A single-action
                // blueprint (e.g. a credential gate) is legitimately terminal, hence the count test.
                foreach (var start in startingActions.Where(a => draft.Actions.Count > 1 && RoutesOf(a).Count == 0))
                {
                    errors.Add(new
                    {
                        code = "STARTING_ACTION_NO_ROUTES",
                        message = $"Starting action {start.Id} ('{start.Title}') declares no routes, so the " +
                                  $"workflow can never advance past it. Add a route to the action that follows it.",
                        location = $"actions[{start.Id}].routes"
                    });
                }

                if (startingActions.Count > 0)
                {
                    var byId = draft.Actions.ToDictionary(a => a.Id);
                    var reachable = startingActions.Select(a => a.Id).ToHashSet();
                    var pending = new Stack<int>(reachable);
                    while (pending.Count > 0)
                    {
                        if (!byId.TryGetValue(pending.Pop(), out var current)) continue;

                        var next = RoutesOf(current).SelectMany(r => r.NextActionIds).ToList();
                        if (current.RejectionConfig is not null)
                        {
                            next.Add(current.RejectionConfig.TargetActionId);
                        }

                        foreach (var id in next.Where(reachable.Add))
                        {
                            pending.Push(id);
                        }
                    }

                    foreach (var orphan in draft.Actions.Where(a => !reachable.Contains(a.Id)))
                    {
                        errors.Add(new
                        {
                            code = "UNREACHABLE_ACTION",
                            message = $"Action {orphan.Id} ('{orphan.Title}') is not reachable from any starting " +
                                      $"action by routes or rejection targets, so it can never run.",
                            location = $"actions[{orphan.Id}]"
                        });
                    }
                }

                // Something must be able to END. An action with no routes is terminal by absence;
                // a route with an empty nextActionIds is terminal by declaration. A cyclic
                // blueprint declares itself so and is exempt.
                var declaresCycles = draft.Metadata is not null
                    && draft.Metadata.TryGetValue("hasCycles", out var cyclesFlag)
                    && string.Equals(cyclesFlag, "true", StringComparison.OrdinalIgnoreCase);

                var hasTerminal = draft.Actions.Any(a =>
                    RoutesOf(a).Count == 0 || RoutesOf(a).Any(r => !r.NextActionIds.Any()));

                if (!hasTerminal && !declaresCycles)
                {
                    warnings.Add(new
                    {
                        code = "NO_TERMINAL_PATH",
                        message = "No action ends the workflow — every action routes onward and none declares " +
                                  "an empty nextActionIds. Add a terminal route, or set metadata.hasCycles = \"true\" " +
                                  "if the loop is intentional.",
                        location = "actions"
                    });
                }
            }

            // Validate action participant references
            var participantIds = draft.Participants.Select(p => p.Id).ToHashSet();
            foreach (var action in draft.Actions)
            {
                if (action.Participants != null)
                {
                    foreach (var participant in action.Participants)
                    {
                        if (!string.IsNullOrEmpty(participant.Principal) &&
                            !participantIds.Contains(participant.Principal))
                        {
                            errors.Add(new
                            {
                                code = "INVALID_PARTICIPANT_REF",
                                message = $"Action '{action.Title}' references non-existent participant '{participant.Principal}'",
                                location = $"actions[{action.Id}]"
                            });
                        }
                    }
                }
            }

            // Validate credential requirements (T023)
            foreach (var action in draft.Actions)
            {
                if (action.CredentialRequirements?.Any() ?? false)
                {
                    foreach (var req in action.CredentialRequirements)
                    {
                        // Feature 135: "open issuer" = no trust policy declared (verifier will
                        // fall back to the default register source).
                        if (req.TrustPolicy is null || req.TrustPolicy.Sources.Count == 0)
                        {
                            warnings.Add(new
                            {
                                code = "OPEN_CREDENTIAL_ISSUER",
                                message = $"Action '{action.Title}' requires credential '{req.Type}' but declares no trust policy. Consider specifying trusted issuers or trust sources.",
                                location = $"actions[{action.Id}].credentialRequirements"
                            });
                        }
                    }
                }

                // Validate credential issuance (T027)
                if (action.CredentialIssuanceConfig is not null)
                {
                    var issuance = action.CredentialIssuanceConfig;

                    if (!string.IsNullOrEmpty(issuance.RecipientParticipantId) &&
                        !participantIds.Contains(issuance.RecipientParticipantId))
                    {
                        warnings.Add(new
                        {
                            code = "INVALID_CREDENTIAL_RECIPIENT",
                            message = $"Action '{action.Title}' issues credential to '{issuance.RecipientParticipantId}' which is not a known participant",
                            location = $"actions[{action.Id}].credentialIssuanceConfig"
                        });
                    }

                    // WARN_BP_CRED_005 (#1550/#1551) — mirrors the publish-time rule so the author
                    // is told at authoring time, not at Go-live. Minting runs BEFORE routing, so an
                    // action that models a decision and omits issuanceCondition issues to the
                    // rejected party too; a terminal reject route does not prevent the mint.
                    if (issuance.IssuanceCondition is null)
                    {
                        var decisionRoutes = action.Routes?.ToList() ?? [];
                        var conditionalRoutes = decisionRoutes.Count(r => r.Condition is not null);
                        if (conditionalRoutes > 0 || decisionRoutes.Count > 1)
                        {
                            warnings.Add(new
                            {
                                code = Sorcha.Blueprint.Models.ValidationWarningCodes.UnconditionalIssuanceOnDecision,
                                message = $"Action '{action.Title}' issues a credential with no issuanceCondition but " +
                                          $"routes on a decision. Minting runs before routing, so the credential is " +
                                          $"minted and delivered on every path — including the reject path. Add " +
                                          $"issuanceCondition, e.g. {{\"==\": [{{\"var\": \"decision\"}}, \"approved\"]}}.",
                                location = $"actions[{action.Id}].credentialIssuanceConfig.issuanceCondition"
                            });
                        }
                    }

                    // A null Disclosable is expanded to EVERY claim name at signing time, so leaving
                    // it unset silently makes all claims disclosable rather than none (#1550).
                    if (issuance.Disclosable is null && issuance.ClaimMappings.Any())
                    {
                        warnings.Add(new
                        {
                            code = "NO_DISCLOSABLE_SET",
                            message = $"Action '{action.Title}' issues a credential with no 'disclosable' list, so " +
                                      $"EVERY claim becomes selectively disclosable (a null set is expanded to all " +
                                      $"claim names — it does not mean 'none'). List the claims a verifier should see.",
                            location = $"actions[{action.Id}].credentialIssuanceConfig.disclosable"
                        });
                    }

                    // Feature: credential VCT decoupling (design §7) — a declared vct must be an
                    // absolute URI (SD-JWT VC vct is a URI). Reject rather than mint an
                    // unmatchable credential. Do NOT apply this to the credentialType fallback.
                    if (!string.IsNullOrWhiteSpace(issuance.Vct) && !Uri.TryCreate(issuance.Vct, UriKind.Absolute, out _))
                    {
                        errors.Add(new
                        {
                            code = "INVALID_CREDENTIAL_VCT",
                            message = $"Action '{action.Title}' credentialIssuanceConfig.vct '{issuance.Vct}' is not an absolute URI. The vct must be an absolute URI, e.g. https://sorcha.dev/vc/{{type}}/v1.",
                            location = $"actions[{action.Id}].credentialIssuanceConfig"
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add(new
            {
                code = "VALIDATION_ERROR",
                message = ex.Message,
                location = "blueprint"
            });
        }

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                isValid = errors.Count == 0,
                errors,
                warnings
            },
            blueprintChanged: false);
    }

    /// <summary>
    /// Feature 142 / US5 — chat-side counterpart to <c>FormLayoutAuthoringService.SetPages/Sections/Width/Introduction</c>.
    /// Bulk-applies presentational <c>x-*</c> form-layout keywords to an action's first data
    /// schema via the shared <see cref="FormLayoutWriter"/>, so direct manipulation and chat
    /// produce byte-equivalent JSON (FR-016). Refuses to write behavioural keywords.
    /// </summary>
    private ToolResult ExecuteSetFormLayout(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var actionId = root.GetProperty("actionId").GetInt32();
        var schemaIndex = root.TryGetProperty("schemaIndex", out var idxEl) ? idxEl.GetInt32() : 0;

        var draft = builder.BuildDraft();
        var action = draft.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action is null)
        {
            return ToolResult.Failed(Guid.NewGuid().ToString(), $"Action with ID {actionId} not found");
        }

        var schemas = action.DataSchemas?.ToList()
            ?? throw new InvalidOperationException("Action has no dataSchemas — call set_action_schema first.");
        if (schemaIndex < 0 || schemaIndex >= schemas.Count)
        {
            return ToolResult.Failed(Guid.NewGuid().ToString(),
                $"schemaIndex {schemaIndex} out of range for {schemas.Count} schema(s).");
        }

        try
        {
            JsonArray? sections = ParseArray(root, "sections");
            JsonArray? pages = ParseArray(root, "pages");
            string? introduction = root.TryGetProperty("introduction", out var introEl) &&
                                   introEl.ValueKind == JsonValueKind.String ? introEl.GetString() : null;
            Dictionary<string, string>? widths = null;
            if (root.TryGetProperty("widths", out var widthsEl) && widthsEl.ValueKind == JsonValueKind.Object)
            {
                widths = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var prop in widthsEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        widths[prop.Name] = prop.Value.GetString()!;
                    }
                }
            }

            schemas[schemaIndex] = FormLayoutWriter.SetFormLayout(
                schemas[schemaIndex], sections, pages, introduction, widths);
            action.DataSchemas = schemas;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Failed(Guid.NewGuid().ToString(), ex.Message);
        }

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                message = $"Applied presentational layout to action '{action.Title}'",
                actionId,
                schemaIndex,
            },
            blueprintChanged: true);
    }

    /// <summary>
    /// Feature 142 / US5 — chat counterpart to <c>FormLayoutAuthoringService.SetFieldPersona</c>.
    /// Sets (or clears, when <c>personaKey</c> is omitted or null) the <c>x-persona</c>
    /// autofill binding on a top-level field of the action's first data schema.
    /// </summary>
    private ToolResult ExecuteSetFieldAutofill(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var actionId = root.GetProperty("actionId").GetInt32();
        var fieldPath = root.GetProperty("fieldPath").GetString()
            ?? throw new InvalidOperationException("fieldPath is required");
        string? personaKey = root.TryGetProperty("personaKey", out var pEl) &&
                             pEl.ValueKind == JsonValueKind.String ? pEl.GetString() : null;

        var draft = builder.BuildDraft();
        var action = draft.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action is null)
        {
            return ToolResult.Failed(Guid.NewGuid().ToString(), $"Action with ID {actionId} not found");
        }
        var schemas = action.DataSchemas?.ToList()
            ?? throw new InvalidOperationException("Action has no dataSchemas — call set_action_schema first.");

        try
        {
            schemas[0] = FormLayoutWriter.SetFieldPersona(schemas[0], fieldPath, personaKey);
            action.DataSchemas = schemas;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Failed(Guid.NewGuid().ToString(), ex.Message);
        }

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                message = personaKey is null
                    ? $"Cleared x-persona on '{fieldPath}'"
                    : $"Bound '{fieldPath}' to persona '{personaKey}'",
                actionId,
                fieldPath,
                personaKey,
            },
            blueprintChanged: true);
    }

    /// <summary>
    /// Feature 142 / US5 — marks a wizard page as the <c>x-review</c> summary page.
    /// Equivalent to the LayoutToolsPanel "Mark review page" button.
    /// </summary>
    private ToolResult ExecuteSetReviewPage(JsonDocument arguments, BlueprintBuilder builder)
    {
        var root = arguments.RootElement;
        var actionId = root.GetProperty("actionId").GetInt32();
        var pageIndex = root.GetProperty("pageIndex").GetInt32();

        var draft = builder.BuildDraft();
        var action = draft.Actions.FirstOrDefault(a => a.Id == actionId);
        if (action is null)
        {
            return ToolResult.Failed(Guid.NewGuid().ToString(), $"Action with ID {actionId} not found");
        }
        var schemas = action.DataSchemas?.ToList()
            ?? throw new InvalidOperationException("Action has no dataSchemas — call set_action_schema first.");

        try
        {
            schemas[0] = FormLayoutWriter.SetReviewPage(schemas[0], pageIndex);
            action.DataSchemas = schemas;
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Failed(Guid.NewGuid().ToString(), ex.Message);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ToolResult.Failed(Guid.NewGuid().ToString(), ex.Message);
        }

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new { message = $"Marked page {pageIndex} as review on action '{action.Title}'", actionId, pageIndex },
            blueprintChanged: true);
    }

    private static JsonArray? ParseArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Array) return null;
        return JsonNode.Parse(el.GetRawText()) as JsonArray;
    }

    private static IReadOnlyList<ToolDefinition> CreateToolDefinitions()
    {
        return new List<ToolDefinition>
        {
            ToolDefinition.Create(
                "create_blueprint",
                "Creates a new blueprint with basic metadata. This must be called first before adding participants or actions.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string", description = "Blueprint title (3-200 characters)", minLength = 3, maxLength = 200 },
                        description = new { type = "string", description = "Blueprint description (5-2000 characters)", minLength = 5, maxLength = 2000 }
                    },
                    required = new[] { "title", "description" }
                }),

            ToolDefinition.Create(
                "add_participant",
                "Adds a participant (actor) to the blueprint. Every blueprint needs at least 2 participants.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Unique participant identifier (e.g., 'applicant', 'reviewer')" },
                        name = new { type = "string", description = "Display name for the participant" },
                        organisation = new { type = "string", description = "Organization the participant belongs to" },
                        role = new { type = "string", @enum = new[] { "person", "organization" }, description = "Whether this is an individual or an organization" }
                    },
                    required = new[] { "id", "name" }
                }),

            ToolDefinition.Create(
                "remove_participant",
                "Removes a participant from the blueprint. Cannot reduce below 2 participants.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Participant ID to remove" }
                    },
                    required = new[] { "id" }
                }),

            ToolDefinition.Create(
                "add_action",
                "Adds a workflow action (step) to the blueprint. Every action needs a sender participant.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "integer", description = "Action sequence number (0-based)" },
                        title = new { type = "string", description = "Action title (e.g., 'Submit Application', 'Review', 'Approve')" },
                        description = new { type = "string", description = "Optional action description" },
                        sender = new { type = "string", description = "Participant ID who performs this action" },
                        isStartingAction = new { type = "boolean", description = "Whether this action can initiate the workflow" },
                        routeToNext = new { type = "string", description = "Participant ID for simple linear routing" },
                        dataFields = new
                        {
                            type = "array",
                            description = "Data fields to collect with optional constraints",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    name = new { type = "string", description = "Field name (camelCase)" },
                                    type = new { type = "string", @enum = new[] { "string", "number", "integer", "boolean", "date", "file" }, description = "Data type" },
                                    title = new { type = "string", description = "Display label" },
                                    description = new { type = "string", description = "Field description" },
                                    required = new { type = "boolean", description = "Whether field is required (default: true)" },
                                    format = new { type = "string", @enum = new[] { "email", "uri", "date-time", "uuid" }, description = "String format validation" },
                                    minLength = new { type = "integer", description = "Minimum string length" },
                                    maxLength = new { type = "integer", description = "Maximum string length" },
                                    pattern = new { type = "string", description = "Regex pattern for string validation" },
                                    minimum = new { type = "number", description = "Minimum value for numbers" },
                                    maximum = new { type = "number", description = "Maximum value for numbers" },
                                    enumValues = new { type = "array", items = new { type = "string" }, description = "Allowed values (dropdown)" }
                                },
                                required = new[] { "name", "type" }
                            }
                        }
                    },
                    required = new[] { "id", "title", "sender" }
                }),

            ToolDefinition.Create(
                "update_action",
                "Modifies an existing action. Only provided fields are updated.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "integer", description = "Action ID to update" },
                        title = new { type = "string" },
                        description = new { type = "string" },
                        sender = new { type = "string" },
                        isStartingAction = new { type = "boolean" }
                    },
                    required = new[] { "id" }
                }),

            ToolDefinition.Create(
                "set_disclosure",
                "Configures which data fields a participant can see at a specific action.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        actionId = new { type = "integer", description = "Action ID where disclosure applies" },
                        participantId = new { type = "string", description = "Participant who receives the disclosure" },
                        fields = new
                        {
                            type = "array",
                            description = "JSON Pointer paths to disclosed fields (e.g., '/applicantName', '/*' for all)",
                            items = new { type = "string" }
                        }
                    },
                    required = new[] { "actionId", "participantId", "fields" }
                }),

            ToolDefinition.Create(
                "add_routing",
                "Adds conditional routing to an action based on data values.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        actionId = new { type = "integer", description = "Action ID to add routing to" },
                        conditions = new
                        {
                            type = "array",
                            description = "Routing conditions",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    field = new { type = "string", description = "Field to evaluate" },
                                    @operator = new { type = "string", @enum = new[] { "equals", "notEquals", "greaterThan", "lessThan", "contains" } },
                                    value = new { description = "Value to compare against" },
                                    routeTo = new { type = "string", description = "Participant ID if condition matches" }
                                },
                                required = new[] { "field", "operator", "value", "routeTo" }
                            }
                        },
                        defaultRoute = new { type = "string", description = "Participant ID for default/else case" }
                    },
                    required = new[] { "actionId", "conditions" }
                }),

            ToolDefinition.Create(
                "validate_blueprint",
                "Validates the current blueprint and returns any errors or warnings. Call this after making changes to ensure the blueprint is valid.",
                new
                {
                    type = "object",
                    properties = new { },
                    required = Array.Empty<string>()
                }),

            ToolDefinition.Create(
                "search_schemas",
                "Search the standardised schema library for reusable data schemas. Returns matching schema summaries including identifier, title, category, description, and field count. Use this to find appropriate schemas before applying them with use_standard_schema.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "Search term to match against schema names, descriptions, tags, and keywords" },
                        category = new
                        {
                            type = "string",
                            @enum = new[] { "people-identity", "financial", "documents-evidence", "compliance-governance", "supply-chain", "healthcare", "credentials" },
                            description = "Optional category filter"
                        }
                    },
                    required = new[] { "query" }
                }),

            ToolDefinition.Create(
                "use_standard_schema",
                "Apply a standardised schema to a blueprint action's data definition. This imports all fields, types, constraints, and form layout from the schema. Call search_schemas first to find the schema identifier.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        schemaId = new { type = "string", description = "Schema identifier (e.g., 'uk-address', 'payment-details')" },
                        actionId = new { type = "integer", description = "Action ID to apply the schema to" },
                        merge = new { type = "boolean", description = "If true, merge with existing fields. If false, replace existing schema. Default: true" }
                    },
                    required = new[] { "schemaId", "actionId" }
                }),

            ToolDefinition.Create(
                "search_templates",
                "Search the blueprint template catalogue for existing templates that match the user's workflow needs. Returns template summaries including title, category, and description. If a good match exists, suggest using it as a starting point rather than building from scratch.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        query = new { type = "string", description = "Search term to match against template titles, descriptions, categories, and tags" },
                        category = new { type = "string", description = "Optional category filter (e.g., 'approval', 'finance', 'demo', 'system')" }
                    },
                    required = new[] { "query" }
                }),

            ToolDefinition.Create(
                "require_credential",
                "Add a Verified Credential requirement to a blueprint action. The participant performing this action must present a valid credential of the specified type before the action can be executed. Use schemas from the 'credentials' category to reference known credential types.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        actionId = new { type = "integer", description = "Action ID to add the credential requirement to" },
                        credentialType = new { type = "string", description = "Type of credential required (e.g., 'TrainingCertificate', 'ProfessionalLicense', 'ProductPassport')" },
                        acceptedIssuers = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            description = "Trusted issuer DIDs. Becomes a did-allowlist trustPolicy source. Empty means any issuer that the register can resolve is accepted. For richer trust (x509/trustlist sources, AllOf combinator, a minimum assurance level) the blueprint must be authored directly — this tool only expresses the allowlist case."
                        },
                        anyOfGroup = new
                        {
                            type = "string",
                            description = "Optional tag making this requirement one of several ALTERNATIVES. Requirements on the same action sharing a tag are satisfied by presenting ANY ONE of them; requirements with no tag are each independently required (AND). Use when 'a passport OR a driving licence' would do."
                        },
                        requiredClaims = new
                        {
                            type = "array",
                            description = "Claims that must be present in the credential",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    claimName = new { type = "string", description = "Claim name to require" },
                                    expectedValue = new { description = "Optional expected value (null means any value accepted)" }
                                },
                                required = new[] { "claimName" }
                            }
                        },
                        revocationPolicy = new
                        {
                            type = "string",
                            @enum = new[] { "FailClosed", "FailOpen" },
                            description = "What happens if revocation status cannot be checked. FailClosed (default): reject. FailOpen: accept."
                        },
                        description = new { type = "string", description = "Human-readable description of why this credential is required" }
                    },
                    required = new[] { "actionId", "credentialType" }
                }),

            ToolDefinition.Create(
                "issue_credential",
                "Configure a blueprint action to issue a Verified Credential when executed. The credential is signed by the action sender's wallet and delivered to the specified recipient. Use this for actions that produce certifications, approvals, or attestations.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        actionId = new { type = "integer", description = "Action ID that issues the credential" },
                        credentialType = new { type = "string", description = "Short readable type name (e.g., 'TrainingCompletionCertificate'). A fallback identity only — ALWAYS pass vct as well." },
                        vct = new
                        {
                            type = "string",
                            description = "REQUIRED in practice. The credential's canonical type identifier and, per SD-JWT VC, its ONLY type claim — an absolute URI, e.g. 'https://sorcha.dev/vc/training-completion/v1'. Omit it and the credential falls back to the bare credentialType, which no conforming verifier can match to a requested type."
                        },
                        displayName = new
                        {
                            type = "string",
                            description = "Human card label shown in the wallet (e.g., 'Training Completion'). Falls back to a humanised vct when omitted."
                        },
                        issuanceCondition = new
                        {
                            type = "object",
                            description = "JSON Logic evaluated over the SUBMITTED action data, gating whether the credential is minted. USE THIS ON ANY APPROVE/REJECT ACTION: without it the credential is issued unconditionally, so a rejection still mints and delivers one. Example: {\"==\": [{\"var\": \"decision\"}, \"approved\"]}. Fails closed — an unevaluable condition skips issuance."
                        },
                        claimMappings = new
                        {
                            type = "array",
                            description = "Map action data fields to credential claims",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    claimName = new { type = "string", description = "Claim name in the issued credential" },
                                    sourceField = new { type = "string", description = "JSON Pointer to action data field (e.g., '/applicantName', '/courseTitle')" }
                                },
                                required = new[] { "claimName", "sourceField" }
                            }
                        },
                        recipientParticipantId = new { type = "string", description = "Participant ID who receives the credential" },
                        expiryDuration = new { type = "string", description = "ISO 8601 duration for credential validity (e.g., 'P365D' for 1 year, 'P2Y' for 2 years)" },
                        disclosable = new
                        {
                            type = "array",
                            description = "Claim names the holder may selectively disclose. OMITTING THIS MAKES EVERY CLAIM DISCLOSABLE - a null set is expanded to all claim names, it does not mean 'none'. List only the claims a verifier should be able to see.",
                            items = new { type = "string" }
                        },
                        holderKeySourceField = new
                        {
                            type = "string",
                            description = "JSON Pointer to the recipient's carried holder key, conventionally '/holderKeys/holderJwk'. REQUIRED when the recipient is an open/late-bound participant with no published participant record, or issuance fails closed at runtime with VAL_RUNTIME_CRED_004 and no credential is delivered. Pair it with a 'sorcha-holder-key' formatted field on the starting action."
                        },
                        usagePolicy = new
                        {
                            type = "string",
                            @enum = new[] { "Reusable", "SingleUse", "LimitedUse" },
                            description = "How many times the credential can be presented. Default: Reusable"
                        }
                    },
                    // vct is required: SD-JWT VC makes it the credential's ONLY type claim, so a
                    // credential without one cannot be matched to a requested type by any conforming
                    // verifier (#1550). The description said "REQUIRED in practice" and was ignored.
                    required = new[] { "actionId", "credentialType", "vct", "claimMappings", "recipientParticipantId" }
                }),

            ToolDefinition.Create(
                "set_action_schema",
                "Advanced. Replaces (or appends to) an action's data schema with a full raw JSON Schema document. " +
                "Use this when add_action's flat fields cannot express the shape you need: nested objects, arrays, " +
                "$ref to core components (PersonName/DateOfBirth/EmailAddress/PostalAddress), x-pages (wizard), " +
                "x-sections / x-introduction / x-width (form layout), x-persona (autofill bindings), " +
                "x-credential-offer (claim card — Feature 104), x-review (id-card summary), x-file " +
                "(file uploads with chunking), or formatMinimum / formatMaximum date constraints with today / " +
                "today+18Y tokens. Prefer add_action / use_standard_schema for simple flat shapes.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        actionId = new { type = "integer", description = "Action ID to attach the schema to" },
                        schema = new
                        {
                            type = "object",
                            description = "A full JSON Schema 2020-12 object. Pass through unchanged — the renderer and " +
                                "validator will resolve $ref, x-pages, x-sections, x-persona, x-credential-offer, etc."
                        },
                        mode = new
                        {
                            type = "string",
                            @enum = new[] { "replace", "append" },
                            description = "replace (default) clears the action's existing dataSchemas; append adds this schema to the existing list."
                        }
                    },
                    required = new[] { "actionId", "schema" }
                }),

            ToolDefinition.Create(
                "set_action_routes",
                "Advanced. Replaces an action's routes with the full Route[] shape. Use this when add_routing cannot " +
                "express what you need: terminal routes (empty nextActionIds means workflow complete), parallel " +
                "branches (multiple nextActionIds with branchDeadline), raw JSON Logic conditions (any operator, not " +
                "just the five add_routing exposes), or outputMapping (Feature 104 payload carry-forward — required " +
                "for credential claim flows). Prefer add_routing for simple linear conditional shapes.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        actionId = new { type = "integer", description = "Action ID whose routes will be replaced" },
                        routes = new
                        {
                            type = "array",
                            description = "Routes evaluated in order. First matching condition wins; the default route fires last.",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    id = new { type = "string", description = "Unique id within the action (e.g., 'approved-to-claim'). Auto-generated if omitted." },
                                    nextActionIds = new
                                    {
                                        type = "array",
                                        items = new { type = "integer" },
                                        description = "Action IDs to route to. Empty array [] terminates the workflow. Multiple IDs create parallel branches."
                                    },
                                    condition = new
                                    {
                                        type = "object",
                                        description = "JSON Logic condition (e.g., { \"==\": [{ \"var\": \"decision\" }, \"approved\"] }). Omit for unconditional routes."
                                    },
                                    isDefault = new { type = "boolean", description = "Marks this as the fall-through route when no other condition matches." },
                                    description = new { type = "string", description = "Human-readable note shown in tooling." },
                                    branchDeadline = new { type = "string", description = "ISO 8601 duration (e.g., 'P7D'). Only meaningful when nextActionIds has multiple entries." },
                                    outputMapping = new
                                    {
                                        type = "object",
                                        description = "Map source JSON Pointer → target JSON Pointer. Source roots: /payload, /calculations, /haip. Target = next action's prepopulated payload. Required for the credential claim card hand-off in Feature 104.",
                                        additionalProperties = new { type = "string" }
                                    }
                                },
                                required = new[] { "nextActionIds" }
                            }
                        }
                    },
                    required = new[] { "actionId", "routes" }
                }),

            ToolDefinition.Create(
                "set_action_metadata",
                "Advanced. Sparse update of action metadata that the typed tools cannot fully express. Pass any " +
                "subset of: isStartingAction (the open-participant flag), instructions (markdown guidance), " +
                "requiredPriorActions, rejectionConfig (full shape with isTerminal / requireReason / " +
                "targetParticipantId / rejectionSchema), credentialRequirements (full shape including " +
                "presentationSource: HaipExternalWallet | SorchaInternal — typed require_credential cannot set this), " +
                "credentialIssuanceConfig (full shape including targetAudience: HaipExternalWallet | SorchaLocalWallet — " +
                "typed issue_credential cannot set this; required for HAIP credential issuance flows). Pass null on a " +
                "field to clear it. Prefer require_credential / issue_credential for simple SorchaInternal cases.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        actionId = new { type = "integer", description = "Action ID to update" },
                        isStartingAction = new
                        {
                            type = "boolean",
                            description = "When true, the action is open: any wallet may submit, the first sender is " +
                                "late-bound to the participant role for the instance. Participant referenced by sender " +
                                "MUST have walletAddress null (publish-time guardrail VAL_BP_010)."
                        },
                        instructions = new { type = "string", description = "Markdown guidance shown to the participant (max 5000 chars)." },
                        requiredPriorActions = new
                        {
                            type = "array",
                            items = new { type = "integer" },
                            description = "Action IDs whose data must be fetched and decrypted to build the accumulated state for routing evaluation. Defaults to immediately preceding action."
                        },
                        rejectionConfig = new
                        {
                            type = "object",
                            description = "Where the workflow goes when this action is rejected. null clears it.",
                            properties = new
                            {
                                targetActionId = new { type = "integer", description = "Action to route to on rejection" },
                                targetParticipantId = new { type = "string", description = "Override sender for the rejection-target action. Optional." },
                                requireReason = new { type = "boolean", description = "Whether a rejection reason is mandatory. Default true." },
                                isTerminal = new { type = "boolean", description = "If true, rejection ends the workflow (Rejected state) instead of routing. Used by the credential claim card decline action." }
                            }
                        },
                        credentialRequirements = new
                        {
                            type = "array",
                            description = "Credentials the participant must present before executing this action. AND-combined. null clears.",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    type = new { type = "string", description = "Credential type (e.g., 'AssuredIdentityCredential')" },
                                    acceptedIssuers = new { type = "array", items = new { type = "string" }, description = "Trusted issuer DIDs/addresses. Empty = any issuer." },
                                    requiredClaims = new
                                    {
                                        type = "array",
                                        items = new
                                        {
                                            type = "object",
                                            properties = new
                                            {
                                                claimName = new { type = "string" },
                                                expectedValue = new { description = "Optional exact-match value." }
                                            }
                                        }
                                    },
                                    revocationCheckPolicy = new { type = "string", @enum = new[] { "FailClosed", "FailOpen" } },
                                    presentationSource = new
                                    {
                                        type = "string",
                                        @enum = new[] { "SorchaInternal", "HaipExternalWallet" },
                                        description = "Where the presentation comes from. SorchaInternal (default) matches against on-platform credentials; HaipExternalWallet requires presentation via the HAIP OpenID4VP verifier — used for credential-bootstrapped open submissions and external wallet flows."
                                    },
                                    description = new { type = "string" }
                                },
                                required = new[] { "type" }
                            }
                        },
                        credentialIssuanceConfig = new
                        {
                            type = "object",
                            description = "Mints a verifiable credential when this action executes. null clears.",
                            properties = new
                            {
                                credentialType = new { type = "string" },
                                claimMappings = new
                                {
                                    type = "array",
                                    items = new
                                    {
                                        type = "object",
                                        properties = new
                                        {
                                            claimName = new { type = "string" },
                                            sourceField = new { type = "string", description = "JSON Pointer to action data field" }
                                        },
                                        required = new[] { "claimName", "sourceField" }
                                    }
                                },
                                recipientParticipantId = new { type = "string", description = "Participant ID who receives the credential" },
                                expiryDuration = new { type = "string", description = "ISO 8601 (e.g., 'P365D')" },
                                registerId = new { type = "string", description = "Optional public register to record on" },
                                disclosable = new { type = "array", items = new { type = "string" }, description = "Selectively disclosable claim names. Omit = all disclosable." },
                                usagePolicy = new { type = "string", @enum = new[] { "Reusable", "SingleUse", "LimitedUse" } },
                                maxPresentations = new { type = "integer", description = "Required when usagePolicy is LimitedUse." },
                                targetAudience = new
                                {
                                    type = "string",
                                    @enum = new[] { "HaipExternalWallet", "SorchaLocalWallet" },
                                    description = "Delivery channel. HaipExternalWallet emits an OpenID4VCI offer (Feature 104 — pair with a separate Claim action carrying x-credential-offer + outputMapping). SorchaLocalWallet (Feature 106) seals the encrypted credential into the action transaction for register-native delivery to an on-platform wallet. Avoid the deprecated SorchaInternal value."
                                }
                            },
                            required = new[] { "credentialType", "claimMappings", "recipientParticipantId" }
                        }
                    },
                    required = new[] { "actionId" }
                }),

            // Feature 142 / US5 — three presentational-only layout tools. They write ONLY the
            // x-sections / x-pages / x-width / x-introduction / x-persona / x-review / x-address-lookup
            // keywords (FormKeywordClassifier.Presentational), so the rehearsal gate is NOT re-locked
            // (FR-023). For x-file / x-credential-offer (behavioural) keep using set_action_schema.
            ToolDefinition.Create(
                "set_form_layout",
                "Apply presentational layout to an action's data schema in one call. " +
                "Writes any combination of x-sections (grouping), x-pages (wizard split), x-width " +
                "(side-by-side hints), and x-introduction. Behavioural keywords (x-file, " +
                "x-credential-offer) are NOT writable here — use set_action_schema for those. " +
                "Direct manipulation and this tool produce byte-equivalent schema JSON.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        actionId = new { type = "integer", description = "Action ID whose data schema to lay out." },
                        schemaIndex = new { type = "integer", description = "Index into dataSchemas (default 0)." },
                        sections = new
                        {
                            type = "array",
                            description = "x-sections array. Each entry: { title, fields:[fieldName,…] }.",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    title = new { type = "string" },
                                    fields = new { type = "array", items = new { type = "string" } },
                                    layout = new { type = "string", @enum = new[] { "vertical", "horizontal", "grid" } },
                                },
                            },
                        },
                        pages = new
                        {
                            type = "array",
                            description = "x-pages array. Each entry: { title, sections:[…] }.",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    title = new { type = "string" },
                                    description = new { type = "string" },
                                    sections = new { type = "array" },
                                },
                            },
                        },
                        introduction = new { type = "string", description = "x-introduction callout shown above the form." },
                        widths = new
                        {
                            type = "object",
                            description = "Map of fieldPath → 'full' | 'half' | 'third' (case-insensitive).",
                            additionalProperties = new { type = "string" },
                        },
                    },
                    required = new[] { "actionId" }
                }),

            ToolDefinition.Create(
                "set_field_autofill",
                "Bind (or, with personaKey omitted/null, unbind) a top-level field to a Sorcha " +
                "persona attribute for autofill. Writes the presentational x-persona keyword — " +
                "does NOT re-lock the rehearsal gate.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        actionId = new { type = "integer" },
                        fieldPath = new { type = "string", description = "JSON Pointer (e.g. '/email') or bare property name." },
                        personaKey = new { type = "string", description = "Persona attribute key (e.g. 'email'). Omit or pass null to clear." },
                    },
                    required = new[] { "actionId", "fieldPath" }
                }),

            ToolDefinition.Create(
                "set_review_page",
                "Mark the wizard page at pageIndex as the x-review summary page (Feature 107 id-card review). " +
                "Requires x-pages to be set first. Writes the presentational x-review keyword.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        actionId = new { type = "integer" },
                        pageIndex = new { type = "integer", description = "Zero-based index into the existing x-pages array." },
                    },
                    required = new[] { "actionId", "pageIndex" }
                })
        };
    }
}
