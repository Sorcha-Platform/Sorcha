// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Services;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.StatusList;

/// <summary>
/// Tests that the W3C and IETF envelopes decompress to byte-identical raw bitstrings
/// from the same backing source (FR-011, FR-012).
/// </summary>
public class StatusListDualEnvelopeIdentityTests
{
    private readonly IetfTokenStatusListSerializer _ietfSerializer = new();

    private static (byte[] privateKey, byte[] publicKey) GenerateP256KeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportECPrivateKey(), ecdsa.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void DualEnvelope_EmptyList_DecompressedBytesMatch()
    {
        var list = BitstringStatusList.Create("issuer1", "register1", "revocation");
        AssertBitstringIdentity(list);
    }

    [Fact]
    public void DualEnvelope_AfterAllocation_DecompressedBytesMatch()
    {
        var list = BitstringStatusList.Create("issuer1", "register1", "revocation");
        list.AllocateIndex(); // index 0
        list.AllocateIndex(); // index 1
        list.AllocateIndex(); // index 2
        AssertBitstringIdentity(list);
    }

    [Fact]
    public void DualEnvelope_AfterRevocation_DecompressedBytesMatch()
    {
        var list = BitstringStatusList.Create("issuer1", "register1", "revocation");
        list.AllocateIndex();
        list.AllocateIndex();
        list.SetBit(0, true); // revoke first credential
        AssertBitstringIdentity(list);
    }

    [Fact]
    public void DualEnvelope_AfterReinstate_DecompressedBytesMatch()
    {
        var list = BitstringStatusList.Create("issuer1", "register1", "revocation");
        list.AllocateIndex();
        list.SetBit(0, true);  // revoke
        list.SetBit(0, false); // reinstate
        AssertBitstringIdentity(list);
    }

    [Fact]
    public void DualEnvelope_MassAllocationToCapacity_DecompressedBytesMatch()
    {
        // Use a smaller list for test speed (minimum allowed size)
        var list = BitstringStatusList.Create("issuer1", "register1", "revocation");
        // Allocate a bunch (not full capacity — that would be 131072 and slow)
        for (int i = 0; i < 1000; i++)
            list.AllocateIndex();

        // Set some bits
        list.SetBit(0, true);
        list.SetBit(42, true);
        list.SetBit(999, true);

        AssertBitstringIdentity(list);
    }

    /// <summary>
    /// Decompresses both the W3C (GZip+Base64) and IETF (ZLib+Base64URL) representations
    /// of the same list and asserts they produce identical raw bytes.
    /// </summary>
    private void AssertBitstringIdentity(BitstringStatusList list)
    {
        // W3C path: GZip-compressed Base64 from the model's EncodedList
        var w3cRawBytes = BitstringStatusList.DecompressBitstring(list.EncodedList);

        // IETF path: serialize to JWT, extract lst, ZLib-decompress
        var (privateKey, _) = GenerateP256KeyPair();
        var jwt = _ietfSerializer.Serialize(
            w3cRawBytes, list.Id, $"did:sorcha:org:{list.IssuerWallet}",
            bitsPerEntry: 1, signingKey: privateKey, algorithm: "ES256");

        var payloadBytes = Base64Url.DecodeFromChars(jwt.Split('.')[1]);
        var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payloadBytes)!;
        var lst = payload["status_list"].GetProperty("lst").GetString()!;

        var ietfCompressed = Base64Url.DecodeFromChars(lst);
        var ietfRawBytes = IetfTokenStatusListSerializer.ZLibDecompress(ietfCompressed);

        // The two decompressed bitstrings must be byte-for-byte identical
        ietfRawBytes.Should().BeEquivalentTo(w3cRawBytes,
            because: "W3C and IETF envelopes must decompress to the same raw bitstring");
    }
}
