// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Mdoc.Cbor;
using Sorcha.Mdoc.Cose;

namespace Sorcha.Mdoc.Proximity;

/// <summary>An mdoc credential as the holder stores it: the issuer's signed bundle, plus its doc type.</summary>
/// <param name="DocType">e.g. <c>org.iso.18013.5.1.mDL</c>.</param>
/// <param name="IssuerSigned">The issuer-signed bundle, exactly as issued.</param>
public sealed record HeldMdoc(string DocType, IssuerSigned IssuerSigned);

/// <summary>
/// Builds the holder's <c>DeviceResponse</c> — <b>the half of mdoc that did not exist in this codebase</b>.
/// </summary>
/// <remarks>
/// <para>
/// Before feature 185, <c>MdocIssuer</c> could issue and <c>MdocService</c> could verify, but nothing could
/// <em>present</em>: <c>MdocCodec</c>'s write path is private, and the only public entry point demanded a
/// fully-formed <c>CoseSign1Message</c> — which a non-extractable device key cannot produce. This closes that.
/// </para>
/// <para><b>The two rules that make or break selective disclosure:</b></para>
/// <list type="number">
///   <item>
///     <description>
///     <b>Splice the stored <c>TaggedBytes</c> verbatim. Never re-encode an <c>IssuerSignedItem</c>.</b> The
///     issuer's digests are computed over the tag-24-wrapped <em>outer</em> bytes. Re-encoding can produce
///     different bytes for the same logical value (map ordering, integer width, string form) — and the
///     digest then won't match, invalidating the issuer's signature over data the issuer really did sign.
///     The failure is total and gives no useful diagnostic.
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Emit only what was requested and approved.</b> Selective disclosure is achieved by <em>omitting</em>
///     items — the MSO still carries a digest for every element the credential contains, and the verifier
///     checks only the ones actually present. An omitted item leaks nothing beyond its existence.
///     </description>
///   </item>
/// </list>
/// </remarks>
public static class MdocDeviceResponseBuilder
{
    /// <summary>Signs the <c>DeviceAuthentication</c> bytes, returning a raw 64-byte <c>r ‖ s</c> ES256 signature.</summary>
    /// <remarks>
    /// A delegate rather than a key, because on the citizen's phone the key is a non-extractable WebCrypto
    /// handle and this is the only operation it exposes.
    /// </remarks>
    public delegate Task<byte[]> DeviceSignerAsync(byte[] deviceAuthenticationBytes, CancellationToken ct);

    /// <summary>
    /// Builds a <c>DeviceResponse</c> disclosing exactly <paramref name="approved"/> from
    /// <paramref name="credential"/>, authenticated with <c>deviceMac</c>.
    /// </summary>
    /// <param name="credential">The held mdoc.</param>
    /// <param name="approved">
    /// The elements the citizen approved. <b>Only these are emitted.</b> Anything the reader asked for but the
    /// citizen did not approve is simply absent.
    /// </param>
    /// <param name="transcript">The session transcript. Its <b>bare</b> form is spliced into <c>DeviceAuthentication</c>.</param>
    /// <param name="eMacKey">The <c>EMacKey</c> derived from the static device key and the reader's ephemeral key.</param>
    public static byte[] BuildWithMac(
        HeldMdoc credential,
        IReadOnlyCollection<RequestedElement> approved,
        ProximitySessionTranscript transcript,
        byte[] eMacKey)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(eMacKey);
        if (eMacKey.Length == 0)
            throw new ArgumentException(
                "EMacKey is empty. The mdoc's device key must be ECDH-capable to produce a deviceMac — an " +
                "ECDSA-only key cannot (feature 185, design §3).", nameof(eMacKey));

        var (disclosed, deviceNameSpacesBytes, deviceAuthentication) =
            Prepare(credential, approved, transcript);

        var deviceMac = CoseMac0.BuildDetached(deviceAuthentication, eMacKey);

