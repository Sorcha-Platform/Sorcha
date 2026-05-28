// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using MudBlazor;
using Sorcha.UI.Core.Models.Chat;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Designer;
using Sorcha.UI.Core.Services.Feedback;
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
    private readonly List<ChatAttachment> _pendingAttachments = [];
    private AutoScrollController? _autoScroll;
    private string _messageInput = string.Empty;
    private string _currentAssistantMessage = string.Empty;
    private bool _isProcessing;
    private bool _connected;
    private bool _isDragging;
    private string? _sessionId;
    private bool _initialised;
    private DotNetObjectReference<AiDesignerPane>? _testHookRef;
    private DotNetObjectReference<AiDesignerPane>? _dropZoneRef;

    private const string MessagesElementId = "ai-pane-messages";
    private const string PaneRootElementId = "ai-pane-root";
    private const string FileInputElementId = "ai-pane-file-input";
    private const int MaxAttachmentsPerMessage = 5;

    private bool IsConnected => _connected && ChatHub.State == ChatConnectionState.Connected;

    /// <summary>
    /// Feature 142 US4 (T044 / FR-010) — a directed-build starter offered as a chip on a fresh
    /// designer load. The <see cref="Id"/> matches <c>DirectedBuildStarter.KnownStarterIds</c>
    /// in the Blueprint service so the orchestration can short-circuit to a deterministic seed.
    /// </summary>
    /// <param name="Id">Stable starter id (e.g. <c>grant</c>).</param>
    /// <param name="Label">Plain-language chip label shown to the user.</param>
    /// <param name="UserMessage">
    /// The plain-language user message the chip click enqueues into the chat — the orchestration
    /// recognises these as directed-build openers (see
    /// <c>ChatOrchestrationService.TryResolveDirectedStarter</c>). Plain English keeps the chat
    /// history readable; the orchestration matches on a prefix allowlist for determinism.
    /// </param>
    public sealed record DirectedBuildStarterOption(string Id, string Label, string UserMessage);

    /// <summary>The three directed-build starters surfaced by the chip row.</summary>
    private static readonly IReadOnlyList<DirectedBuildStarterOption> DirectedBuildStarters =
    [
        new("grant", "Apply for a grant",
            "Help me build a grant application"),
        new("permit", "Apply for a permit / licence",
            "Help me build a permit / licence application"),
        new("certify-then-apply", "Certify, then apply",
            "Help me build a certify-then-apply workflow"),
    ];

    /// <summary>Tracks whether the user has chosen a directed-build starter this session.</summary>
    private bool _directedStarterClicked;

    /// <summary>
    /// The chip row is visible only on the very first turn (the opening assistant greeting may be
    /// in the list — count ≤ 1), with no blueprint loaded, and only until the user picks a chip.
    /// Once a free-form user message has been sent (<c>_messages.Count &gt; 1</c>) or a blueprint
    /// has been applied via the chip path, the chips disappear.
    /// </summary>
    private bool ShouldShowDirectedBuildChips =>
        !_directedStarterClicked
        && Context.Blueprint is null
        && _messages.Count <= 1
        && IsConnected
        && !_isProcessing;

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
            Feedback.ShowError($"Failed to connect: {ex.Message}", autoDismissMs: 0);
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // Register auto-scroll + drop-zone helpers expected by the JS layer.
            // Data is always passed as arguments — never interpolated into JS.
            try
            {
                await JS.InvokeVoidAsync("eval",
                    "window.sorcha = window.sorcha || {};" +
                    "window.sorcha.designer = window.sorcha.designer || {};" +
                    "window.sorcha.designer.scrollToBottom = window.sorcha.designer.scrollToBottom || function(id) {" +
                    "var el = document.getElementById(id); if (el) { el.scrollTop = el.scrollHeight; } };" +

                    // Programmatic file picker open (paperclip button).
                    "window.sorcha.designer.openFilePicker = window.sorcha.designer.openFilePicker || function(id) {" +
                    "var el = document.getElementById(id); if (el) { el.value = ''; el.click(); } };" +

                    // Chunked ArrayBuffer → base64 (avoids call-stack overflow on large files).
                    "window.sorcha.designer._toBase64 = window.sorcha.designer._toBase64 || function(buf) {" +
                    "var bytes = new Uint8Array(buf), binary = '', chunk = 0x8000;" +
                    "for (var i = 0; i < bytes.length; i += chunk) {" +
                    "binary += String.fromCharCode.apply(null, bytes.subarray(i, Math.min(i + chunk, bytes.length))); }" +
                    "return btoa(binary); };" +

                    // Whole-pane drop zone with depth-counted enter/leave (children fire spurious leaves).
                    "window.sorcha.designer.attachDropZone = window.sorcha.designer.attachDropZone || function(id, dotNetRef) {" +
                    "var el = document.getElementById(id); if (!el) return;" +
                    "if (el.__sorchaDrop) return; el.__sorchaDrop = true;" +
                    "var depth = 0;" +
                    "el.addEventListener('dragenter', function(e) {" +
                    "if (!e.dataTransfer || !Array.from(e.dataTransfer.types || []).includes('Files')) return;" +
                    "e.preventDefault(); depth++;" +
                    "if (depth === 1) dotNetRef.invokeMethodAsync('OnDragStart'); });" +
                    "el.addEventListener('dragover', function(e) {" +
                    "if (!e.dataTransfer || !Array.from(e.dataTransfer.types || []).includes('Files')) return;" +
                    "e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; });" +
                    "el.addEventListener('dragleave', function(e) {" +
                    "if (!e.dataTransfer || !Array.from(e.dataTransfer.types || []).includes('Files')) return;" +
                    "depth = Math.max(0, depth - 1);" +
                    "if (depth === 0) dotNetRef.invokeMethodAsync('OnDragEnd'); });" +
                    "el.addEventListener('drop', async function(e) {" +
                    "e.preventDefault(); depth = 0;" +
                    "dotNetRef.invokeMethodAsync('OnDragEnd');" +
                    "if (!e.dataTransfer || !e.dataTransfer.files) return;" +
                    "var out = [];" +
                    "for (var i = 0; i < e.dataTransfer.files.length; i++) {" +
                    "var f = e.dataTransfer.files[i];" +
                    "var buf = await f.arrayBuffer();" +
                    "out.push({ fileName: f.name, mediaType: f.type || 'application/octet-stream'," +
                    "base64Data: window.sorcha.designer._toBase64(buf) }); }" +
                    "await dotNetRef.invokeMethodAsync('OnFilesDropped', JSON.stringify(out)); }); };");

                _dropZoneRef = DotNetObjectReference.Create(this);
                await JS.InvokeVoidAsync("window.sorcha.designer.attachDropZone", PaneRootElementId, _dropZoneRef);
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
            Feedback.ShowError($"Error [{code}]: {message}", autoDismissMs: 0);
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
                Feedback.ShowWarning($"Warning: Only {remaining} messages remaining in this session");
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
                              "and I'll help you build it step by step. Drop images or PDFs onto this pane to share " +
                              "reference material with me.",
                    Timestamp = DateTime.UtcNow
                });
            }

            StateHasChanged();
        });
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey
            && (!string.IsNullOrWhiteSpace(_messageInput) || _pendingAttachments.Count > 0))
        {
            await SendMessageAsync();
        }
    }

    /// <summary>
    /// Feature 142 US4 (T044) — handles a directed-build chip click by hiding the chip row and
    /// dispatching the chip's plain-language user message into the chat. The Blueprint service
    /// orchestration recognises the message via <c>TryResolveDirectedStarter</c> and seeds the
    /// blueprint deterministically without invoking the AI, so the journey appears live in the
    /// canvas (FR-011) and the AI takes over from the next user turn.
    /// </summary>
    private async Task SendDirectedStarterAsync(DirectedBuildStarterOption starter)
    {
        if (!IsConnected || _isProcessing || string.IsNullOrEmpty(_sessionId))
        {
            return;
        }

        _directedStarterClicked = true;

        _messages.Add(new ChatMessageModel
        {
            Role = MessageRole.User,
            Content = starter.UserMessage,
            Timestamp = DateTime.UtcNow,
        });
        _isProcessing = true;
        StateHasChanged();

        try
        {
            await ChatHub.SendMessageAsync(_sessionId, starter.UserMessage, attachments: null);
        }
        catch (Exception ex)
        {
            Feedback.ShowError($"Failed to start directed build: {ex.Message}", autoDismissMs: 0);
            _isProcessing = false;
            StateHasChanged();
        }
    }

    private async Task SendMessageAsync()
    {
        var hasText = !string.IsNullOrWhiteSpace(_messageInput);
        var hasFiles = _pendingAttachments.Count > 0;
        if ((!hasText && !hasFiles) || string.IsNullOrEmpty(_sessionId))
        {
            return;
        }

        var message = _messageInput;
        var attachmentsToSend = _pendingAttachments.ToList();
        _messageInput = string.Empty;
        _pendingAttachments.Clear();

        _messages.Add(new ChatMessageModel
        {
            Role = MessageRole.User,
            Content = message,
            Attachments = attachmentsToSend.ToList(),
            Timestamp = DateTime.UtcNow
        });
        _isProcessing = true;
        StateHasChanged();

        try
        {
            await ChatHub.SendMessageAsync(_sessionId, message, attachmentsToSend);
        }
        catch (Exception ex)
        {
            Feedback.ShowError($"Failed to send message: {ex.Message}", autoDismissMs: 0);
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
            Feedback.ShowError($"Failed to cancel: {ex.Message}", autoDismissMs: 0);
        }
    }

    private async Task OpenFilePicker()
    {
        try
        {
            await JS.InvokeVoidAsync("window.sorcha.designer.openFilePicker", FileInputElementId);
        }
        catch
        {
            // Ignore — JS layer will reattach on next render.
        }
    }

    private async Task OnInputFilesChanged(InputFileChangeEventArgs e)
    {
        // Click-to-pick path mirrors the drag-drop path: read each file, validate,
        // base64-encode, and feed the same _pendingAttachments list.
        foreach (var file in e.GetMultipleFiles(MaxAttachmentsPerMessage))
        {
            if (_pendingAttachments.Count >= MaxAttachmentsPerMessage)
            {
                Feedback.ShowWarning($"Maximum {MaxAttachmentsPerMessage} attachments per message.");
                break;
            }

            try
            {
                var attachment = await ReadBrowserFileAsync(file);
                if (attachment != null)
                {
                    _pendingAttachments.Add(attachment);
                }
            }
            catch (Exception ex)
            {
                Feedback.ShowError($"Could not attach {file.Name}: {ex.Message}", autoDismissMs: 0);
            }
        }

        StateHasChanged();
    }

    private async Task<ChatAttachment?> ReadBrowserFileAsync(IBrowserFile file)
    {
        var (kind, ok) = ClassifyMediaType(file.ContentType);
        if (!ok)
        {
            Feedback.ShowWarning($"Unsupported file type: {file.ContentType}");
            return null;
        }

        var maxSize = kind == ChatAttachmentKind.Image ? 5 * 1024 * 1024L : 32 * 1024 * 1024L;
        if (file.Size > maxSize)
        {
            var limit = kind == ChatAttachmentKind.Image ? "5 MB" : "32 MB";
            Feedback.ShowWarning($"{file.Name} is too large (max {limit}).");
            return null;
        }

        await using var stream = file.OpenReadStream(maxAllowedSize: maxSize);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var base64 = Convert.ToBase64String(ms.ToArray());

        return new ChatAttachment
        {
            Kind = kind,
            MediaType = file.ContentType,
            Base64Data = base64,
            FileName = file.Name
        };
    }

    private static (ChatAttachmentKind kind, bool ok) ClassifyMediaType(string? mediaType)
    {
        return (mediaType ?? string.Empty).ToLowerInvariant() switch
        {
            "image/jpeg" or "image/png" or "image/webp" or "image/gif" => (ChatAttachmentKind.Image, true),
            "application/pdf" => (ChatAttachmentKind.Pdf, true),
            _ => (ChatAttachmentKind.Image, false)
        };
    }

    private void RemoveAttachment(ChatAttachment att)
    {
        _pendingAttachments.Remove(att);
        StateHasChanged();
    }

    /// <summary>JS callback: drag entered the pane (file payload).</summary>
    [JSInvokable]
    public void OnDragStart()
    {
        InvokeAsync(() =>
        {
            _isDragging = true;
            StateHasChanged();
        });
    }

    /// <summary>JS callback: drag left the pane or drop fired.</summary>
    [JSInvokable]
    public void OnDragEnd()
    {
        InvokeAsync(() =>
        {
            _isDragging = false;
            StateHasChanged();
        });
    }

    /// <summary>
    /// JS callback: files dropped on the pane. JSON payload is an array of
    /// <c>{ fileName, mediaType, base64Data }</c>. We classify, validate size,
    /// and append to <see cref="_pendingAttachments"/>.
    /// </summary>
    [JSInvokable]
    public void OnFilesDropped(string filesJson)
    {
        InvokeAsync(() =>
        {
            try
            {
                using var doc = JsonDocument.Parse(filesJson);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (_pendingAttachments.Count >= MaxAttachmentsPerMessage)
                    {
                        Feedback.ShowWarning($"Maximum {MaxAttachmentsPerMessage} attachments per message.");
                        break;
                    }

                    var fileName = item.TryGetProperty("fileName", out var n) ? n.GetString() : null;
                    var mediaType = item.TryGetProperty("mediaType", out var mt) ? mt.GetString() : null;
                    var data = item.TryGetProperty("base64Data", out var d) ? d.GetString() : null;

                    if (string.IsNullOrEmpty(data) || string.IsNullOrEmpty(mediaType))
                    {
                        continue;
                    }

                    var (kind, ok) = ClassifyMediaType(mediaType);
                    if (!ok)
                    {
                        Feedback.ShowWarning($"Unsupported file type: {mediaType}");
                        continue;
                    }

                    // Server enforces hard limits; reject obvious fails client-side too.
                    var maxBase64 = kind == ChatAttachmentKind.Image ? 7_000_000 : 45_000_000;
                    if (data.Length > maxBase64)
                    {
                        Feedback.ShowWarning($"{fileName ?? "Attachment"} exceeds the size limit.");
                        continue;
                    }

                    _pendingAttachments.Add(new ChatAttachment
                    {
                        Kind = kind,
                        MediaType = mediaType,
                        Base64Data = data,
                        FileName = fileName
                    });
                }
            }
            catch (Exception ex)
            {
                Feedback.ShowError($"Failed to read dropped files: {ex.Message}", autoDismissMs: 0);
            }
            StateHasChanged();
        });
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
        _dropZoneRef?.Dispose();
    }
}
