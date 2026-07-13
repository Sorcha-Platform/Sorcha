// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography.Cose;

namespace Sorcha.Mdoc;

/// <summary>
/// A single issuer-signed data element (ISO 18013-5 <c>IssuerSignedItem</c>, feature 135):
/// <c>{ digestID, random, elementIdentifier, elementValue }</c>.
/// </summary>
public sealed class IssuerSignedItem
{
    /// <summary>Digest identifier linking this item to an MSO <c>valueDigests</c> entry.</summary>
    public uint DigestId { get; set; }

    /// <summary>Per-item random salt (≥16 bytes) so digests don't leak element values.</summary>
    public byte[] Random { get; set; } = [];

    /// <summary>The element name within its namespace (e.g. <c>family_name</c>).</summary>
    public string ElementIdentifier { get; set; } = string.Empty;

    /// <summary>The element value (CBOR-typed: string, number, bool, byte[], date, etc.).</summary>
    public object? ElementValue { get; set; }
}

/// <summary>
/// The tag-24-wrapped CBOR of an <see cref="IssuerSignedItem"/> (ISO <c>IssuerSignedItemBytes</c>).
/// <see cref="TaggedBytes"/> is the exact <c>#6.24(bstr)</c> encoding — the hash input for the MSO
/// <c>valueDigests</c>, preserved verbatim; never re-encode the decoded <see cref="Item"/>.
/// </summary>
public sealed class IssuerSignedItemBytes
{
    /// <summary>The verbatim tag-24 encoded bytes (the digest input).</summary>
    public required byte[] TaggedBytes { get; init; }

    /// <summary>The decoded item.</summary>
    public required IssuerSignedItem Item { get; init; }
}

/// <summary>
/// ISO 18013-5 <c>IssuerSigned</c>: the issuer's signed claims grouped by namespace plus the
/// COSE_Sign1 (<c>issuerAuth</c>) whose payload is the tag-24-wrapped <see cref="MobileSecurityObject"/>.
/// </summary>
public sealed class IssuerSigned
{
    /// <summary>Namespace → ordered issuer-signed items.</summary>
    public Dictionary<string, IReadOnlyList<IssuerSignedItemBytes>> NameSpaces { get; set; } = new();

    /// <summary>The COSE_Sign1 over the tag-24-wrapped MSO. Carries the issuer key via x5chain (label 33).</summary>
    public required CoseSign1Message IssuerAuth { get; init; }
}

/// <summary>MSO validity window.</summary>
public sealed class ValidityInfo
{
    public DateTimeOffset Signed { get; set; }
    public DateTimeOffset ValidFrom { get; set; }
    public DateTimeOffset ValidUntil { get; set; }
    public DateTimeOffset? ExpectedUpdate { get; set; }
}

/// <summary>MSO status reference (IETF token status list — same shape as the SD-JWT IETF status).</summary>
public sealed class MsoStatus
{
    public string Uri { get; set; } = string.Empty;
    public uint Idx { get; set; }
}

/// <summary>
/// ISO 18013-5 Mobile Security Object — the issuer-signed payload that binds the value digests to
/// the holder device key and the document type. Carried tag-24-wrapped inside <c>issuerAuth</c>.
/// </summary>
public sealed class MobileSecurityObject
{
    public string Version { get; set; } = "1.0";

    /// <summary>"SHA-256" | "SHA-384" | "SHA-512".</summary>
    public string DigestAlgorithm { get; set; } = "SHA-256";

    /// <summary>Namespace → (digestID → digest bytes).</summary>
    public Dictionary<string, Dictionary<uint, byte[]>> ValueDigests { get; set; } = new();

    /// <summary>The holder binding key as raw COSE_Key CBOR bytes (EC2/P-256 in v1).</summary>
    public byte[] DeviceKeyCose { get; set; } = [];

    public string DocType { get; set; } = string.Empty;

    public ValidityInfo ValidityInfo { get; set; } = new();

    /// <summary>Optional IETF status reference for revocation (resolved via the unified status checker).</summary>
    public MsoStatus? Status { get; set; }
}

/// <summary>
/// ISO 18013-5 <c>DeviceAuth</c>: a COSE_Sign1 device signature or a COSE_Mac0, over the detached
/// <c>DeviceAuthentication</c> payload (holder binding / freshness). v1 verifies the signature path
/// (the OpenID4VP online target); the MAC path is preserved as raw bytes but not verified — the BCL
/// has no COSE_Mac0 type, and OpenID4VP device auth is signature-based.
/// </summary>
public sealed class DeviceAuth
{
    public CoseSign1Message? DeviceSignature { get; set; }

    /// <summary>Raw COSE_Mac0 CBOR when MAC-based device auth was used. Not verified in v1.</summary>
    public byte[]? DeviceMacRaw { get; set; }
}

/// <summary>ISO 18013-5 <c>DeviceSigned</c>: the device namespaces (tag-24, usually empty) + device auth.</summary>
public sealed class DeviceSigned
{
    /// <summary>The verbatim tag-24-wrapped device namespaces bytes (the <c>DeviceNameSpacesBytes</c>).</summary>
    public byte[] NameSpacesBytes { get; set; } = [];

    public required DeviceAuth DeviceAuth { get; init; }
}

/// <summary>One document within a <see cref="DeviceResponse"/>.</summary>
public sealed class Document
{
    public string DocType { get; set; } = string.Empty;
    public required IssuerSigned IssuerSigned { get; init; }
    public required DeviceSigned DeviceSigned { get; init; }
}

/// <summary>ISO 18013-5 <c>DeviceResponse</c> — the OpenID4VP <c>vp_token</c> payload for mso_mdoc.</summary>
public sealed class DeviceResponse
{
    public string Version { get; set; } = "1.0";
    public List<Document> Documents { get; set; } = [];

    /// <summary>Overall response status (0 = OK).</summary>
    public uint Status { get; set; }
}