        return Assemble(credential, disclosed, deviceNameSpacesBytes,
            new DeviceAuth { DeviceMacRaw = deviceMac });
    }

    /// <summary>
    /// Builds a <c>DeviceResponse</c> authenticated with <c>deviceSignature</c> instead of <c>deviceMac</c>.
    /// </summary>
    /// <remarks>
    /// ISO 18013-5 requires exactly one of the two, and a conformant reader must accept either. Signature is
    /// the right choice when the credential is bound to an ECDSA-only device key — but note it produces
    /// <em>transferable</em> evidence: unlike a MAC, a signature can be shown to third parties afterwards,
    /// forever. Prefer <see cref="BuildWithMac"/> where the credential's key allows it.
    /// </remarks>
    public static async Task<byte[]> BuildWithSignatureAsync(
        HeldMdoc credential,
        IReadOnlyCollection<RequestedElement> approved,
        ProximitySessionTranscript transcript,
        DeviceSignerAsync signer,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(signer);

        var (disclosed, deviceNameSpacesBytes, deviceAuthentication) =
            Prepare(credential, approved, transcript);

        var protectedHeader = CoseSign1Builder.EncodeProtectedHeaderEs256();
        var sigStructure = CoseSign1Builder.BuildSigStructure(protectedHeader, deviceAuthentication);

        // The device signs the Sig_structure. On a phone this crosses the JS-interop boundary to a
        // non-extractable WebCrypto key, which is why it is async and why we never see the key.
        var rawSignature = await signer(sigStructure, ct).ConfigureAwait(false);

        var deviceSignature = CoseSign1Builder.BuildDetached(protectedHeader, rawSignature);

        return Assemble(credential, disclosed, deviceNameSpacesBytes,
            new DeviceAuth { DeviceSignature = System.Security.Cryptography.Cose.CoseMessage.DecodeSign1(deviceSignature) });
    }

    /// <summary>
    /// Common preparation: filter to the approved items, build the (empty) device namespaces, and construct
    /// the <c>DeviceAuthentication</c> bytes that get signed or MAC'd.
    /// </summary>
    private static (Dictionary<string, IReadOnlyList<IssuerSignedItemBytes>> Disclosed,
                    byte[] DeviceNameSpacesBytes,
                    byte[] DeviceAuthentication)
        Prepare(HeldMdoc credential, IReadOnlyCollection<RequestedElement> approved, ProximitySessionTranscript transcript)
    {
        var approvedByNamespace = approved
            .GroupBy(a => a.Namespace)
            .ToDictionary(g => g.Key, g => g.Select(a => a.ElementIdentifier).ToHashSet(StringComparer.Ordinal));

        var disclosed = new Dictionary<string, IReadOnlyList<IssuerSignedItemBytes>>(StringComparer.Ordinal);

        foreach (var (ns, items) in credential.IssuerSigned.NameSpaces)
        {
            if (!approvedByNamespace.TryGetValue(ns, out var approvedIds))
                continue;

            // RULE 1: keep the ORIGINAL IssuerSignedItemBytes objects — their TaggedBytes are spliced
            // verbatim by MdocCodec. Never rebuild an item from its decoded value.
            var kept = items.Where(i => approvedIds.Contains(i.Item.ElementIdentifier)).ToList();

            if (kept.Count > 0)
                disclosed[ns] = kept;
        }

        // DeviceNameSpaces carries device-signed (as opposed to issuer-signed) elements. We emit none — every
        // element we disclose is issuer-signed — but the empty map is still what gets authenticated, so it
        // must be present and byte-stable.
        var deviceNameSpacesBytes = MdocCbor.WrapTag24(MdocCbor.Encode(w =>
        {
            w.WriteStartMap(0);
            w.WriteEndMap();
        }));

        var deviceAuthentication = MdocCodec.BuildDeviceAuthentication(
            transcript.Encoded,        // the BARE array — not the tag-24 form (that one is for the HKDF salt)
            credential.DocType,
            deviceNameSpacesBytes);

        return (disclosed, deviceNameSpacesBytes, deviceAuthentication);
    }

    private static byte[] Assemble(
        HeldMdoc credential,
        Dictionary<string, IReadOnlyList<IssuerSignedItemBytes>> disclosed,
        byte[] deviceNameSpacesBytes,
        DeviceAuth deviceAuth)
    {
        var issuerSigned = new IssuerSigned
        {
            NameSpaces = disclosed,
            IssuerAuth = credential.IssuerSigned.IssuerAuth   // the issuer's signature, untouched
        };

        var document = new Document
        {
            DocType = credential.DocType,
            IssuerSigned = issuerSigned,
            DeviceSigned = new DeviceSigned
            {
                NameSpacesBytes = deviceNameSpacesBytes,
                DeviceAuth = deviceAuth
            }
        };

        return MdocCodec.EncodeDeviceResponse(new DeviceResponse
        {
            Documents = [document],
            Status = 0   // OK
        });
    }
}
