// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.X509;

namespace Sorcha.Verifier.Engine;

/// <summary>
/// Feature 181 US6 (T055 / R13) — validates a signed OpenID4VP request object so a wallet can authenticate
/// the verifier. Pure-managed <b>BouncyCastle</b> throughout (X.509 parse, SAN, ES256 JWS verify, chain
/// walk) so it runs in a Blazor WASM host where <c>System.Security.Cryptography.X509Chain</c> is unreliable.
///
/// Contract: a present-but-invalid signature or a SAN that doesn't match the <c>x509_san_dns:</c> client_id
/// host is a HARD refusal (the request must not proceed); everything else renders a three-state
/// <see cref="VerifierAuthState"/> (Trusted / AuthenticUntrusted / Unverifiable). Absent anchors NEVER block
/// (FR-027) — they cap the verdict at authentic-but-untrusted.
/// </summary>
public sealed class RequestObjectValidator
{
    private const string X509SanDnsPrefix = "x509_san_dns:";
    private const int DnsNameGeneralNameTag = 2; // RFC 5280 GeneralName dNSName

    /// <summary>
    /// Validate <paramref name="requestObjectJwt"/> (a compact JWS) against the presentation request's
    /// <paramref name="expectedClientId"/>, using <paramref name="anchors"/> for the chain-to-anchor check.
    /// </summary>
    public RequestObjectValidationResult Validate(
        string? requestObjectJwt, string? expectedClientId, VerifierTrustAnchors? anchors)
    {
        var parts = requestObjectJwt?.Split('.');
        if (parts is not { Length: 3 })
        {
            return RequestObjectValidationResult.Refuse(RequestObjectErrorCodes.RequestObjectInvalid);
        }

        JsonElement header;
        try
        {
            header = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(parts[0]));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return RequestObjectValidationResult.Refuse(RequestObjectErrorCodes.RequestObjectInvalid);
        }

        var alg = header.TryGetProperty("alg", out var algEl) ? algEl.GetString() : null;

        // No verifier certificate → cannot authenticate → Unverifiable (rendered, never blocks).
        if (!header.TryGetProperty("x5c", out var x5cEl) || x5cEl.ValueKind != JsonValueKind.Array || x5cEl.GetArrayLength() == 0)
        {
            return Unverifiable("request object carries no verifier certificate (x5c)");
        }
        if (!string.Equals(alg, "ES256", StringComparison.Ordinal))
        {
            return Unverifiable($"unsupported request-object signature algorithm '{alg}'");
        }

        List<X509Certificate> chain;
        try
        {
            var parser = new X509CertificateParser();
            chain = [.. x5cEl.EnumerateArray().Select(e => parser.ReadCertificate(Convert.FromBase64String(e.GetString()!)))];
        }
        catch (Exception)
        {
            return Unverifiable("verifier certificate could not be parsed");
        }
        var leaf = chain[0];

        // Verify the ES256 JWS signature against the leaf key. A present-but-invalid signature is TAMPERING.
        byte[] signature;
        try
        {
            signature = Base64Url.DecodeFromChars(parts[2]);
        }
        catch (FormatException)
        {
            return RequestObjectValidationResult.Refuse(RequestObjectErrorCodes.RequestObjectInvalid);
        }
        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        if (!VerifyEs256(leaf, signingInput, signature))
        {
            return RequestObjectValidationResult.Refuse(RequestObjectErrorCodes.RequestObjectInvalid);
        }

        // The verifier host comes from the prefixed client_id; the leaf SAN dNSName must match it.
        var host = ExtractHost(expectedClientId);
        if (host is null)
        {
            return Unverifiable("client_id is not an x509_san_dns identity");
        }
        var sanDnsNames = ExtractSanDnsNames(leaf);
        if (!sanDnsNames.Any(d => string.Equals(d, host, StringComparison.OrdinalIgnoreCase)))
        {
            return RequestObjectValidationResult.Refuse(RequestObjectErrorCodes.RequestHostMismatch);
        }

