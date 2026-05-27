// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Services.Forms;
using Sorcha.UI.Core.Services.HolderKeys;

namespace Sorcha.Wallet.Pwa.Services.Applications;

/// <summary>
/// Feature 137 (cross-node submission, C3) — loads a blueprint instance's current action so the PWA
/// can render <c>SorchaFormRenderer</c>, and submits the citizen's completed form to the Blueprint
/// Service action-execution endpoint. The citizen's Sorcha wallet is server-custodied (Feature 114),
/// so the action is signed server-side from the bearer token — this client carries no private key.
/// </summary>
public interface IApplicationActionClient
{
    /// <summary>
    /// Resolves the instance's current action + submission context (blueprint id, register id, and
    /// the citizen's own wallet address as the open-participant sender). Returns <c>null</c> when the
    /// instance, blueprint, current action, or the citizen's wallet cannot be resolved.
    /// </summary>
    Task<ApplicationFormContext?> LoadFormAsync(Guid instanceId, CancellationToken ct = default);

    /// <summary>
    /// Nests the flat JSON-Pointer-keyed form data and POSTs it to
    /// <c>/api/instances/{id}/actions/{actionId}/execute</c>.
    /// </summary>
    Task<ApplicationSubmissionResult> SubmitAsync(
        ApplicationFormContext context,
        IReadOnlyDictionary<string, object?> formData,
        CancellationToken ct = default);
}

/// <summary>Context needed to render and submit a blueprint instance's current action.</summary>
/// <param name="InstanceId">The blueprint instance id.</param>
/// <param name="Action">The current action (schema + layout) to render.</param>
/// <param name="BlueprintId">The instance's blueprint id.</param>
/// <param name="RegisterId">The register the action transaction is submitted to.</param>
/// <param name="SenderWallet">The citizen's own wallet address (open-participant sender).</param>
/// <param name="ActionId">The current action id.</param>
/// <param name="Title">A user-facing title for the application.</param>
public sealed record ApplicationFormContext(
    Guid InstanceId,
    Sorcha.Blueprint.Models.Action Action,
    string BlueprintId,
    string RegisterId,
    string SenderWallet,
    int ActionId,
    string Title);

