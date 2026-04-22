// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Auth;

namespace Sorcha.Agent.Persona;

/// <summary>
/// Submits persona-generated actions by posting to
/// <c>/api/instances/{instanceId}/actions/{actionIndex}/execute</c>, matching
/// the contract used by <see cref="Execution.ActionExecutor"/>.
/// </summary>
public sealed class PersonaSubmitter : IPersonaSubmitter
{
    private readonly HttpClient _httpClient;
    private readonly Func<CancellationToken, Task<string>> _tokenProvider;
    private readonly string _walletAddress;
    private readonly string _registerId;
    private readonly ILogger<PersonaSubmitter> _logger;

    public PersonaSubmitter(
        HttpClient httpClient,
        AgentAuthService authService,
        string walletAddress,
        string registerId,
        ILogger<PersonaSubmitter> logger)
        : this(httpClient, authService.GetTokenAsync, walletAddress, registerId, logger) { }

    internal PersonaSubmitter(
        HttpClient httpClient,
        Func<CancellationToken, Task<string>> tokenProvider,
        string walletAddress,
        string registerId,
        ILogger<PersonaSubmitter> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _walletAddress = walletAddress;
        _registerId = registerId;
        _logger = logger;
    }

    public async Task<PersonaSubmissionResult> SubmitAsync(
        PersonaDefinition persona,
        JsonObject resolvedPayload,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var actionIndex = persona.Target.ActionIndex
            ?? throw new InvalidOperationException(
                "PersonaTarget.ActionIndex must be resolved before submission (use PersonaTargetResolver).");

        var endpoint = $"/api/instances/{persona.Target.InstanceId}/actions/{actionIndex}/execute";
        var body = new
        {
            blueprintId = persona.Target.BlueprintId,
            actionId = actionIndex.ToString(),
            instanceId = persona.Target.InstanceId,
            senderWallet = _walletAddress,
            registerAddress = _registerId,
            payloadData = (JsonNode)resolvedPayload
        };

        try
        {
            var token = await _tokenProvider(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("X-Delegation-Token", token);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Persona {PersonaName} submitted action index {ActionIndex} (blueprint={BlueprintId})",
                    persona.Name, actionIndex, persona.Target.BlueprintId);
                return new PersonaSubmissionResult(PersonaSubmissionOutcome.Submitted, sw.ElapsedMilliseconds);
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var outcome = IsTransient(response.StatusCode)
                ? PersonaSubmissionOutcome.TransientFailure
                : PersonaSubmissionOutcome.HardFailure;
            _logger.LogWarning(
                "Persona {PersonaName} submission failed: {Status} {Body} (classified {Outcome})",
                persona.Name, (int)response.StatusCode, errorBody, outcome);
            return new PersonaSubmissionResult(outcome, sw.ElapsedMilliseconds, $"{(int)response.StatusCode}: {errorBody}");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogWarning(ex, "Persona {PersonaName} submission network error", persona.Name);
            return new PersonaSubmissionResult(PersonaSubmissionOutcome.TransientFailure, sw.ElapsedMilliseconds, ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return new PersonaSubmissionResult(PersonaSubmissionOutcome.TransientFailure, sw.ElapsedMilliseconds, "Request timed out");
        }
    }

    private static bool IsTransient(HttpStatusCode code) =>
        code == HttpStatusCode.RequestTimeout
        || code == HttpStatusCode.TooManyRequests
        || (int)code >= 500;
}
