// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;
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
    /// the citizen's own wallet address as the open-participant sender). Returns a discriminated
    /// <see cref="ApplicationFormLoadResult"/> — P0 fix (<c>fix/pwa-p0-claim-and-camera</c>): a
    /// permission failure (403) and a genuine connectivity failure used to collapse into the same
    /// <c>null</c>, which the page then always reported as "offline" even when it wasn't. Callers
    /// MUST branch on <see cref="ApplicationFormLoadResult.Status"/> rather than treating every
    /// non-<see cref="ApplicationFormLoadStatus.Loaded"/> result as an offline signal.
    /// </summary>
    Task<ApplicationFormLoadResult> LoadFormAsync(Guid instanceId, CancellationToken ct = default);

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
/// Why <see cref="IApplicationActionClient.LoadFormAsync"/> did or didn't return a usable
/// <see cref="ApplicationFormContext"/>. Distinguishes "the server said no" (<see cref="Forbidden"/>,
/// <see cref="NotFound"/>) from "we couldn't reach the server" (<see cref="NetworkError"/>) — the two
/// have very different honest messages, and only the latter is ever an "offline" signal.
/// </summary>
public enum ApplicationFormLoadStatus
{
    /// <summary>The form context was resolved successfully.</summary>
    Loaded,

    /// <summary>The instance, blueprint, or action could not be found.</summary>
    NotFound,

    /// <summary>The server refused the request (401/403) — a real answer, not a connectivity gap.</summary>
    Forbidden,

    /// <summary>The request could not be completed — timeout, DNS failure, 5xx, or similar.</summary>
    NetworkError,
}

/// <summary>Discriminated result of <see cref="IApplicationActionClient.LoadFormAsync"/>.</summary>
/// <param name="Status">Which outcome occurred.</param>
/// <param name="Context">The loaded context; only non-null when <paramref name="Status"/> is <see cref="ApplicationFormLoadStatus.Loaded"/>.</param>
public sealed record ApplicationFormLoadResult(ApplicationFormLoadStatus Status, ApplicationFormContext? Context)
{
    /// <summary>Builds a successful result.</summary>
    public static ApplicationFormLoadResult Success(ApplicationFormContext context) =>
        new(ApplicationFormLoadStatus.Loaded, context);

    /// <summary>The instance, blueprint, or action could not be found.</summary>
    public static readonly ApplicationFormLoadResult NotFound = new(ApplicationFormLoadStatus.NotFound, null);

    /// <summary>The server refused the request.</summary>
    public static readonly ApplicationFormLoadResult Forbidden = new(ApplicationFormLoadStatus.Forbidden, null);

    /// <summary>The request could not be completed.</summary>
    public static readonly ApplicationFormLoadResult NetworkError = new(ApplicationFormLoadStatus.NetworkError, null);
}