        // An expired verifier cert can't be trusted; render as Unverifiable rather than claim authenticity.
        if (!IsCurrentlyValid(leaf))
        {
            return Unverifiable("verifier certificate is outside its validity window");
        }

        // Chain to a trusted-list anchor. Absent anchors never block — cap at authentic-but-untrusted.
        if (anchors is { Roots.Count: > 0 } && ChainsToAnchor(chain, anchors.Roots))
        {
            return RequestObjectValidationResult.Render(
                new VerifierAuthState(VerifierAuthStatus.TrustedListVerified, host, anchors.AnchorSetId));
        }
        return RequestObjectValidationResult.Render(new VerifierAuthState(VerifierAuthStatus.AuthenticUntrusted, host));
    }

    private static RequestObjectValidationResult Unverifiable(string reason)
        => RequestObjectValidationResult.Render(new VerifierAuthState(VerifierAuthStatus.Unverifiable, Reason: reason));

    private static bool VerifyEs256(X509Certificate leaf, byte[] signingInput, byte[] p1363Signature)
    {
        if (leaf.GetPublicKey() is not ECPublicKeyParameters ecKey || p1363Signature.Length != 64)
        {
            return false;
        }
        var hash = SHA256.HashData(signingInput);
        var r = new BigInteger(1, p1363Signature[..32]);
        var s = new BigInteger(1, p1363Signature[32..]);
        var signer = new ECDsaSigner();
        signer.Init(forSigning: false, ecKey);
        try
        {
            return signer.VerifySignature(hash, r, s);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string? ExtractHost(string? clientId)
        => clientId is not null && clientId.StartsWith(X509SanDnsPrefix, StringComparison.Ordinal)
            ? clientId[X509SanDnsPrefix.Length..]
            : null;

    private static IEnumerable<string> ExtractSanDnsNames(X509Certificate cert)
    {
        var sans = cert.GetSubjectAlternativeNames();
        if (sans is null)
        {
            yield break;
        }
        foreach (var entry in sans)
        {
            // Each entry is an IList: [ int type, object value ]. dNSName type == 2.
            if (entry is System.Collections.IList list && list.Count >= 2
                && list[0] is int tag && tag == DnsNameGeneralNameTag && list[1] is string dns)
            {
                yield return dns;
            }
        }
    }

    private static bool IsCurrentlyValid(X509Certificate cert)
    {
        try
        {
            cert.CheckValidity();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Walk the leaf up through the presented intermediates to a trusted anchor root, verifying each link's
    /// signature. Returns true iff a path reaches a cert issued by (or equal to) one of the anchor roots.
    /// </summary>
    private static bool ChainsToAnchor(IReadOnlyList<X509Certificate> presented, IReadOnlyList<byte[]> anchorRootsDer)
    {
        List<X509Certificate> anchorRoots;
        try
        {
            var parser = new X509CertificateParser();
            anchorRoots = [.. anchorRootsDer.Select(parser.ReadCertificate)];
        }
        catch (Exception)
        {
            return false;
        }

        // Candidate issuers = presented intermediates + the anchor roots.
        var pool = presented.Skip(1).Concat(anchorRoots).ToList();
        var current = presented[0];

        for (var depth = 0; depth < 10; depth++)
        {
            // Directly issued by an anchor root?
            foreach (var anchor in anchorRoots)
            {
                if (current.IssuerDN.Equivalent(anchor.SubjectDN) && Verifies(current, anchor))
                {
                    return true;
                }
            }

            // Otherwise climb to the intermediate that issued `current`.
            var issuer = pool.FirstOrDefault(c =>
                !ReferenceEquals(c, current) && current.IssuerDN.Equivalent(c.SubjectDN) && Verifies(current, c));
            if (issuer is null)
            {
                return false;
            }
            // If we climbed onto an anchor root itself, that's trusted.
            if (anchorRoots.Any(a => a.Equals(issuer)))
            {
                return true;
            }
            current = issuer;
        }
        return false;
    }

    private static bool Verifies(X509Certificate cert, X509Certificate issuer)
    {
        try
        {
            cert.Verify(issuer.GetPublicKey());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
