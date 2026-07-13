// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Sorcha.Mdoc.Cbor;
using Sorcha.Mdoc.Cose;

namespace Sorcha.Mdoc;

/// <summary>The OpenID4VP session parameters needed to reconstruct the mdoc <c>SessionTranscript</c>.</summary>
public sealed class MdocSessionTranscript
{
    public required string ClientId { get; init; }
    public required string Nonce { get; init; }
    public byte[]? JwkThumbprint { get; init; }
    public required string ResponseUri { get; init; }
}

/// <summary>
/// Result of verifying an mdoc <see cref="DeviceResponse"/> at the cryptographic layer (feature 135).
/// The trust decision (does the issuer chain to a trusted anchor) is the unified evaluator's job;
/// this records the format-level facts it consumes.
/// </summary>
public sealed class MdocVerificationResult
{
    /// <summary>Whether the issuer COSE_Sign1 over the MSO verified against the x5chain leaf key.</summary>
    public bool IssuerSignatureValid { get; set; }

    /// <summary>Whether every disclosed item's tag-24 digest matched the MSO <c>valueDigests</c>.</summary>
    public bool DigestsValid { get; set; }

    /// <summary>Whether the device auth verified against the reconstructed DeviceAuthentication.</summary>
    public bool DeviceBindingValid { get; set; }

    /// <summary>Whether the current time is within the MSO validity window.</summary>
    public bool ValidityOk { get; set; }

    public string DocType { get; set; } = string.Empty;

    /// <summary>Disclosed elements as <c>elementIdentifier → value</c>, flattened across namespaces.</summary>
    public Dictionary<string, object> Claims { get; set; } = new();

    /// <summary>The issuer's x5chain (leaf-first DER) for trust evaluation, when present.</summary>
    public IReadOnlyList<byte[]>? X5cChain { get; set; }

    /// <summary>The issuer identifier (x5chain leaf subject), when resolvable.</summary>
    public string? IssuerId { get; set; }

    /// <summary>The MSO status reference for revocation, when present.</summary>
    public MsoStatus? Status { get; set; }

    public List<string> Errors { get; set; } = [];

    /// <summary>All format-level checks passed. Trust is decided separately by the evaluator.</summary>
    public bool IsValid => IssuerSignatureValid && DigestsValid && DeviceBindingValid && ValidityOk && Errors.Count == 0;
}

/// <summary>Verifies ISO 18013-5 mdoc <see cref="DeviceResponse"/> presentations (feature 135).</summary>
public interface IMdocService
{
    /// <summary>
    /// Verifies the first document of an mdoc <c>DeviceResponse</c>: issuer signature, value-digest
    /// integrity, and holder binding against the OpenID4VP session transcript.
    /// </summary>
    MdocVerificationResult Verify(ReadOnlyMemory<byte> deviceResponse, MdocSessionTranscript transcript);

    /// <summary>
    /// Verifies an mdoc <c>DeviceResponse</c> against an <b>already-built</b> session transcript — the form
    /// the ISO 18013-5 <b>proximity</b> exchange needs (feature 185).
    /// </summary>
    /// <param name="deviceResponse">The response bytes.</param>
    /// <param name="sessionTranscript">
    /// The <b>bare</b> encoded <c>SessionTranscript</c> array (not the tag-24-wrapped form — that one is only
    /// for the HKDF salt).
    /// </param>
    /// <param name="eMacKey">
    /// The <c>EMacKey</c>, when the holder authenticated with <c>deviceMac</c>. Pass <see langword="null"/>
    /// (or empty) if only <c>deviceSignature</c> is expected — a <c>deviceMac</c> then cannot be verified and
    /// is <b>rejected</b> rather than waved through.
    /// </param>
    /// <remarks>
    /// The OpenID4VP overload delegates here, so the two paths cannot drift apart. Both device-auth forms are
    /// accepted: ISO requires exactly one of them, and a conformant verifier must handle either.
    /// </remarks>
    MdocVerificationResult Verify(
        ReadOnlyMemory<byte> deviceResponse, byte[] sessionTranscript, byte[]? eMacKey = null);
}

