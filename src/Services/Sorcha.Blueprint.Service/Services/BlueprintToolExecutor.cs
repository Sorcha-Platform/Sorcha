// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Schemas.Services;
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
    private readonly ISchemaStore _schemaStore;
    private readonly IBlueprintTemplateService _templateService;
    private readonly IReadOnlyList<ToolDefinition> _toolDefinitions;

    /// <summary>
    /// Initializes a new instance of the <see cref="BlueprintToolExecutor"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="schemaStore">Schema store for searching and retrieving standard schemas.</param>
    /// <param name="templateService">Template service for searching blueprint templates.</param>
    public BlueprintToolExecutor(
        ILogger<BlueprintToolExecutor> logger,
        ISchemaStore schemaStore,
        IBlueprintTemplateService templateService)
    {
        _logger = logger;
        _schemaStore = schemaStore;
        _templateService = templateService;
        _toolDefinitions = CreateToolDefinitions();
    }

    /// <inheritdoc />
    public Task<ToolResult> ExecuteAsync(
        string toolName,
        JsonDocument arguments,
        BlueprintBuilder builder,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing tool {ToolName} with arguments: {Arguments}",
            toolName, arguments.RootElement.GetRawText());

        try
        {
            return toolName switch
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
                _ => Task.FromResult(ToolResult.Failed(Guid.NewGuid().ToString(), $"Unknown tool: {toolName}"))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
            return Task.FromResult(ToolResult.Failed(Guid.NewGuid().ToString(), ex.Message));
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

        var (schemas, totalCount, _) = await _schemaStore.ListAsync(
            search: query,
            cancellationToken: cancellationToken);

        var results = schemas.AsEnumerable();

        // Filter by category (sector tag) if provided
        if (!string.IsNullOrEmpty(category))
        {
            results = results.Where(s =>
                s.SectorTags != null &&
                s.SectorTags.Any(t => t.Equals(category, StringComparison.OrdinalIgnoreCase)));
        }

        var resultList = results.Select(s => new
        {
            identifier = s.Identifier,
            title = s.Title,
            category = s.SectorTags?.FirstOrDefault() ?? s.Category.ToString(),
            description = s.Description ?? string.Empty,
            fieldCount = s.FieldCount ?? 0,
            fieldNames = s.FieldNames ?? [],
            tags = s.SectorTags ?? []
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

        var schema = await _schemaStore.GetByIdentifierAsync(schemaId, cancellationToken: cancellationToken);
        if (schema == null)
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
        var content = schema.Content.RootElement;
        var fieldsAdded = new List<string>();
        string? disclosureRecommendation = null;

        if (content.TryGetProperty("properties", out var properties))
        {
            var requiredFields = content.TryGetProperty("required", out var reqArray)
                ? reqArray.EnumerateArray().Select(r => r.GetString()!).ToHashSet()
                : new HashSet<string>();

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
        if (content.TryGetProperty("x-sorcha-disclosure", out var disclosureProp))
        {
            disclosureRecommendation = disclosureProp.GetString();
        }

        return ToolResult.Succeeded(
            Guid.NewGuid().ToString(),
            new
            {
                message = $"Applied '{schema.Title}' schema to action '{action.Title}'",
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

        var requirement = new CredentialRequirement
        {
            Type = credentialType,
            AcceptedIssuers = acceptedIssuers,
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

        action.CredentialIssuanceConfig = new CredentialIssuanceConfig
        {
            CredentialType = credentialType,
            ClaimMappings = claimMappings,
            RecipientParticipantId = recipientParticipantId,
            ExpiryDuration = expiryDuration,
            UsagePolicy = usagePolicy
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
                usagePolicy = usagePolicy.ToString()
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
                        if (req.AcceptedIssuers == null || !req.AcceptedIssuers.Any())
                        {
                            warnings.Add(new
                            {
                                code = "OPEN_CREDENTIAL_ISSUER",
                                message = $"Action '{action.Title}' requires credential '{req.Type}' but accepts any issuer. Consider specifying trusted issuers.",
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
                            description = "List of trusted issuer DIDs or addresses. Empty array means any issuer is accepted."
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
                        credentialType = new { type = "string", description = "Type of credential to issue (e.g., 'TrainingCompletionCertificate', 'ApprovalAttestation')" },
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
                        usagePolicy = new
                        {
                            type = "string",
                            @enum = new[] { "Reusable", "SingleUse", "LimitedUse" },
                            description = "How many times the credential can be presented. Default: Reusable"
                        }
                    },
                    required = new[] { "actionId", "credentialType", "claimMappings", "recipientParticipantId" }
                })
        };
    }
}
