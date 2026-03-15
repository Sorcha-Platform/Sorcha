// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Microsoft.Extensions.Options;
using Sorcha.Blueprint.Service.Models.Chat;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Tool = Anthropic.SDK.Common.Tool;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// AI provider implementation using Anthropic Claude API.
/// </summary>
public class AnthropicProviderService : IAIProviderService
{
    private readonly AnthropicClient _client;
    private readonly AIProviderOptions _options;
    private readonly ILogger<AnthropicProviderService> _logger;

    public AnthropicProviderService(
        IOptions<AIProviderOptions> options,
        ILogger<AnthropicProviderService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new AnthropicClient(_options.ApiKey);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AIStreamEvent> StreamCompletionAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        string? systemPrompt = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var anthropicMessages = ConvertMessages(messages);
        var anthropicTools = ConvertTools(tools);

        var parameters = new MessageParameters
        {
            Model = _options.Model,
            MaxTokens = _options.MaxTokens,
            Messages = anthropicMessages,
            System = new List<SystemMessage> { new SystemMessage(systemPrompt ?? "You are a helpful assistant.") },
            Tools = anthropicTools,
            Stream = true
        };

        _logger.LogInformation("Starting AI completion stream with model: {Model}, {MessageCount} messages, {ToolCount} tools",
            _options.Model, messages.Count, tools.Count);
        _logger.LogInformation("System prompt length: {Length} chars, Tools: {ToolNames}",
            (systemPrompt ?? "").Length, string.Join(", ", tools.Select(t => t.Name)));
        _logger.LogDebug("Full system prompt: {SystemPrompt}", systemPrompt ?? "You are a helpful assistant.");

        string? currentToolId = null;
        string? currentToolName = null;
        string toolArgumentsJson = "";

        await foreach (var response in _client.Messages.StreamClaudeMessageAsync(parameters, cancellationToken))
        {
            if (response.Delta?.Type == "text_delta" && response.Delta.Text != null)
            {
                yield return new TextChunk(response.Delta.Text);
            }
            else if (response.ContentBlock?.Type == "tool_use")
            {
                // Start of a tool use block
                currentToolId = response.ContentBlock.Id;
                currentToolName = response.ContentBlock.Name;
                toolArgumentsJson = "";
                _logger.LogInformation("AI is calling tool: {ToolName} (ID: {ToolId})", currentToolName, currentToolId);
            }
            else if (response.Delta?.Type == "input_json_delta" && response.Delta.PartialJson != null)
            {
                // Accumulate tool arguments
                toolArgumentsJson += response.Delta.PartialJson;
            }
            else if (response.Delta?.StopReason != null)
            {
                _logger.LogInformation("AI stream ended with stop reason: {StopReason}", response.Delta.StopReason);
                yield return new StreamEnd(response.Delta.StopReason);
            }
            else if (currentToolId != null && currentToolName != null
                     && response.Delta?.Type == null && response.ContentBlock?.Type == null)
            {
                // The SDK sends content_block_stop as an event with all-null properties.
                // When we have an active tool and get this null event, the tool block is complete.
                _logger.LogInformation("Tool block complete: {ToolName} (ID: {ToolId}), args: {ArgsLen} chars",
                    currentToolName, currentToolId, toolArgumentsJson.Length);

                JsonDocument arguments;
                try
                {
                    arguments = string.IsNullOrEmpty(toolArgumentsJson)
                        ? JsonDocument.Parse("{}")
                        : JsonDocument.Parse(toolArgumentsJson);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse tool arguments: {Json}", toolArgumentsJson);
                    arguments = JsonDocument.Parse("{}");
                }

                yield return new ToolUse(currentToolId, currentToolName, arguments);

                currentToolId = null;
                currentToolName = null;
                toolArgumentsJson = "";
            }
        }

        _logger.LogDebug("AI completion stream ended");
    }

    private static List<Message> ConvertMessages(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<Message>();

        foreach (var msg in messages)
        {
            var role = msg.Role == MessageRole.User ? RoleType.User : RoleType.Assistant;

            if (msg.ToolResults != null && msg.ToolResults.Count > 0)
            {
                // Tool result message
                var toolResultContents = msg.ToolResults.Select(tr => new ToolResultContent
                {
                    ToolUseId = tr.ToolCallId,
                    Content = new List<ContentBase>
                    {
                        new TextContent
                        {
                            Text = tr.Success
                                ? tr.Result?.RootElement.GetRawText() ?? "{}"
                                : $"Error: {tr.Error}"
                        }
                    }
                }).ToList();

                result.Add(new Message
                {
                    Role = RoleType.User,
                    Content = toolResultContents.Cast<ContentBase>().ToList()
                });
            }
            else if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
            {
                // Assistant message with tool calls
                var contents = new List<ContentBase>();

                // Only add text content if it's non-empty
                if (!string.IsNullOrWhiteSpace(msg.Content))
                {
                    contents.Add(new TextContent { Text = msg.Content });
                }

                foreach (var tc in msg.ToolCalls)
                {
                    contents.Add(new ToolUseContent
                    {
                        Id = tc.Id,
                        Name = tc.ToolName,
                        Input = JsonNode.Parse(tc.Arguments.RootElement.GetRawText())
                    });
                }

                result.Add(new Message
                {
                    Role = role,
                    Content = contents
                });
            }
            else
            {
                // Regular text message
                result.Add(new Message
                {
                    Role = role,
                    Content = [new TextContent { Text = msg.Content }]
                });
            }
        }

        return result;
    }

    private static List<Tool> ConvertTools(IReadOnlyList<ToolDefinition> tools)
    {
        return tools.Select(t => new Tool(new Function(
            t.Name,
            t.Description,
            JsonNode.Parse(t.InputSchema.RootElement.GetRawText())))).ToList();
    }
}

/// <summary>
/// Configuration options for the AI provider.
/// </summary>
public class AIProviderOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "AIProvider";

    /// <summary>
    /// AI provider name (e.g., "Anthropic").
    /// </summary>
    public string Provider { get; set; } = "Anthropic";

    /// <summary>
    /// API key for the provider.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Model to use (e.g., "claude-sonnet-4-5-20250929").
    /// </summary>
    public string Model { get; set; } = "claude-sonnet-4-5-20250929";

    /// <summary>
    /// Maximum tokens for response.
    /// </summary>
    public int MaxTokens { get; set; } = 4096;
}
