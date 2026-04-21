// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using Sorcha.UI.Core.Models.Chat;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Designer;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ChatMessageModel = Sorcha.UI.Core.Models.Chat.ChatMessage;

namespace Sorcha.UI.Web.Client.Pages.DesignerShell.Panes;

/// <summary>
/// AI chat pane for the designer unified shell. CSS-Grid two-row layout
/// pins the input composer to the bottom; the messages area scrolls
/// internally. Subscribes to <see cref="IChatHubConnection"/> events and
/// writes blueprint updates into the shared <see cref="DesignerContext"/>.
/// </summary>
public partial class AiDesignerPane : ComponentBase, IAsyncDisposable
{
    private readonly List<ChatMessageModel> _messages = [];
    private AutoScrollController? _autoScroll;
    private string _messageInput = string.Empty;
    private string _currentAssistantMessage = string.Empty;
    private bool _isProcessing;
    private bool _connected;
    private string? _sessionId;
    private bool _initialised;
    private DotNetObjectReference<AiDesignerPane>? _testHookRef;

    private const string MessagesElementId = "ai-pane-messages";

    private bool IsConnected => _connected && ChatHub.State == ChatConnectionState.Connected;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        _autoScroll = new AutoScrollController(JS);

        ChatHub.OnChunkReceived += HandleChunkReceived;
        ChatHub.OnToolExecuted += HandleToolExecuted;
        ChatHub.OnBlueprintUpdated += HandleBlueprintUpdated;
        ChatHub.OnMessageComplete += HandleMessageComplete;
        ChatHub.OnSessionError += HandleSessionError;
        ChatHub.OnMessageLimitWarning += HandleMessageLimitWarning;
        ChatHub.OnStateChanged += HandleStateChanged;
        ChatHub.OnSessionStarted += HandleSessionStarted;

