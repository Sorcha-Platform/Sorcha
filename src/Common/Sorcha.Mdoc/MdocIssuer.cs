// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.Cose;

using Sorcha.Mdoc.Cbor;
using Sorcha.Mdoc.Cose;

namespace Sorcha.Mdoc;

/// <summary>
/// Builds (issues) an ISO 18013-5 mdoc credential (feature 135, US3): one issuer-signed item per
/// element under the document-type namespace, an MSO binding the value digests to the holder device
/// key, and a COSE_Sign1 (<c>issuerAuth</c>) over the tag-24-wrapped MSO with an optional x5chain
/// (label 33). mdoc is ES256/P-256 only at the format layer. The result round-trips through
/// <see cref="MdocService"/> once the holder wraps it in a presentation.
/// </summary>
public static class MdocIssuer
{
    /// <summary>
    /// Issues an <see cref="IssuerSigned"/> for <paramref name="docType"/> over the supplied
    /// <paramref name="elements"/> (flat namespace = docType), signed by the issuer key. When
    /// <paramref name="x5cChain"/> is supplied it is embedded in the issuerAuth label-33 header
    /// (required for the X.509 trust anchor; the register anchor is DID-resolved and carries none).
    /// </summary>
    public static IssuerSigned IssueIssuerSigned(
        string docType,
        IReadOnlyDictionary<string, object> elements,
        byte[] issuerPrivateKey,
        string algorithm,
        byte[] holderDeviceKeyCose,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        IReadOnlyList<byte[]>? x5cChain = null,
        MsoStatus? status = null,
        string digestAlgorithm = "SHA-256",
        DateTimeOffset? signedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(docType);
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(holderDeviceKeyCose);

        var items = new List<IssuerSignedItemBytes>();
        var digests = new Dictionary<uint, byte[]>();
        uint digestId = 0;
        foreach (var (name, value) in elements)
        {
            var item = new IssuerSignedItem
            {
                DigestId = digestId,
                Random = RandomNumberGenerator.GetBytes(16),
                ElementIdentifier = name,
                ElementValue = value
            };
            var tagged = MdocCbor.WrapTag24(MdocCodec.EncodeIssuerSignedItem(item));
            items.Add(new IssuerSignedItemBytes { TaggedBytes = tagged, Item = item });
            digests[digestId] = Hash(digestAlgorithm, tagged);
            digestId++;
        }

        var mso = new MobileSecurityObject
        {
            Version = "1.0",
            DigestAlgorithm = digestAlgorithm,
            ValueDigests = new Dictionary<string, Dictionary<uint, byte[]>> { [docType] = digests },
            DeviceKeyCose = holderDeviceKeyCose,
            DocType = docType,
            ValidityInfo = new ValidityInfo
            {
                Signed = signedAt ?? DateTimeOffset.UtcNow,
                ValidFrom = validFrom,
                ValidUntil = validUntil
            },
            Status = status
        };
        var msoTagged = MdocCbor.WrapTag24(MdocCodec.EncodeMso(mso));

        using var issuerKey = CreateIssuerKey(issuerPrivateKey, algorithm);
        var unprotected = new CoseHeaderMap();
        if (x5cChain is { Count: > 0 })
            unprotected[CoseX5Chain.Label] = CoseX5Chain.Encode(x5cChain);

        var signer = new CoseSigner(issuerKey, HashAlgorithmName.SHA256, protectedHeaders: null, unprotectedHeaders: unprotected);
        var issuerAuth = CoseMessage.DecodeSign1(CoseSign1Message.SignEmbedded(msoTagged, signer));

        return new IssuerSigned
        {
            NameSpaces = new Dictionary<string, IReadOnlyList<IssuerSignedItemBytes>> { [docType] = items },
            IssuerAuth = issuerAuth
        };
    }

    private static ECDsa CreateIssuerKey(byte[] privateKey, string algorithm)
    {
        var alg = algorithm.ToUpperInvariant();
        if (alg is not ("ES256" or "P-256" or "P256"))
            throw new NotSupportedException($"mdoc issuance is ES256/P-256 only; got '{algorithm}'.");

        var ecdsa = ECDsa.Create();
        ecdsa.ImportECPrivateKey(privateKey, out _);
        return ecdsa;
    }

    private static byte[] Hash(string algorithm, byte[] data) => algorithm.ToUpperInvariant() switch
    {
        "SHA-256" => SHA256.HashData(data),
        "SHA-384" => SHA384.HashData(data),
        "SHA-512" => SHA512.HashData(data),
        _ => throw new NotSupportedException($"Unsupported MSO digest algorithm '{algorithm}'.")
    };
}
