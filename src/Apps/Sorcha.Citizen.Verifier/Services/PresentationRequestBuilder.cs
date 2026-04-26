// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Sorcha.Citizen.Verifier.Services.Models;

namespace Sorcha.Citizen.Verifier.Services;

/// <summary>
/// Default <see cref="IPresentationRequestBuilder"/>. Builds an unsigned OID4VP
/// cross-device request per research §R-008. The request is conveyed inline as
/// query parameters so the wallet can parse it offline without fetching
/// <c>request_uri</c>. The wallet's KB-JWT is what carries integrity for v1 —
/// signed-request mode is a hardening item for a later phase.
/// </summary>
public sealed class PresentationRequestBuilder : IPresentationRequestBuilder
{
    private readonly IVerifierSessionStore _store;
    private readonly TimeProvider _clock;
    private readonly ILogger<PresentationRequestBuilder> _logger;

    /// <summary>Default validity window for a generated request.</summary>
    public static readonly TimeSpan DefaultValidity = TimeSpan.FromMinutes(5);

    /// <summary>Initialises a new instance.</summary>
    public PresentationRequestBuilder(
        IVerifierSessionStore store,
        TimeProvider clock,
        ILogger<PresentationRequestBuilder> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<PresentationRequestResult> CreateAsync(
        Guid verifierOrgId,
        string purpose,
        string requiredVct,
        IReadOnlyList<string> requiredClaims,
        IReadOnlyList<string> optionalClaims,
        string responseBaseUri,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredVct);
        ArgumentException.ThrowIfNullOrWhiteSpace(responseBaseUri);
        ArgumentNullException.ThrowIfNull(requiredClaims);
        ArgumentNullException.ThrowIfNull(optionalClaims);

        var sessionId = NewUrlSafeId(16);
        var nonce = NewUrlSafeId(16);
        var clientId = $"did:sorcha:verifier:{verifierOrgId:N}";
        var now = _clock.GetUtcNow();

        var session = new VerifierSession
        {
            SessionId = sessionId,
            ClientId = clientId,
            Nonce = nonce,
            RequiredVct = requiredVct,
            RequiredClaims = requiredClaims,
            OptionalClaims = optionalClaims,
            Purpose = purpose,
            CreatedAt = now,
            ExpiresAt = now + DefaultValidity,
        };

        _store.Add(session);

        var presentationDefinition = BuildPresentationDefinitionJson(
            sessionId, requiredVct, requiredClaims, optionalClaims, purpose);

        var responseUri = $"{responseBaseUri.TrimEnd('/')}/verify/r/{sessionId}/response";

        var deepLink =
            "openid4vp://?" +
            $"client_id={Uri.EscapeDataString(clientId)}" +
            "&response_mode=direct_post" +
            $"&response_uri={Uri.EscapeDataString(responseUri)}" +
            $"&nonce={Uri.EscapeDataString(nonce)}" +
            $"&presentation_definition={Uri.EscapeDataString(presentationDefinition)}";

        _logger.LogInformation(
            "Verifier session created: id={SessionId}, vct={Vct}, requiredClaims={Required}",
            sessionId, requiredVct, requiredClaims.Count);

        return Task.FromResult(new PresentationRequestResult(deepLink, session));
    }

    /// <summary>
    /// Build a Presentation Exchange (PEX) presentation_definition document. We use the
    /// standard PEX shape with a single <c>input_descriptor</c> that constrains by
    /// <c>vct</c> and lists every required + optional claim as a JSON pointer path.
    /// Required claims have <c>optional=false</c>; optional ones <c>optional=true</c>.
    /// </summary>
    internal static string BuildPresentationDefinitionJson(
        string sessionId,
        string vct,
        IReadOnlyList<string> requiredClaims,
        IReadOnlyList<string> optionalClaims,
        string purpose)
    {
        var fields = new List<object>
        {
            // vct constraint — every credential surfacing this descriptor must declare the type
            new
            {
                path = new[] { "$.vct" },
                filter = new { type = "string", @const = vct }
            }
        };

        foreach (var claim in requiredClaims)
        {
            fields.Add(new { path = new[] { ToJsonPath(claim) }, optional = false });
        }
        foreach (var claim in optionalClaims)
        {
            fields.Add(new { path = new[] { ToJsonPath(claim) }, optional = true });
        }

        var doc = new
        {
            id = sessionId,
            input_descriptors = new[]
            {
                new
                {
                    id = "primary",
                    name = vct,
                    purpose,
                    constraints = new
                    {
                        limit_disclosure = "required",
                        fields = fields.ToArray()
                    }
                }
            }
        };

        return JsonSerializer.Serialize(doc);
    }

    private static string ToJsonPath(string claimName)
    {
        // Allow callers to pass either a bare claim name ("givenName") or a JSON pointer
        // ("/credentialSubject/givenName"). Convert pointer form to JSONPath dotted form.
        if (claimName.StartsWith('/'))
        {
            return "$" + claimName.Replace('/', '.');
        }
        return $"$.{claimName}";
    }

    private static string NewUrlSafeId(int byteLen)
    {
        Span<byte> buf = stackalloc byte[byteLen];
        RandomNumberGenerator.Fill(buf);
        return Base64Url.EncodeToString(buf);
    }
}
