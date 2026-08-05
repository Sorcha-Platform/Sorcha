// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Verification.Abstractions;
using Sorcha.Verifier.Engine.Models;

namespace Sorcha.UI.Components.User.Services.Verification;

/// <summary>
/// Maps HAIP's authoritative server-side verification result into the engine's
/// <see cref="VerificationOutcome"/> so the shared VerdictTrailPanel can render the four-layer
/// trust trail. HAIP verifies the presentation online (with the real nonce and issuer-key
/// resolution), so an accepted online result means every offline layer passed and the issuer
/// signature was verified. The register-anchor (layer 4) is appended on demand by the panel.
/// WASM-safe — System.Text.Json only.
/// </summary>
public static class HaipOutcomeMapper
{
    /// <summary>Builds a <see cref="VerificationOutcome"/> from HAIP's poll result.</summary>
    public static VerificationOutcome Map(
        bool accepted,
        IReadOnlyDictionary<string, object?> disclosedClaims,
        IReadOnlyList<string> errors,
        bool holderKeyVerified,
        string? vpToken,
        DateTimeOffset completedAt)
    {
        var live = accepted && holderKeyVerified ? VerificationStatus.Verified : VerificationStatus.Failed;
        var issuer = accepted ? VerificationStatus.Verified : VerificationStatus.Failed;
        var revocation = accepted ? VerificationStatus.Verified : VerificationStatus.Failed;

        var (iss, jti) = ExtractIssuerAndJti(vpToken);

        var issuerDetail = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(iss)) issuerDetail["iss"] = iss!;
        if (!string.IsNullOrEmpty(jti)) issuerDetail["jti"] = jti!;

        var layers = new List<ValidationLayerResult>
        {
            new()
            {
                Layer = ValidationLayer.LivePresentation,
                Status = live,
                Headline = live == VerificationStatus.Verified ? "Proved on the holder's own device" : "Live presentation failed",
            },
            new()
            {
                Layer = ValidationLayer.IssuerSignature,
                Status = issuer,
                Headline = issuer == VerificationStatus.Verified ? "Signed by the issuer" : "Issuer signature not verified",
                Detail = issuerDetail,
            },
            new()
            {
                Layer = ValidationLayer.Revocation,
                Status = revocation,
                Headline = revocation == VerificationStatus.Verified ? "Checked against the issuer's status list" : "Revocation check failed",
            },
        };

        return new VerificationOutcome
        {
            Accepted = accepted,
            DisclosedClaims = disclosedClaims,
            Errors = errors,
            CompletedAt = completedAt,
            // HAIP online verification requires and resolves the issuer signature; an accepted result is Verified.
            IssuerSignature = accepted ? IssuerSignatureStatus.Verified : IssuerSignatureStatus.NotVerified,
            Layers = layers,
        };
    }

    private static (string? iss, string? jti) ExtractIssuerAndJti(string? vpToken)
    {
        if (string.IsNullOrWhiteSpace(vpToken)) return (null, null);
        try
        {
            // SD-JWT VC compact form: <issuer-jwt>~<disclosure>~...~<kb-jwt>. The issuer JWT is first.
            var jwt = vpToken.Split('~', 2)[0];
            var parts = jwt.Split('.');
            if (parts.Length < 2) return (null, null);
            var payloadJson = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var iss = root.TryGetProperty("iss", out var i) ? i.GetString() : null;
            var jti = root.TryGetProperty("jti", out var j) ? j.GetString() : null;
            return (iss, jti);
        }
        catch
        {
            return (null, null);
        }
    }

    private static string Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