/// <inheritdoc />
public sealed class MdocService : IMdocService
{
    private readonly TimeProvider _timeProvider;

    public MdocService(TimeProvider? timeProvider = null) => _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public MdocVerificationResult Verify(ReadOnlyMemory<byte> deviceResponse, MdocSessionTranscript transcript)
    {
        ArgumentNullException.ThrowIfNull(transcript);

        // Build the OpenID4VP transcript, then hand off to the one real implementation. Keeping a single
        // verification body is what stops the online and proximity paths quietly diverging.
        var sessionTranscript = MdocCodec.BuildOpenId4VpSessionTranscript(
            transcript.ClientId, transcript.Nonce, transcript.JwkThumbprint, transcript.ResponseUri);

        return Verify(deviceResponse, sessionTranscript, eMacKey: null);
    }

    /// <inheritdoc />
    public MdocVerificationResult Verify(
        ReadOnlyMemory<byte> deviceResponse, byte[] sessionTranscript, byte[]? eMacKey = null)
    {
        ArgumentNullException.ThrowIfNull(sessionTranscript);
        var result = new MdocVerificationResult();

        try
        {
            var response = MdocCodec.DecodeDeviceResponse(deviceResponse);
            if (response.Documents.Count == 0)
            {
                result.Errors.Add("DeviceResponse contains no documents.");
                return result;
            }

            var doc = response.Documents[0];
            result.DocType = doc.DocType;

            // Surface disclosed claims regardless of verification outcome (for diagnostics).
            foreach (var (_, items) in doc.IssuerSigned.NameSpaces)
                foreach (var item in items)
                    result.Claims[item.Item.ElementIdentifier] = item.Item.ElementValue ?? string.Empty;

            // x5chain → issuer leaf key.
            var chain = CoseX5Chain.Read(doc.IssuerSigned.IssuerAuth);
            result.X5cChain = chain;
            if (chain is null || chain.Count == 0)
            {
                result.Errors.Add("issuerAuth carries no x5chain — cannot resolve the issuer key.");
                return result;
            }

            // BouncyCastle, not X509CertificateLoader/GetECDsaPublicKey: this assembly runs in a Blazor WASM
            // host (the reader app verifies on-device, offline), where BCL platform crypto is not dependable.
            result.IssuerId = X509Leaf.TryReadSubject(chain[0]);
            var issuerKey = X509Leaf.TryReadEcPublicKey(chain[0]);
            if (issuerKey is null)
            {
                result.Errors.Add("x5chain leaf certificate has no EC public key (mdoc is P-256/ES256 only).");
                return result;
            }

            // 1) Issuer signature over the tag-24-wrapped MSO.
            result.IssuerSignatureValid = CoseSign1Builder.VerifyEmbedded(doc.IssuerSigned.IssuerAuth, issuerKey);

            var content = doc.IssuerSigned.IssuerAuth.Content;
            if (content is null)
            {
                result.Errors.Add("issuerAuth has no embedded MSO content.");
                return result;
            }
            var mso = MdocCodec.DecodeMso(MdocCbor.UnwrapTag24(content.Value));
            result.Status = mso.Status;

            // 2) Value-digest integrity over the verbatim tag-24 item bytes.
            result.DigestsValid = VerifyDigests(doc.IssuerSigned, mso);

            // 3) Holder binding: device auth over the reconstructed DeviceAuthentication.
            result.DeviceBindingValid = VerifyDeviceBinding(doc, mso, sessionTranscript, eMacKey, result);

            // 4) Validity window.
            var now = _timeProvider.GetUtcNow();
            result.ValidityOk = now >= mso.ValidityInfo.ValidFrom && now <= mso.ValidityInfo.ValidUntil;
            if (!result.ValidityOk)
                result.Errors.Add($"MSO is outside its validity window ({mso.ValidityInfo.ValidFrom:o} … {mso.ValidityInfo.ValidUntil:o}).");
        }
        catch (Exception ex) when (ex is CborContentException or InvalidOperationException or CryptographicException or FormatException)
        {
            result.Errors.Add($"mdoc verification failed: {ex.Message}");
        }

        return result;
    }