        try
        {
            await ChatHub.ConnectAsync();
            _sessionId = await ChatHub.StartSessionAsync(Context.Blueprint?.Id);
            Context.ChatSessionId = _sessionId;
            _connected = ChatHub.State == ChatConnectionState.Connected;
            _initialised = true;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to connect: {ex.Message}", Severity.Error);
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Register auto-scroll helper expected by AutoScrollController.
            // Data is always passed as arguments — never interpolated into JS.
            try
            {
                await JS.InvokeVoidAsync("eval",
                    "window.sorcha = window.sorcha || {};" +
                    "window.sorcha.designer = window.sorcha.designer || {};" +
                    "window.sorcha.designer.scrollToBottom = window.sorcha.designer.scrollToBottom || function(id) {" +
                    "var el = document.getElementById(id); if (el) { el.scrollTop = el.scrollHeight; } };");
            }
            catch
            {
                // Circuit tearing down — nothing to register.
            }

#if DEBUG || E2E_TEST_HOOKS
            _testHookRef = DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("eval",
                    "window.sorcha = window.sorcha || {};" +
                    "window.sorcha.designer = window.sorcha.designer || {};" +
                    "window.sorcha.designer.aiPaneRef = arguments[0];",
                    _testHookRef);
            }
            catch
            {
                // Ignore — test hook registration is best-effort.
            }
#endif
        }

        if (_autoScroll is not null && _messages.Count > 0)
        {
            await _autoScroll.OnContentAppendedAsync(MessagesElementId);
        }
    }

    private void HandleChunkReceived(string chunk)
    {
        InvokeAsync(() =>
        {
            _currentAssistantMessage += chunk;

            var last = _messages.LastOrDefault();
            if (last?.Role == MessageRole.Assistant && last.IsStreaming)
            {
                last.Content = _currentAssistantMessage;
            }
            else
            {
                _messages.Add(new ChatMessageModel
                {
                    Role = MessageRole.Assistant,
                    Content = _currentAssistantMessage,
                    Timestamp = DateTime.UtcNow,
                    IsStreaming = true
                });
            }

            StateHasChanged();
        });
    }

    private void HandleToolExecuted(string toolName, bool success, string? error)
    {
        InvokeAsync(() =>
        {
            var last = _messages.LastOrDefault();
            last?.ToolResults.Add(new ToolExecutionResult
            {
                ToolName = toolName,
                Success = success,
                Error = error
            });
            StateHasChanged();
        });
    }

    private void HandleBlueprintUpdated(BlueprintModel blueprint, Sorcha.UI.Core.Models.Chat.ValidationResult validation)
    {
        InvokeAsync(() =>
        {
            var editedId = TryExtractEditedActionId(Context.Blueprint, blueprint);
            // TODO: the hub payload still does not carry an explicit edited-action
            // id — we fall back to a JSON-diff heuristic (see TryExtractEditedActionId).
            // When the hub event grows an explicit field, drop the heuristic.
            Context.ApplyAiUpdate(blueprint, validation, editedActionId: editedId);
            StateHasChanged();
        });
    }

    /// <summary>
    /// Best-effort detection of which action the AI most recently edited. Compares
    /// the incoming blueprint to the prior snapshot by serialising each matched-ID
    /// pair to JSON and returning the first ID whose serialised form differs. If
    /// no prior blueprint exists, no differences are found, or the incoming
    /// blueprint has no actions, returns <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Heuristic — the hub event does not yet carry the edited-action id directly.
    /// Good enough for the auto-cursor UX: a single tool call typically mutates one
    /// action, and if it mutates several we still land on the first-by-id changed
    /// action which is at worst a near-miss. Replace once the hub grows an explicit
    /// <c>editedActionId</c> field.
    /// </remarks>
    private static string? TryExtractEditedActionId(BlueprintModel? previous, BlueprintModel current)
    {
        if (current?.Actions is null || current.Actions.Count == 0)
        {
            return null;
        }
        if (previous?.Actions is null || previous.Actions.Count == 0)
        {
            // No baseline to diff against — caller's IsManualCursor state decides
            // whether ActiveActionId moves; returning null keeps the current cursor.
            return null;
        }

        var previousById = previous.Actions.ToDictionary(a => a.Id, a => a);
        foreach (var candidate in current.Actions)
        {
            if (!previousById.TryGetValue(candidate.Id, out var before))
            {
                // New action — almost certainly what the AI just added.
                return candidate.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            try
            {
                var beforeJson = System.Text.Json.JsonSerializer.Serialize(before);
                var afterJson = System.Text.Json.JsonSerializer.Serialize(candidate);
                if (!string.Equals(beforeJson, afterJson, StringComparison.Ordinal))
                {
                    return candidate.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch
            {
                // Serialisation failure → skip this pair. Worst case we fall through
                // and return null, keeping the current cursor.
            }
        }
        return null;
    }

    private void HandleMessageComplete(string messageId)
    {
        InvokeAsync(() =>
        {
            _currentAssistantMessage = string.Empty;
            _isProcessing = false;
            var last = _messages.LastOrDefault();
            if (last?.Role == MessageRole.Assistant)
            {
                last.IsStreaming = false;
            }
            StateHasChanged();
        });
    }

    private void HandleSessionError(string code, string message)
    {
        InvokeAsync(() =>
        {
            Snackbar.Add($"Error [{code}]: {message}", Severity.Error);
            _isProcessing = false;
            StateHasChanged();
        });
    }

    private void HandleMessageLimitWarning(int remaining)
    {
        InvokeAsync(() =>
        {
            if (remaining <= 10)
            {
                Snackbar.Add($"Warning: Only {remaining} messages remaining in this session", Severity.Warning);
            }
            StateHasChanged();
        });
    }

    private void HandleStateChanged(ChatConnectionState state)
    {
        InvokeAsync(() =>
        {
            _connected = state == ChatConnectionState.Connected;
            StateHasChanged();
        });
    }

    private void HandleSessionStarted(string sessionId, BlueprintModel? blueprint, int messageCount)
    {
        InvokeAsync(() =>
        {
            _sessionId = sessionId;
            Context.ChatSessionId = sessionId;

            if (blueprint is not null)
            {
                Context.SetBlueprint(blueprint);
                _messages.Add(new ChatMessageModel
                {
                    Role = MessageRole.Assistant,
                    Content = $"Loaded blueprint **\"{blueprint.Title}\"** for editing. " +
                              $"{blueprint.Participants.Count} participants, {blueprint.Actions.Count} actions. " +
                              $"What changes would you like to make?",
                    Timestamp = DateTime.UtcNow
                });
            }
            else if (_messages.Count == 0)
            {
                _messages.Add(new ChatMessageModel
                {
                    Role = MessageRole.Assistant,
                    Content = "Hello! I'm your AI blueprint designer. Describe the workflow you want to create " +
                              "and I'll help you build it step by step.",
                    Timestamp = DateTime.UtcNow
                });
            }

            StateHasChanged();
        });
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey && !string.IsNullOrWhiteSpace(_messageInput))
        {
            await SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(_messageInput) || string.IsNullOrEmpty(_sessionId))
        {
            return;
        }

        var message = _messageInput;
        _messageInput = string.Empty;

        _messages.Add(new ChatMessageModel
        {
            Role = MessageRole.User,
            Content = message,
            Timestamp = DateTime.UtcNow
        });
        _isProcessing = true;
        StateHasChanged();

        try
        {
            await ChatHub.SendMessageAsync(_sessionId, message);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to send message: {ex.Message}", Severity.Error);
            _isProcessing = false;
        }
    }

    private async Task CancelGenerationAsync()
    {
        if (string.IsNullOrEmpty(_sessionId))
        {
            return;
        }
        try
        {
            await ChatHub.CancelGenerationAsync(_sessionId);
            _isProcessing = false;
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Failed to cancel: {ex.Message}", Severity.Error);
        }
    }

#if DEBUG || E2E_TEST_HOOKS
    /// <summary>
    /// Test-only hook. Playwright invokes this via the globally registered
    /// <c>DotNetObjectReference</c> to simulate a <c>BlueprintUpdated</c> hub
    /// event without a real SignalR round-trip. Closes GAP-011b.
    /// </summary>
    [JSInvokable]
    public void TestInject_SimulateBlueprintUpdated(string blueprintJson, string? editedActionId)
    {
        try
        {
            var bp = System.Text.Json.JsonSerializer.Deserialize<BlueprintModel>(blueprintJson);
            if (bp is null)
            {
                return;
            }
            InvokeAsync(() =>
            {
                Context.ApplyAiUpdate(bp, val: null, editedActionId);
                StateHasChanged();
            });
        }
        catch
        {
            // Swallow — test hook must not crash the circuit.
        }
    }

    /// <summary>
    /// Test-only hook: append a synthetic assistant message without a hub round-trip.
    /// Used by E2E tests that need many messages to verify input pinning.
    /// </summary>
    [JSInvokable]
    public void TestInject_AppendMessage(string role, string content)
    {
        InvokeAsync(() =>
        {
            _messages.Add(new ChatMessageModel
            {
                Role = string.Equals(role, "user", StringComparison.OrdinalIgnoreCase)
                    ? MessageRole.User
                    : MessageRole.Assistant,
                Content = content,
                Timestamp = DateTime.UtcNow
            });
            StateHasChanged();
        });
    }
#endif

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        ChatHub.OnChunkReceived -= HandleChunkReceived;
        ChatHub.OnToolExecuted -= HandleToolExecuted;
        ChatHub.OnBlueprintUpdated -= HandleBlueprintUpdated;
        ChatHub.OnMessageComplete -= HandleMessageComplete;
        ChatHub.OnSessionError -= HandleSessionError;
        ChatHub.OnMessageLimitWarning -= HandleMessageLimitWarning;
        ChatHub.OnStateChanged -= HandleStateChanged;
        ChatHub.OnSessionStarted -= HandleSessionStarted;

        if (_initialised && !string.IsNullOrEmpty(_sessionId))
        {
            try
            {
                await ChatHub.EndSessionAsync(_sessionId);
            }
            catch
            {
                // Ignore cleanup errors.
            }
        }

        try
        {
            await ChatHub.DisconnectAsync();
        }
        catch
        {
            // Ignore cleanup errors.
        }

        _testHookRef?.Dispose();
    }
}
