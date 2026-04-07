// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Configuration;
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Decision;

/// <summary>
/// Calls the Claude API with a persona prompt and action context to generate payloads.
/// </summary>
public partial class AiDecisionEngine : IDecisionEngine
{
    private readonly AiConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiDecisionEngine> _logger;
    private string? _personaPrompt;

    private readonly string _apiKey;

    public AiDecisionEngine(AiConfig config, HttpClient httpClient, ILogger<AiDecisionEngine> logger)
    {
        _config = config;
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException("ANTHROPIC_API_KEY environment variable is not set");
    }

    public async Task<ActionDecision> DecideAsync(PendingAction action, CancellationToken cancellationToken = default)
    {
        // Load persona prompt on first use
        _personaPrompt ??= await LoadPromptAsync(cancellationToken);

        var contextMessage = BuildContextMessage(action, _personaPrompt);

        // First attempt
        var response = await CallClaudeAsync(contextMessage, cancellationToken);
        var payload = TryParsePayload(response);

        if (payload is not null)
        {
            _logger.LogInformation("AI generated valid payload for {ActionName}", action.ActionName);
            return new ActionDecision("approve", payload, $"AI response: {response[..Math.Min(100, response.Length)]}");
        }

        // Retry with error feedback
        _logger.LogWarning("AI payload failed validation, retrying with feedback");
        var retryMessage = $"{contextMessage}\n\nYour previous response could not be parsed as valid JSON. " +
            "Please respond with ONLY a JSON object matching the schema above. No markdown, no explanation.";

        response = await CallClaudeAsync(retryMessage, cancellationToken);
        payload = TryParsePayload(response);

        if (payload is not null)
        {
            _logger.LogInformation("AI retry generated valid payload for {ActionName}", action.ActionName);
            return new ActionDecision("approve", payload, "AI response (retry)");
        }

        _logger.LogError("AI failed to generate valid payload after retry for {ActionName}", action.ActionName);
        return new ActionDecision("skip", null, "AI failed to generate valid payload after retry");
    }

    /// <summary>
    /// Builds the context message sent to the Claude API.
    /// </summary>
    public static string BuildContextMessage(PendingAction action, string personaPrompt)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(personaPrompt);
        sb.AppendLine();
        sb.AppendLine("## Action Context");
        sb.AppendLine($"- **Action Name:** {action.ActionName}");
        sb.AppendLine($"- **Action Index:** {action.ActionIndex}");
        sb.AppendLine($"- **Blueprint:** {action.BlueprintId}");
        sb.AppendLine($"- **Instance:** {action.InstanceId}");
        sb.AppendLine();

        if (action.Schema.HasValue)
        {
            sb.AppendLine("## Expected Payload Schema");
            sb.AppendLine("```json");
            sb.AppendLine(action.Schema.Value.GetRawText());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        if (action.PreviousPayload.HasValue)
        {
            sb.AppendLine("## Previous Action Payload");
            sb.AppendLine("```json");
            sb.AppendLine(action.PreviousPayload.Value.GetRawText());
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## Instructions");
        sb.AppendLine("Respond with a JSON object that matches the expected payload schema above.");
        sb.AppendLine("Output ONLY the JSON object, no additional text or markdown fences.");

        return sb.ToString();
    }

    /// <summary>
    /// Attempts to parse a payload from the AI response, handling markdown code fences.
    /// </summary>
    public static Dictionary<string, object>? TryParsePayload(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return null;

        // Try extracting from markdown code fence first
        var match = JsonCodeFenceRegex().Match(response);
        var jsonString = match.Success ? match.Groups[1].Value.Trim() : response.Trim();

        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(jsonString);
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            return JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<string> CallClaudeAsync(string message, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = JsonContent.Create(new
            {
                model = _config.Model,
                max_tokens = 2048,
                temperature = _config.Temperature,
                messages = new[]
                {
                    new { role = "user", content = message }
                }
            })
        };

        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(json);

        // Extract text from Claude API response
        var content = doc.RootElement.GetProperty("content");
        foreach (var block in content.EnumerateArray())
        {
            if (block.GetProperty("type").GetString() == "text")
            {
                return block.GetProperty("text").GetString() ?? "";
            }
        }

        return "";
    }

    private async Task<string> LoadPromptAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_config.PromptFile))
            throw new FileNotFoundException($"Persona prompt file not found: {_config.PromptFile}");

        return await File.ReadAllTextAsync(_config.PromptFile, cancellationToken);
    }

    [GeneratedRegex(@"```(?:json)?\s*\n?([\s\S]*?)```", RegexOptions.Multiline)]
    private static partial Regex JsonCodeFenceRegex();
}