    private static bool VerifyDigests(IssuerSigned issuerSigned, MobileSecurityObject mso)
    {
        foreach (var (ns, items) in issuerSigned.NameSpaces)
        {
            if (!mso.ValueDigests.TryGetValue(ns, out var nsDigests))
                return false;

            foreach (var item in items)
            {
                if (!nsDigests.TryGetValue(item.Item.DigestId, out var expected))
                    return false;
                var actual = Hash(mso.DigestAlgorithm, item.TaggedBytes);
                if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Verifies the holder's proof of possession over the reconstructed <c>DeviceAuthentication</c>.
    /// </summary>
    /// <remarks>
    /// ISO 18013-5 offers two forms and requires exactly one of them; a conformant verifier must accept
    /// either. Feature 135 could only do <c>deviceSignature</c> (the BCL has no <c>COSE_Mac0</c> type, so
    /// <c>deviceMac</c> was refused outright); feature 185 added <see cref="CoseMac0"/> and closes that gap.
    /// </remarks>
    private static bool VerifyDeviceBinding(
        Document doc,
        MobileSecurityObject mso,
        byte[] sessionTranscript,
        byte[]? eMacKey,
        MdocVerificationResult result)
    {
        var auth = doc.DeviceSigned.DeviceAuth;

        var deviceAuthentication = MdocCodec.BuildDeviceAuthentication(
            sessionTranscript, doc.DocType, doc.DeviceSigned.NameSpacesBytes);

        if (auth.DeviceSignature is not null)
        {
            var deviceKey = CoseKey.TryParseEc2PublicKey(mso.DeviceKeyCose);
            if (deviceKey is null)
            {
                result.Errors.Add("MSO device key is not a P-256 EC2 COSE_Key.");
                return false;
            }

            return CoseSign1Builder.VerifyDetached(auth.DeviceSignature, deviceAuthentication, deviceKey);
        }

        if (auth.DeviceMacRaw is not null)
        {
            if (eMacKey is null || eMacKey.Length == 0)
            {
                // Fail closed. A deviceMac we cannot check is an UNVERIFIED holder binding, and treating it
                // as acceptable would defeat the whole point of the check.
                result.Errors.Add(
                    "DeviceAuth carries a deviceMac but no EMacKey was supplied, so holder binding cannot " +
                    "be verified. (A deviceMac is only meaningful inside the proximity session that derived " +
                    "the key — the online OpenID4VP path has no such key and expects a deviceSignature.)");
                return false;
            }

            return CoseMac0.VerifyDetached(auth.DeviceMacRaw, deviceAuthentication, eMacKey);
        }

        result.Errors.Add("DeviceAuth carries neither a deviceSignature nor a deviceMac.");
        return false;
    }

    private static byte[] Hash(string algorithm, byte[] data) => algorithm.ToUpperInvariant() switch
    {
        "SHA-256" => SHA256.HashData(data),
        "SHA-384" => SHA384.HashData(data),
        "SHA-512" => SHA512.HashData(data),
        _ => throw new NotSupportedException($"Unsupported MSO digest algorithm '{algorithm}'.")
    };

    // ParseEc2CoseKey was removed in feature 185: it returned a BCL ECDsa, which is exactly the dependency
    // that cannot be relied on in WASM. CoseKey.TryParseEc2PublicKey supersedes it and additionally rejects
    // off-curve points — an attacker-supplied invalid point is the classic invalid-curve attack against the
    // ECDH that follows.
}