/// <summary>
/// Default <see cref="IApplicationActionClient"/> — talks to the Blueprint Service through the
/// gateway-routed, bearer-authed PWA HttpClient: <c>GET /api/instances/{id}</c>,
/// <c>GET /api/instances/{id}/actions/{actionId}</c>, and
/// <c>POST /api/instances/{id}/actions/{actionId}/execute</c>.
/// </summary>
/// <remarks>
/// P0 fix (<c>fix/pwa-p0-claim-and-camera</c>): this client previously read the action schema from
/// <c>GET /api/blueprints/{id}</c> — the authoring endpoint Feature 147 deliberately locked to
/// service/platform-tier callers. A consumer-tier citizen token always 403'd there, and the caller
/// folded that into a bare <c>null</c>, which <c>ApplicationInstance.razor</c> then always reported as
/// "offline" — even though the citizen was online and the server had genuinely refused the read. This
/// client now reads the instance-scoped, consumer-readable
/// <c>GET /api/instances/{id}/actions/{actionId}</c> endpoint instead, and returns a discriminated
/// <see cref="ApplicationFormLoadResult"/> so a permission failure is never mistaken for a connectivity one.
/// </remarks>
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
    public async Task<ApplicationFormLoadResult> LoadFormAsync(Guid instanceId, CancellationToken ct = default)
    {
        try
        {
            var instanceResponse = await _http.GetAsync($"api/instances/{instanceId:N}", ct);
            if (!instanceResponse.IsSuccessStatusCode)
            {
                return ClassifyFailure(instanceId, "instance", instanceResponse.StatusCode);
            }

            var instance = await instanceResponse.Content.ReadFromJsonAsync<InstanceDto>(JsonDefaults.Api, ct);
            if (instance is null || string.IsNullOrEmpty(instance.BlueprintId))
            {
                return ApplicationFormLoadResult.NotFound;
            }

            var actionId = instance.CurrentActionIds is { Count: > 0 } ? instance.CurrentActionIds[0] : 1;

            // Instance-scoped, consumer-readable read — NOT GET /api/blueprints/{id} (authoring-only,
            // Feature 147). See the class remarks for why.
            var actionResponse = await _http.GetAsync(
                $"api/instances/{instanceId:N}/actions/{actionId}", ct);
            if (!actionResponse.IsSuccessStatusCode)
            {
                return ClassifyFailure(instanceId, "action", actionResponse.StatusCode);
            }

            var actionJson = await actionResponse.Content.ReadAsStringAsync(ct);
            var action = ParseActionSchema(actionJson);
            if (action is null)
            {
                _logger.LogWarning(
                    "Action schema for instance {InstanceId} action {ActionId} could not be parsed",
                    instanceId, actionId);
                return ApplicationFormLoadResult.NotFound;
            }

            // Open-participant sender: the citizen's own wallet address. Resolved from the
            // holder-keys endpoint (the same call HolderKeyRenderer makes) so we never assume the
            // wallet is client-side.
            var keys = await _holderKeys.GetHolderKeysAsync(ct);
            if (keys is null || string.IsNullOrEmpty(keys.WalletAddress))
            {
                _logger.LogWarning("Could not resolve the citizen's wallet address for submission.");
                return ApplicationFormLoadResult.NetworkError;
            }

            var context = new ApplicationFormContext(
                InstanceId: instanceId,
                Action: action,
                BlueprintId: instance.BlueprintId,
                RegisterId: instance.RegisterId ?? string.Empty,
                SenderWallet: keys.WalletAddress,
                ActionId: actionId,
                Title: string.IsNullOrWhiteSpace(instance.Title) ? "Application" : instance.Title!);
            return ApplicationFormLoadResult.Success(context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to load form context for instance {InstanceId}", instanceId);
            return ex.StatusCode is { } code
                ? ClassifyFailure(instanceId, "request", code)
                : ApplicationFormLoadResult.NetworkError;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load form context for instance {InstanceId}", instanceId);
            return ApplicationFormLoadResult.NetworkError;
        }
    }

    /// <summary>
    /// Maps an unsuccessful HTTP status to the discriminated <see cref="ApplicationFormLoadResult"/>
    /// status. 401/403 are a real server answer (Forbidden) — never a connectivity signal, which is
    /// the whole point of the P0 fix. 404 is NotFound. Everything else (5xx, etc.) is treated as a
    /// network-shaped failure so the page's offline-vs-online branch (driven by
    /// <c>IConnectivity.IsOnline</c>) decides how to present it.
    /// </summary>
    private ApplicationFormLoadResult ClassifyFailure(Guid instanceId, string what, HttpStatusCode statusCode)
    {
        _logger.LogWarning(
            "Loading {What} for instance {InstanceId} failed with {StatusCode}", what, instanceId, statusCode);
        return statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ApplicationFormLoadResult.Forbidden,
            HttpStatusCode.NotFound => ApplicationFormLoadResult.NotFound,
            _ => ApplicationFormLoadResult.NetworkError,
        };
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

    /// <summary>
    /// Maps the narrow <c>InstanceActionSchemaResponse</c> wire shape (server:
    /// <c>Sorcha.Blueprint.Service.Models.Responses.InstanceActionSchemaResponse</c>) onto a
    /// <see cref="Sorcha.Blueprint.Models.Action"/> instance for <c>SorchaFormRenderer</c>. Only the
    /// fields the renderer actually reads are populated — routing/other-participant fields
    /// (<c>Routes</c>, <c>Condition</c>, <c>Participants</c>, ...) are never present on the wire and so
    /// stay at the model's own defaults.
    /// </summary>
    private static Sorcha.Blueprint.Models.Action? ParseActionSchema(string actionJson)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<ActionSchemaDto>(actionJson, JsonOptions);
            if (dto is null)
            {
                return null;
            }

            return new Sorcha.Blueprint.Models.Action
            {
                Id = dto.ActionId,
                Title = dto.Title ?? string.Empty,
                Form = dto.Form,
                DataSchemas = dto.DataSchemas,
                Calculations = dto.Calculations,
                CredentialRequirements = dto.CredentialRequirements,
                CredentialIssuanceConfig = dto.CredentialIssuanceConfig,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class InstanceDto
    {
        public string? BlueprintId { get; set; }
        public string? RegisterId { get; set; }
        public List<int>? CurrentActionIds { get; set; }
        public string? Title { get; set; }
    }

    /// <summary>Wire shape of <c>GET /api/instances/{id}/actions/{actionId}</c> (server-side: <c>InstanceActionSchemaResponse</c>).</summary>
    private sealed class ActionSchemaDto
    {
        public int ActionId { get; set; }
        public string? Title { get; set; }
        public Sorcha.Blueprint.Models.Control? Form { get; set; }
        public List<JsonDocument>? DataSchemas { get; set; }
        public Dictionary<string, System.Text.Json.Nodes.JsonNode>? Calculations { get; set; }
        public List<Sorcha.Blueprint.Models.Credentials.CredentialRequirement>? CredentialRequirements { get; set; }
        public Sorcha.Blueprint.Models.Credentials.CredentialIssuanceConfig? CredentialIssuanceConfig { get; set; }
    }

    private sealed record ActionExecuteBody(
        string BlueprintId,
        string ActionId,
        string InstanceId,
        string SenderWallet,
        string RegisterAddress,
        Dictionary<string, object> PayloadData);
}
