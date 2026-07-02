// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Components.User.Services.Verification;

/// <summary>
/// Typed HTTP client implementing <see cref="IHaipVerifierClient"/>. Calls the HAIP verifier
/// endpoints to create presentation requests and poll for results (Feature 164, B3).
/// WASM-safe — uses only <see cref="System.Text.Json"/> and <see cref="HttpClient"/>.
/// </summary>
public sealed class HaipVerifierClient : IHaipVerifierClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    /// <summary>Initialises the client with the provided <see cref="HttpClient"/>.</summary>
    public HaipVerifierClient(HttpClient http)
        => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <inheritdoc />
    public async Task<HaipCreateResult> CreateRequestAsync(
        string clientId,
        string credentialType,
        IReadOnlyList<string> requiredClaims,
        CancellationToken ct = default)
    {
        var body = new CreateRequestDto(credentialType, requiredClaims);
        using var response = await _http.PostAsJsonAsync("/api/v1/verifier/requests", body, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreateResultDto>(JsonDefaults.Api, ct)
            ?? throw new InvalidOperationException("HAIP verifier returned an empty create-request response.");

        return new HaipCreateResult(
            result.RequestId ?? throw new InvalidOperationException("HAIP verifier response missing requestId."),
            result.AuthorizationRequestUri ?? throw new InvalidOperationException("HAIP verifier response missing authorizationRequestUri."));
    }

    /// <inheritdoc />
    public async Task<HaipPollResult> PollResultAsync(string requestId, CancellationToken ct = default)
    {
        var url = $"/api/v1/verifier/requests/{Uri.EscapeDataString(requestId)}/result";
        using var response = await _http.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Gone)
            return new HaipPollResult("Expired", null, null);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<PollResultDto>(JsonDefaults.Api, ct)
            ?? throw new InvalidOperationException("HAIP verifier returned an empty poll response.");

        return new HaipPollResult(
            result.State ?? "Pending",
            result.VpToken,
            result.PresentationSubmission);
    }

    private sealed record CreateRequestDto(
        [property: JsonPropertyName("credentialType")] string CredentialType,
        [property: JsonPropertyName("requiredClaims")] IReadOnlyList<string> RequiredClaims);

    private sealed record CreateResultDto(
        [property: JsonPropertyName("requestId")] string? RequestId,
        [property: JsonPropertyName("authorizationRequestUri")] string? AuthorizationRequestUri);

    private sealed record PollResultDto(
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("vpToken")] string? VpToken,
        [property: JsonPropertyName("presentationSubmission")] string? PresentationSubmission);
}
