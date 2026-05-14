// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Presentation;

namespace Sorcha.Wallet.Pwa.Services.Presentation;

/// <summary>
/// Default <see cref="IPresentationEngine"/>. v1 understands the unsigned
/// query-parameter form of <c>openid4vp://</c> the reference verifier emits
/// (research §R-008). Signed-request mode is a hardening item for a later phase.
/// </summary>
public sealed class PresentationEngine : IPresentationEngine
{
    private readonly TimeProvider _clock;
    private readonly ILogger<PresentationEngine> _logger;

    /// <summary>Initialises a new instance.</summary>
    public PresentationEngine(TimeProvider clock, ILogger<PresentationEngine> logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ParsedPresentationRequest Parse(string openid4vpDeepLink)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(openid4vpDeepLink);
        if (!openid4vpDeepLink.StartsWith("openid4vp://", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Deep link must start with openid4vp://");
        }

        var queryStart = openid4vpDeepLink.IndexOf('?');
        if (queryStart < 0)
        {
            throw new FormatException("Deep link is missing the query component.");
        }

        NameValueCollection parsed = HttpUtility.ParseQueryString(openid4vpDeepLink[(queryStart + 1)..]);

        var clientId = parsed["client_id"]
            ?? throw new FormatException("Deep link is missing client_id.");
        var responseUri = parsed["response_uri"]
            ?? throw new FormatException("Deep link is missing response_uri.");
        var nonce = parsed["nonce"]
            ?? throw new FormatException("Deep link is missing nonce.");
        var responseMode = parsed["response_mode"] ?? "direct_post";

        var pdJson = parsed["presentation_definition"]
            ?? throw new FormatException("Deep link is missing presentation_definition.");

        var (vct, required, optional, purpose) = ParsePresentationDefinition(pdJson);

        return new ParsedPresentationRequest
        {
            ClientId = clientId,
            ResponseUri = responseUri,
            Nonce = nonce,
            RequiredVct = vct,
            RequiredClaims = required,
            OptionalClaims = optional,
            Purpose = purpose,
            ResponseMode = responseMode,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<CredentialMatch> Match(
        ParsedPresentationRequest request,
        IReadOnlyList<CachedCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        var matches = new List<CredentialMatch>();
        foreach (var credential in credentials)
        {
            if (!string.Equals(credential.Vct, request.RequiredVct, StringComparison.Ordinal)) continue;

            var availableNames = new HashSet<string>(credential.AvailableClaimNames, StringComparer.Ordinal);
            var satisfied = request.RequiredClaims.Where(c => availableNames.Contains(c)).ToList();
            if (satisfied.Count != request.RequiredClaims.Count)
            {
                continue;
            }

            var optional = request.OptionalClaims.Where(c => availableNames.Contains(c)).ToList();
            matches.Add(new CredentialMatch
            {
                Credential = credential,
                SatisfiedRequired = satisfied,
                AvailableOptional = optional,
            });
        }

        return matches;
    }

    /// <inheritdoc />
    public async Task<string> BuildVpTokenAsync(
        CredentialMatch match,
        IReadOnlyList<string> approvedClaims,
        ParsedPresentationRequest request,
        JsonElement deviceJwk,
        Func<byte[], CancellationToken, Task<byte[]>> deviceSigner,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(approvedClaims);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(deviceSigner);

        // Sanity check: every required claim must be in the approved set.
        foreach (var required in request.RequiredClaims)
        {
            if (!approvedClaims.Contains(required, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Approved claims do not cover required claim '{required}'.");
            }
        }

        var (credentialJwt, allDisclosures, _) = SplitSdJwt(match.Credential.RawSdJwt);
        if (credentialJwt is null)
        {
            throw new InvalidOperationException("Cached credential is not a valid SD-JWT.");
        }

        // Pick only the disclosures whose claim name was approved.
        var approvedSet = new HashSet<string>(approvedClaims, StringComparer.Ordinal);
        var selected = new List<string>(allDisclosures.Count);
        foreach (var seg in allDisclosures)
        {
            var name = ReadDisclosureName(seg);
            if (name is not null && approvedSet.Contains(name))
            {
                selected.Add(seg);
            }
        }

        // KB-JWT — header carries the device-key thumbprint as kid; payload binds nonce+aud.
        var kid = ComputeJwkThumbprint(deviceJwk);
        var header = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = "kb+jwt",
            ["kid"] = kid,
        };
        // sd_hash per RFC 9901 §4.3 — SHA-256 of the to-be-presented hashable form.
        // Compute over the canonical "credentialJwt~disclosure1~..~" prefix.
        var hashable = credentialJwt + string.Concat(selected.Select(s => "~" + s)) + "~";
        var sdHash = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(hashable)));

        var payload = new Dictionary<string, object>
        {
            ["iat"] = _clock.GetUtcNow().ToUnixTimeSeconds(),
            ["aud"] = request.ClientId,
            ["nonce"] = request.Nonce,
            ["sd_hash"] = sdHash,
        };

        var headerSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.ASCII.GetBytes($"{headerSeg}.{payloadSeg}");
        var signature = await deviceSigner(signingInput, ct);
        var kbJwt = $"{headerSeg}.{payloadSeg}.{Base64Url.EncodeToString(signature)}";

        var vpToken = hashable + kbJwt;
        _logger.LogInformation(
            "Built vp_token: credentialId={Id}, disclosures={Count}, audience={Aud}",
            match.Credential.Id, selected.Count, request.ClientId);
        return vpToken;
    }

    // ─────────────────────────── parsing helpers ─────────────────────────────────

    /// <summary>
    /// Parse the verifier-supplied PEX presentation_definition into the simplified
    /// shape the engine consumes: required vct, required claim names, optional claim names.
    /// Mirror image of <c>PresentationRequestBuilder.BuildPresentationDefinitionJson</c>.
    /// </summary>
    internal static (string Vct, IReadOnlyList<string> Required, IReadOnlyList<string> Optional, string? Purpose)
        ParsePresentationDefinition(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var inputDescriptors = root.GetProperty("input_descriptors");
        if (inputDescriptors.GetArrayLength() == 0)
        {
            throw new FormatException("presentation_definition has no input_descriptors.");
        }
        var primary = inputDescriptors[0];
        var purpose = primary.TryGetProperty("purpose", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;

        var fields = primary.GetProperty("constraints").GetProperty("fields");

        string? vct = null;
        var required = new List<string>();
        var optional = new List<string>();

        foreach (var field in fields.EnumerateArray())
        {
            var paths = field.GetProperty("path");
            if (paths.GetArrayLength() == 0) continue;
            var path = paths[0].GetString();
            if (path is null) continue;

            // vct constraint
            if (path == "$.vct" && field.TryGetProperty("filter", out var filter)
                && filter.TryGetProperty("const", out var constEl)
                && constEl.ValueKind == JsonValueKind.String)
            {
                vct = constEl.GetString();
                continue;
            }

            // claim field — convert "$.givenName" → "givenName", or "$.foo.bar" → "/foo/bar"
            var claimName = FromJsonPath(path);
            var isOptional = field.TryGetProperty("optional", out var opt)
                && opt.ValueKind == JsonValueKind.True;
            if (isOptional) optional.Add(claimName);
            else required.Add(claimName);
        }

        if (vct is null)
        {
            throw new FormatException("presentation_definition is missing a vct constraint.");
        }
        return (vct, required, optional, purpose);
    }

    private static string FromJsonPath(string path)
    {
        if (!path.StartsWith("$.", StringComparison.Ordinal)) return path;
        var rest = path[2..];
        return rest.Contains('.') ? "/" + rest.Replace('.', '/') : rest;
    }

    /// <summary>
    /// Split the SD-JWT compact form into (credentialJwt, disclosures, kbJwt). Mirror
    /// of <c>VerifiablePresentationValidator.SplitSdJwt</c> — duplicated here to avoid a
    /// project reference between wallet and verifier (they ship as separate apps).
    /// </summary>
    internal static (string? Credential, IReadOnlyList<string> Disclosures, string? KbJwt) SplitSdJwt(string vp)
    {
        var segments = vp.Split('~');
        if (segments.Length < 1) return (null, [], null);

        var credential = segments[0];
        var last = segments[^1];
        var hasKbJwt = !string.IsNullOrEmpty(last) && last.Count(c => c == '.') == 2;

        var disclosureCount = segments.Length - 1 - (hasKbJwt ? 1 : 0);
        var disclosures = new List<string>(Math.Max(disclosureCount, 0));
        for (int i = 1; i <= disclosureCount; i++)
        {
            if (!string.IsNullOrEmpty(segments[i])) disclosures.Add(segments[i]);
        }
        return (credential, disclosures, hasKbJwt ? last : null);
    }

    /// <summary>Read the claim name out of a single SD-JWT disclosure segment.</summary>
    internal static string? ReadDisclosureName(string segment)
    {
        try
        {
            var bytes = Base64Url.DecodeFromChars(segment);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            return doc.RootElement.GetArrayLength() switch
            {
                3 => doc.RootElement[1].GetString(),
                2 => doc.RootElement[0].GetString(),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Compute the RFC 7638 JWK thumbprint for an EC P-256 public JWK.</summary>
    internal static string ComputeJwkThumbprint(JsonElement jwk)
    {
        var crv = jwk.GetProperty("crv").GetString();
        var kty = jwk.GetProperty("kty").GetString();
        var x = jwk.GetProperty("x").GetString();
        var y = jwk.GetProperty("y").GetString();
        var canonical = $"{{\"crv\":\"{crv}\",\"kty\":\"{kty}\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
