// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Prng;

namespace Sorcha.Cryptography.Core;

/// <summary>
/// Deterministic seed material for post-quantum key generation (#1689).
/// </summary>
/// <remarks>
/// <para>
/// A wallet's keys are re-derived from its recovery phrase — on recovery, and on every
/// derivation-path signature (<c>sorcha:docket-signing</c> and friends). Key generation for a derived
/// key must therefore be a FUNCTION of its seed. The PQC generators used a fresh
/// <c>SecureRandom</c> regardless, so a PQC wallet recovered to a different address and every
/// re-derived signing key differed from the one on the roster — silently.
/// </para>
/// <para>
/// The input is the BIP32-derived private key for the path. It is expanded with HKDF-SHA256 under a
/// per-algorithm info string so that no two algorithms (nor a classical key at the same path) ever
/// share raw key material. The info strings are part of the key-derivation contract: changing one
/// changes every key derived under it, so they are versioned rather than edited.
/// </para>
/// </remarks>
internal static class PqcSeedDerivation
{
    /// <summary>ML-DSA key generation seed length (FIPS 204 ξ).</summary>
    public const int MlDsaSeedLength = 32;

    /// <summary>ML-KEM key generation seed length (FIPS 203 d ‖ z).</summary>
    public const int MlKemSeedLength = 64;

    /// <summary>
    /// Expands <paramref name="derivedKey"/> into <paramref name="length"/> bytes of seed material for
    /// <paramref name="algorithm"/>.
    /// </summary>
    public static byte[] Expand(byte[] derivedKey, string algorithm, int length)
    {
        ArgumentNullException.ThrowIfNull(derivedKey);
        if (derivedKey.Length == 0)
            throw new ArgumentException("Seed cannot be empty", nameof(derivedKey));

        var info = Encoding.UTF8.GetBytes($"Sorcha PQC key generation v1|{algorithm}");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, derivedKey, length, salt: null, info: info);
    }

    /// <summary>
    /// A random source that yields exactly the bytes it was given and then refuses. Used to drive a
    /// key generator whose standard defines key generation as "draw these seeds" (FIPS 205
    /// SLH-DSA: SK.seed, SK.prf, PK.seed) but whose library exposes no from-seed constructor.
    /// </summary>
    /// <remarks>
    /// Over-consumption throws rather than repeating or zero-filling: a generator that drew more
    /// than the standard specifies has changed behaviour, and a key built from invented bytes would
    /// be exactly the silent failure this class exists to prevent. The caller additionally checks
    /// that the resulting key encodes the seeds it supplied.
    /// </remarks>
    public sealed class ExactBytesRandomGenerator(byte[] bytes) : IRandomGenerator
    {
        private readonly byte[] _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        private int _position;

        /// <summary>Seed material is fixed by construction; additions are ignored.</summary>
        public void AddSeedMaterial(byte[] seed) { }

        /// <summary>Seed material is fixed by construction; additions are ignored.</summary>
        public void AddSeedMaterial(ReadOnlySpan<byte> seed) { }

        /// <summary>Seed material is fixed by construction; additions are ignored.</summary>
        public void AddSeedMaterial(long seed) { }

        /// <inheritdoc />
        public void NextBytes(byte[] bytes) => NextBytes(bytes.AsSpan());

        /// <inheritdoc />
        public void NextBytes(byte[] bytes, int start, int len) => NextBytes(bytes.AsSpan(start, len));

        /// <inheritdoc />
        public void NextBytes(Span<byte> bytes)
        {
            if (_position + bytes.Length > _bytes.Length)
            {
                throw new InvalidOperationException(
                    $"Key generator requested more seed material than the standard specifies ({_bytes.Length} bytes).");
            }

            _bytes.AsSpan(_position, bytes.Length).CopyTo(bytes);
            _position += bytes.Length;
        }
    }
}