/// <summary>
/// Default <see cref="IApplicationActionClient"/> — talks to the Blueprint Service through the
/// gateway-routed, bearer-authed PWA HttpClient. Mirrors the walkthrough's
/// <c>Invoke-SorchaAction</c> contract: <c>GET /api/instances/{id}</c>,
/// <c>GET /api/blueprints/{id}</c>, and <c>POST /api/instances/{id}/actions/{actionId}/execute</c>.
/// </summary>
public sealed class HttpApplicationActionClient : IApplicationActionClient
{
    private readonly HttpClient _http;
    private readonly IHolderKeyClient _holderKeys;
    private readonly ILogger<HttpApplicationActionClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public HttpApplicationActionClient(
        HttpClient http,
        IHolderKeyClient holderKeys,
        ILogger<HttpApplicationActionClient> logger)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _holderKeys = holderKeys ?? throw new ArgumentNullException(nameof(holderKeys));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ApplicationFormContext?> LoadFormAsync(Guid instanceId, CancellationToken ct = default)
    {
        try
        {
            var instance = await _http.GetFromJsonAsync<InstanceDto>(
                $"api/instances/{instanceId:N}", JsonOptions, ct);
            if (instance is null || string.IsNullOrEmpty(instance.BlueprintId))
            {
                return null;
            }

            var actionId = instance.CurrentActionIds is { Count: > 0 } ? instance.CurrentActionIds[0] : 1;

            var blueprintJson = await _http.GetStringAsync(
                $"api/blueprints/{Uri.EscapeDataString(instance.BlueprintId)}", ct);
            var action = ExtractAction(blueprintJson, actionId);
            if (action is null)
            {
                _logger.LogWarning(
                    "Blueprint {BlueprintId} has no action {ActionId} to render for instance {InstanceId}",
                    instance.BlueprintId, actionId, instanceId);
                return null;
            }

            // Open-participant sender: the citizen's own wallet address. Resolved from the
            // holder-keys endpoint (the same call HolderKeyRenderer makes) so we never assume the
            // wallet is client-side.
            var keys = await _holderKeys.GetHolderKeysAsync(ct);
            if (keys is null || string.IsNullOrEmpty(keys.WalletAddress))
            {
                _logger.LogWarning("Could not resolve the citizen's wallet address for submission.");
                return null;
            }

            return new ApplicationFormContext(
                InstanceId: instanceId,
                Action: action,
                BlueprintId: instance.BlueprintId,
                RegisterId: instance.RegisterId ?? string.Empty,
                SenderWallet: keys.WalletAddress,
                ActionId: actionId,
                Title: string.IsNullOrWhiteSpace(instance.Title) ? "Application" : instance.Title!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load form context for instance {InstanceId}", instanceId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<ApplicationSubmissionResult> SubmitAsync(
        ApplicationFormContext context,
        IReadOnlyDictionary<string, object?> formData,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(formData);

        var payloadData = FormPayloadBuilder.BuildNested(formData);
        var execBody = new ActionExecuteBody(
            BlueprintId: context.BlueprintId,
            ActionId: context.ActionId.ToString(),
            InstanceId: context.InstanceId.ToString("N"),
            SenderWallet: context.SenderWallet,
            RegisterAddress: context.RegisterId,
            PayloadData: payloadData);

        try
        {
            var response = await _http.PostAsJsonAsync(
                $"api/instances/{context.InstanceId:N}/actions/{context.ActionId}/execute",
                execBody, JsonOptions, ct);

            if (response.IsSuccessStatusCode)
            {
                return new ApplicationSubmissionResult(
                    ApplicationSubmissionStatus.Success, context.InstanceId, ErrorCode: null, ErrorDetail: null);
            }

            var detail = await SafeReadAsync(response, ct);
            var status = (int)response.StatusCode is >= 400 and < 500
                ? ApplicationSubmissionStatus.ValidationFailed
                : ApplicationSubmissionStatus.ServerError;
            return new ApplicationSubmissionResult(
                status, InstanceId: null, ErrorCode: $"HTTP_{(int)response.StatusCode}", ErrorDetail: detail);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Action submission failed for instance {InstanceId}", context.InstanceId);
            return new ApplicationSubmissionResult(
                ApplicationSubmissionStatus.ServerError, InstanceId: null, ErrorCode: "ERR_APPSUBMIT_NETWORK",
                ErrorDetail: "Couldn't reach the server to submit your application. Try again.");
        }
    }

    private static async Task<string?> SafeReadAsync(HttpResponseMessage r, CancellationToken ct)
    {
        try { return await r.Content.ReadAsStringAsync(ct); } catch { return null; }
    }

    private static Sorcha.Blueprint.Models.Action? ExtractAction(string blueprintJson, int actionId)
    {
        using var doc = JsonDocument.Parse(blueprintJson);
        var root = doc.RootElement;

        // Served blueprints may be the flat Blueprint model ({ actions: [...] }) or wrap the
        // authored template ({ template: { actions: [...] } }). Tolerate both.
        var found = root.TryGetProperty("actions", out var actionsEl)
            || (root.TryGetProperty("template", out var tmpl) && tmpl.TryGetProperty("actions", out actionsEl));
        if (!found || actionsEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var el in actionsEl.EnumerateArray())
        {
            if (el.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id) && id == actionId)
            {
                return el.Deserialize<Sorcha.Blueprint.Models.Action>(JsonOptions);
            }
        }
        return null;
    }

    private sealed class InstanceDto
    {
        public string? BlueprintId { get; set; }
        public string? RegisterId { get; set; }
        public List<int>? CurrentActionIds { get; set; }
        public string? Title { get; set; }
    }

    private sealed record ActionExecuteBody(
        string BlueprintId,
        string ActionId,
        string InstanceId,
        string SenderWallet,
        string RegisterAddress,
        Dictionary<string, object> PayloadData);
}
