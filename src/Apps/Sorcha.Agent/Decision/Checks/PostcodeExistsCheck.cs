// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Sorcha.Agent.Decision.Checks;

/// <summary>
/// How <see cref="PostcodeExistsCheck"/> reconciles the live lookup with the bundled offline fixture.
/// </summary>
public enum PostcodeOfflineMode
{
    /// <summary>Call postcodes.io; fall back to the bundled fixture only when the call faults (default).</summary>
    Auto,

    /// <summary>Never call the network — resolve solely against the bundled fixture (offline venue).</summary>
    Always,

    /// <summary>Always call the network — a fault resolves to <c>false</c> (no fixture fallback).</summary>
    Never
}

/// <summary>
/// Checks that the application's UK postcode exists. Calls the public, keyless
/// <c>postcodes.io</c> <c>/postcodes/{postcode}/validate</c> endpoint when available, and degrades
/// gracefully to a bundled offline fixture so the assurance step keeps working without internet
/// (SC-007). <c>Detail</c> carries the queried postcode for the on-brand rejection reason.
/// </summary>
public sealed class PostcodeExistsCheck : IExternalCheck
{
    private readonly string _addressField;
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly IReadOnlySet<string> _offlineFixture;
    private readonly PostcodeOfflineMode _offlineMode;
    private readonly ILogger? _logger;

    /// <summary>Creates the postcode-existence check.</summary>
    /// <param name="name">Fact key (e.g. <c>postcodeExists</c>).</param>
    /// <param name="addressField">JSON-Pointer to the address (object with a <c>postcode</c> property) or a bare postcode string.</param>
    /// <param name="httpClient">Client used for the postcodes.io call.</param>
    /// <param name="offlineFixture">Known-good postcodes for the offline fallback (any spacing/casing).</param>
    /// <param name="offlineMode">How the live lookup and fixture are reconciled.</param>
    /// <param name="baseUrl">postcodes.io base URL (default <c>https://api.postcodes.io</c>).</param>
    /// <param name="logger">Optional logger.</param>
    public PostcodeExistsCheck(
        string name,
        string addressField,
        HttpClient httpClient,
        IEnumerable<string> offlineFixture,
        PostcodeOfflineMode offlineMode = PostcodeOfflineMode.Auto,
        string? baseUrl = null,
        ILogger? logger = null)
    {
        Name = name;
        _addressField = string.IsNullOrWhiteSpace(addressField) ? "/address" : addressField;
        _httpClient = httpClient;
        _offlineFixture = (offlineFixture ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Normalize)
            .ToHashSet();
        _offlineMode = offlineMode;
        _baseUrl = (baseUrl ?? "https://api.postcodes.io").TrimEnd('/');
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct)
    {
        var postcode = ExtractPostcode(payload);
        if (string.IsNullOrWhiteSpace(postcode))
            return new ExternalCheckResult(Name, false, null);

        var normalized = Normalize(postcode);

        if (_offlineMode == PostcodeOfflineMode.Always)
            return new ExternalCheckResult(Name, _offlineFixture.Contains(normalized), postcode);

        try
        {
            var exists = await ValidateOnlineAsync(normalized, ct);
            return new ExternalCheckResult(Name, exists, postcode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (_offlineMode == PostcodeOfflineMode.Never)
            {
                _logger?.LogWarning(ex, "postcodes.io lookup failed for {Postcode}; offlineMode=Never resolves to false", postcode);
                return new ExternalCheckResult(Name, false, postcode);
            }

            _logger?.LogWarning(ex, "postcodes.io unreachable for {Postcode}; falling back to bundled fixture", postcode);
            return new ExternalCheckResult(Name, _offlineFixture.Contains(normalized), postcode);
        }
    }

    private async Task<bool> ValidateOnlineAsync(string postcode, CancellationToken ct)
    {
        var uri = $"{_baseUrl}/postcodes/{Uri.EscapeDataString(postcode)}/validate";
        using var response = await _httpClient.GetAsync(uri, ct);

        // 404 = postcodes.io received and understood the request, but the postcode is invalid or
        // malformed. This is NOT a network fault — do not fall back to the offline fixture.
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return false;

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.TryGetProperty("result", out var result)
            && result.ValueKind == JsonValueKind.True;
    }

    private string? ExtractPostcode(IReadOnlyDictionary<string, object?> payload)
    {
        var node = PayloadPointer.Resolve(payload, _addressField);
        return node switch
        {
            JsonObject obj when obj.TryGetPropertyValue("postcode", out var pc) && pc is JsonValue => pc.ToString(),
            JsonValue value => value.ToString(),
            _ => null
        };
    }

    private static string Normalize(string postcode) =>
        new string(postcode.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
}
