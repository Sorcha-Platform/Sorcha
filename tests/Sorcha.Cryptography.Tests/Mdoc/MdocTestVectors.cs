// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.Cose;
using System.Security.Cryptography.X509Certificates;

using Sorcha.Cryptography.Mdoc;
using Sorcha.Cryptography.Mdoc.Cbor;
using Sorcha.Cryptography.Mdoc.Cose;

namespace Sorcha.Cryptography.Tests.Mdoc;

/// <summary>
/// Builds real signed mdoc <see cref="DeviceResponse"/> test vectors (feature 135). No external EUDI
/// reference vector is available offline, so vectors are generated end-to-end here — issuer COSE_Sign1
/// over the tag-24-wrapped MSO with an x5chain, and a detached device COSE_Sign1 over the OpenID4VP
/// <c>DeviceAuthentication</c> payload — which still exercises the real cryptography the verifier checks.
/// A genuine PID known-answer vector is a follow-up (it would replace <see cref="BuildPidLike"/>).
/// </summary>
internal static class MdocTestVectors
{
    public const string PidDocType = "eu.europa.ec.eudi.pid.1";

    public sealed record BuiltMdoc(
        DeviceResponse Response,
        byte[] IssuerCertDer,
        ECDsa IssuerKey,
        ECDsa DeviceKey,
        byte[] SessionTranscript,
        string DocType,
        string ClientId,
        string Nonce,
        string ResponseUri,
        IReadOnlyDictionary<string, string> Elements);

    public static BuiltMdoc BuildPidLike(
        string docType = PidDocType,
        string clientId = "x509_san_dns:verifier.example.com",
        string nonce = "mdoc-test-nonce",
        string responseUri = "https://verifier.example.com/response",
        MsoStatus? status = null,
        IReadOnlyList<byte[]>? issuerChain = null)
    {
        var now = DateTimeOffset.UtcNow;

        // Issuer key + self-signed cert (the x5chain leaf).
        var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var certReq = new CertificateRequest("CN=Test PID Issuer", issuerKey, HashAlgorithmName.SHA256);
        using var issuerCert = certReq.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        var issuerCertDer = issuerCert.Export(X509ContentType.Cert);

        // Device (holder binding) key.
        var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deviceKeyCose = EncodeEc2CoseKey(deviceKey);

        // Issuer-signed items (the disclosed PID elements).
        var elements = new Dictionary<string, string>
        {
            ["family_name"] = "Andersson",
            ["given_name"] = "Anna",
            ["birth_date"] = "1985-03-30"
        };

        var items = new List<IssuerSignedItemBytes>();
        var digests = new Dictionary<uint, byte[]>();
        uint digestId = 0;
        foreach (var (id, value) in elements)
        {
            var item = new IssuerSignedItem
            {
                DigestId = digestId,
                Random = RandomNumberGenerator.GetBytes(16),
                ElementIdentifier = id,
                ElementValue = value
            };
            var tagged = MdocCbor.WrapTag24(MdocCodec.EncodeIssuerSignedItem(item));
            items.Add(new IssuerSignedItemBytes { TaggedBytes = tagged, Item = item });
            digests[digestId] = SHA256.HashData(tagged);
            digestId++;
        }

        // MSO over the value digests + device key, signed by the issuer (COSE_Sign1 with x5chain).
        var mso = new MobileSecurityObject
        {
            Version = "1.0",
            DigestAlgorithm = "SHA-256",
            ValueDigests = new Dictionary<string, Dictionary<uint, byte[]>> { [docType] = digests },
            DeviceKeyCose = deviceKeyCose,
            DocType = docType,
            ValidityInfo = new ValidityInfo { Signed = now, ValidFrom = now, ValidUntil = now.AddYears(1) },
            Status = status
        };
        var msoTagged = MdocCbor.WrapTag24(MdocCodec.EncodeMso(mso));

        var unprotected = new CoseHeaderMap { [CoseX5Chain.Label] = CoseX5Chain.Encode(issuerChain ?? [issuerCertDer]) };
        var issuerSigner = new CoseSigner(issuerKey, HashAlgorithmName.SHA256, protectedHeaders: null, unprotectedHeaders: unprotected);
        var issuerAuth = CoseMessage.DecodeSign1(CoseSign1Message.SignEmbedded(msoTagged, issuerSigner));

        var issuerSigned = new IssuerSigned
        {
            NameSpaces = new Dictionary<string, IReadOnlyList<IssuerSignedItemBytes>> { [docType] = items },
            IssuerAuth = issuerAuth
        };

        // Device auth: detached COSE_Sign1 over the OpenID4VP DeviceAuthentication payload.
        var deviceNameSpacesBytes = MdocCbor.WrapTag24(MdocCbor.Encode(w => { w.WriteStartMap(0); w.WriteEndMap(); }));
        var sessionTranscript = MdocCodec.BuildOpenId4VpSessionTranscript(clientId, nonce, jwkThumbprint: null, responseUri);
        var deviceAuthentication = MdocCodec.BuildDeviceAuthentication(sessionTranscript, docType, deviceNameSpacesBytes);
        var deviceSigner = new CoseSigner(deviceKey, HashAlgorithmName.SHA256);
        var deviceSignature = CoseMessage.DecodeSign1(CoseSign1Message.SignDetached(deviceAuthentication, deviceSigner));

        var deviceSigned = new DeviceSigned
        {
            NameSpacesBytes = deviceNameSpacesBytes,
            DeviceAuth = new DeviceAuth { DeviceSignature = deviceSignature }
        };

        var response = new DeviceResponse
        {
            Version = "1.0",
            Documents = [new Document { DocType = docType, IssuerSigned = issuerSigned, DeviceSigned = deviceSigned }],
            Status = 0
        };

        return new BuiltMdoc(response, issuerCertDer, issuerKey, deviceKey, sessionTranscript, docType, clientId, nonce, responseUri, elements);
    }

    /// <summary>Encodes an EC2 (P-256) public key as a COSE_Key CBOR map (kty=2, crv=1, x, y).</summary>
    public static byte[] EncodeEc2CoseKey(ECDsa key)
    {
        var p = key.ExportParameters(includePrivateParameters: false);
        return MdocCbor.Encode(w =>
        {
            w.WriteStartMap(4);
            w.WriteInt32(1); w.WriteInt32(2);   // kty: EC2
            w.WriteInt32(-1); w.WriteInt32(1);  // crv: P-256
            w.WriteInt32(-2); w.WriteByteString(p.Q.X!); // x
            w.WriteInt32(-3); w.WriteByteString(p.Q.Y!); // y
            w.WriteEndMap();
        });
    }
}
