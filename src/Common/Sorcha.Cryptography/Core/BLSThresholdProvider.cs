// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Nethermind.MclBindings;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Models;

namespace Sorcha.Cryptography.Core;

/// <summary>
/// Provides BLS12-381 threshold signature operations for distributed docket validation.
/// <para>Uses the min-sig scheme: public keys in G2 (96 bytes), signatures in G1 (48 bytes).
/// Threshold key generation uses Shamir's Secret Sharing over the scalar field (Fr),
/// and signature aggregation uses Lagrange interpolation on G1 points.</para>
/// </summary>
public sealed class BLSThresholdProvider : IDisposable
{
    /// <summary>BLS12-381 G1 compressed point size (signature).</summary>
    public const int SignatureSize = 48;

    /// <summary>BLS12-381 G2 compressed point size (public key).</summary>
    public const int PublicKeySize = 96;

    /// <summary>BLS12-381 Fr scalar size (secret key).</summary>
    public const int SecretKeySize = 32;

    private const int MCL_BLS12_381 = 5;
    private const int MCLBN_COMPILED_TIME_VAR = 46; // x64: FR=4, FP=6 → 4*10+6

    private static readonly object InitLock = new();
    private static bool _initialized;
    private bool _disposed;

    public BLSThresholdProvider()
    {
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (InitLock)
        {
            if (_initialized) return;
            var ret = Mcl.mclBn_init(MCL_BLS12_381, MCLBN_COMPILED_TIME_VAR);
            if (ret != 0)
                throw new InvalidOperationException($"MCL BLS12-381 initialization failed (error code: {ret})");
            Mcl.mclBn_setETHserialization(1);
            _initialized = true;
        }
    }

    /// <summary>
    /// Generates threshold key shares using Shamir's Secret Sharing.
    /// Returns a group public key and n individual key shares where any t shares can sign.
    /// </summary>
    public CryptoResult<BLSThresholdKeySet> GenerateThresholdKeyShares(
        uint threshold, uint totalSigners, string[] validatorIds)
    {
        if (threshold == 0)
            return CryptoResult<BLSThresholdKeySet>.Failure(CryptoStatus.InvalidParameter, "Threshold must be >= 1");
        if (totalSigners < threshold)
            return CryptoResult<BLSThresholdKeySet>.Failure(CryptoStatus.InvalidParameter, "TotalSigners must be >= threshold");
        if (validatorIds == null || validatorIds.Length != (int)totalSigners)
            return CryptoResult<BLSThresholdKeySet>.Failure(CryptoStatus.InvalidParameter, "ValidatorIds length must equal totalSigners");

        try
        {
            // Generate random polynomial coefficients and serialize them
            // f(x) = a0 + a1*x + ... + a(t-1)*x^(t-1) where a0 = master secret
            var coeffBytes = new byte[threshold][];
            for (int i = 0; i < (int)threshold; i++)
            {
                var coeff = new mclBnFr();
                Mcl.mclBnFr_setByCSPRNG(ref coeff);
                coeffBytes[i] = SerializeFr(ref coeff);
            }

            // Master public key = a0 * G2
            var masterSk = DeserializeFr(coeffBytes[0]);
            var g2Base = GetG2Generator();
            var masterPk = new mclBnG2();
            Mcl.mclBnG2_mul(ref masterPk, in g2Base, in masterSk);
            var groupPublicKey = SerializeG2(ref masterPk);

            // Generate key shares using Horner's method polynomial evaluation
            var keyShares = new List<BLSKeyShare>((int)totalSigners);
            for (uint i = 0; i < totalSigners; i++)
            {
                var shareIndex = i + 1; // 1-based

                // Evaluate f(shareIndex) using Horner's method
                var secretShareBytes = EvaluatePolynomialHorner(coeffBytes, shareIndex);

                // Public share = secretShare * G2
                var secretShare = DeserializeFr(secretShareBytes);
                var publicShare = new mclBnG2();
                Mcl.mclBnG2_mul(ref publicShare, in g2Base, in secretShare);

                keyShares.Add(new BLSKeyShare
                {
                    ValidatorId = validatorIds[i],
                    ShareIndex = shareIndex,
                    SecretShare = secretShareBytes,
                    PublicShare = SerializeG2(ref publicShare)
                });
            }

            // Zeroize polynomial coefficients
            foreach (var c in coeffBytes)
                CryptographicOperations.ZeroMemory(c);

            return CryptoResult<BLSThresholdKeySet>.Success(new BLSThresholdKeySet
            {
                GroupPublicKey = groupPublicKey,
                KeyShares = keyShares,
                Threshold = threshold,
                TotalSigners = totalSigners
            });
        }
        catch (Exception ex)
        {
            return CryptoResult<BLSThresholdKeySet>.Failure(CryptoStatus.KeyGenerationFailed,
                $"BLS threshold key generation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates a partial BLS signature on a docket hash using a validator's secret key share.
    /// </summary>
    public CryptoResult<byte[]> SignPartial(byte[] secretShare, byte[] docketHashBytes)
    {
        if (secretShare == null || secretShare.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidKey, "Secret share cannot be null or empty");
        if (docketHashBytes == null || docketHashBytes.Length == 0)
            return CryptoResult<byte[]>.Failure(CryptoStatus.InvalidParameter, "Docket hash cannot be null or empty");

        try
        {
            var sk = DeserializeFr(secretShare);
            var h = new mclBnG1();
            HashToG1(ref h, docketHashBytes);

            // Partial signature = sk * H(msg)
            var sig = new mclBnG1();
            Mcl.mclBnG1_mul(ref sig, in h, in sk);

            return CryptoResult<byte[]>.Success(SerializeG1(ref sig));
        }
        catch (Exception ex)
        {
            return CryptoResult<byte[]>.Failure(CryptoStatus.SigningFailed,
                $"BLS partial signing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Aggregates t partial signatures into a single BLS threshold signature
    /// using Lagrange interpolation at x=0.
    /// </summary>
    public BLSAggregationResult AggregateSignatures(
        byte[][] partialSignatures, uint[] shareIndices, uint threshold, uint totalSigners)
    {
        if (partialSignatures == null || partialSignatures.Length < (int)threshold)
            return BLSAggregationResult.Failure($"Need at least {threshold} partial signatures, got {partialSignatures?.Length ?? 0}");
        if (shareIndices == null || shareIndices.Length != partialSignatures.Length)
            return BLSAggregationResult.Failure("Share indices count must match partial signatures count");

        try
        {
            int k = partialSignatures.Length;

            // Manual Lagrange interpolation at x=0:
            // sig = sum_{i=0..k-1} L_i(0) * sig_i
            // where L_i(0) = product_{j!=i} (x_j / (x_j - x_i))

            // Accumulate the result in aggregated (G1 point)
            var aggregated = new mclBnG1();
            Mcl.mclBnG1_clear(ref aggregated);

            for (int i = 0; i < k; i++)
            {
                // Compute Lagrange coefficient L_i(0) in Fr
                var lagrangeCoeff = ComputeLagrangeCoefficient(shareIndices, i);

                // Deserialize partial signature
                var sigI = DeserializeG1(partialSignatures[i]);

                // Weighted point: L_i * sig_i
                var weighted = new mclBnG1();
                Mcl.mclBnG1_mul(ref weighted, in sigI, in lagrangeCoeff);

                // Accumulate: aggregated += weighted
                var tmp = new mclBnG1();
                Mcl.mclBnG1_add(ref tmp, in aggregated, in weighted);
                aggregated = tmp;
            }

            var bitfield = BuildSignerBitfield(shareIndices, totalSigners);

            return BLSAggregationResult.Success(new BLSAggregateSignature
            {
                Signature = SerializeG1(ref aggregated),
                SignerBitfield = bitfield,
                Threshold = threshold,
                TotalSigners = totalSigners
            });
        }
        catch (Exception ex)
        {
            return BLSAggregationResult.Failure($"BLS aggregation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies a BLS aggregate signature against the group public key.
    /// Uses the pairing check: e(sig, G2) == e(H(msg), groupPk).
    /// </summary>
    public CryptoResult<bool> VerifyAggregateSignature(
        byte[] signature, byte[] groupPublicKey, byte[] docketHashBytes)
    {
        if (signature == null || signature.Length == 0)
            return CryptoResult<bool>.Failure(CryptoStatus.InvalidParameter, "Signature cannot be null or empty");
        if (groupPublicKey == null || groupPublicKey.Length == 0)
            return CryptoResult<bool>.Failure(CryptoStatus.InvalidKey, "Group public key cannot be null or empty");
        if (docketHashBytes == null || docketHashBytes.Length == 0)
            return CryptoResult<bool>.Failure(CryptoStatus.InvalidParameter, "Docket hash cannot be null or empty");

        try
        {
            var sig = DeserializeG1(signature);
            var pk = DeserializeG2(groupPublicKey);
            var h = new mclBnG1();
            HashToG1(ref h, docketHashBytes);
            var g2 = GetG2Generator();

            // Verify: e(sig, G2) == e(H(msg), pk)
            var lhs = new mclBnGT();
            var rhs = new mclBnGT();
            Mcl.mclBn_pairing(ref lhs, in sig, in g2);
            Mcl.mclBn_pairing(ref rhs, in h, in pk);

            bool valid = Mcl.mclBnGT_isEqual(in lhs, in rhs) == 1;
            return CryptoResult<bool>.Success(valid);
        }
        catch (Exception ex)
        {
            return CryptoResult<bool>.Failure(CryptoStatus.InvalidSignature,
                $"BLS verification failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Evaluates polynomial f(x) = a0 + a1*x + ... + a(t-1)*x^(t-1) at the given point
    /// using Horner's method. All arithmetic is in the Fr scalar field.
    /// </summary>
    private static byte[] EvaluatePolynomialHorner(byte[][] coeffBytes, uint x)
    {
        var xFr = new mclBnFr();
        Mcl.mclBnFr_setInt32(ref xFr, (int)x);

        // Horner's method: start from highest coefficient
        // result = a(t-1)
        // for i = t-2 down to 0: result = result * x + a_i
        int t = coeffBytes.Length;
        var result = DeserializeFr(coeffBytes[t - 1]);

        for (int i = t - 2; i >= 0; i--)
        {
            // result = result * x
            var mulResult = new mclBnFr();
            Mcl.mclBnFr_mul(ref mulResult, in result, in xFr);

            // result = result + a_i
            var coeff = DeserializeFr(coeffBytes[i]);
            Mcl.mclBnFr_add(ref result, in mulResult, in coeff);
        }

        return SerializeFr(ref result);
    }

    /// <summary>
    /// Computes the Lagrange coefficient L_i(0) for share index i among the set of indices.
    /// L_i(0) = product_{j != i} (x_j / (x_j - x_i))
    /// </summary>
    private static mclBnFr ComputeLagrangeCoefficient(uint[] indices, int i)
    {
        var result = new mclBnFr();
        Mcl.mclBnFr_setInt32(ref result, 1); // Start with multiplicative identity

        var xi = new mclBnFr();
        Mcl.mclBnFr_setInt32(ref xi, (int)indices[i]);

        for (int j = 0; j < indices.Length; j++)
        {
            if (j == i) continue;

            var xj = new mclBnFr();
            Mcl.mclBnFr_setInt32(ref xj, (int)indices[j]);

            // numerator = x_j
            // denominator = x_j - x_i
            var denom = new mclBnFr();
            Mcl.mclBnFr_sub(ref denom, in xj, in xi);

            // fraction = x_j / (x_j - x_i)
            var fraction = new mclBnFr();
            Mcl.mclBnFr_div(ref fraction, in xj, in denom);

            // result *= fraction
            var tmp = new mclBnFr();
            Mcl.mclBnFr_mul(ref tmp, in result, in fraction);
            result = tmp;
        }

        return result;
    }

    private static byte[] SerializeG1(ref mclBnG1 point)
    {
        var buf = new byte[SignatureSize];
        unsafe
        {
            fixed (byte* p = buf)
            {
                var written = Mcl.mclBnG1_serialize((IntPtr)p, (UIntPtr)buf.Length, in point);
                if ((int)written == 0)
                    throw new InvalidOperationException("Failed to serialize G1 point");
            }
        }
        return buf;
    }

    private static mclBnG1 DeserializeG1(byte[] data)
    {
        var point = new mclBnG1();
        unsafe
        {
            fixed (byte* p = data)
            {
                var read = Mcl.mclBnG1_deserialize(ref point, (IntPtr)p, (UIntPtr)data.Length);
                if ((int)read == 0)
                    throw new InvalidOperationException("Failed to deserialize G1 point");
            }
        }
        return point;
    }

    private static byte[] SerializeG2(ref mclBnG2 point)
    {
        var buf = new byte[PublicKeySize];
        unsafe
        {
            fixed (byte* p = buf)
            {
                var written = Mcl.mclBnG2_serialize((IntPtr)p, (UIntPtr)buf.Length, in point);
                if ((int)written == 0)
                    throw new InvalidOperationException("Failed to serialize G2 point");
            }
        }
        return buf;
    }

    private static mclBnG2 DeserializeG2(byte[] data)
    {
        var point = new mclBnG2();
        unsafe
        {
            fixed (byte* p = data)
            {
                var read = Mcl.mclBnG2_deserialize(ref point, (IntPtr)p, (UIntPtr)data.Length);
                if ((int)read == 0)
                    throw new InvalidOperationException("Failed to deserialize G2 point");
            }
        }
        return point;
    }

    private static byte[] SerializeFr(ref mclBnFr scalar)
    {
        var buf = new byte[SecretKeySize];
        unsafe
        {
            fixed (byte* p = buf)
            {
                var written = Mcl.mclBnFr_serialize((IntPtr)p, (UIntPtr)buf.Length, in scalar);
                if ((int)written == 0)
                    throw new InvalidOperationException("Failed to serialize Fr scalar");
            }
        }
        return buf;
    }

    private static mclBnFr DeserializeFr(byte[] data)
    {
        var scalar = new mclBnFr();
        unsafe
        {
            fixed (byte* p = data)
            {
                var read = Mcl.mclBnFr_deserialize(ref scalar, (IntPtr)p, (UIntPtr)data.Length);
                if ((int)read == 0)
                    throw new InvalidOperationException("Failed to deserialize Fr scalar");
            }
        }
        return scalar;
    }

    private static void HashToG1(ref mclBnG1 point, byte[] data)
    {
        unsafe
        {
            fixed (byte* p = data)
            {
                Mcl.mclBnG1_hashAndMapTo(ref point, (IntPtr)p, (UIntPtr)data.Length);
            }
        }
    }

    private static mclBnG2 GetG2Generator()
    {
        // Derive a deterministic G2 generator by hashing a fixed DST string.
        // This is consistent across all calls within this provider.
        var g2 = new mclBnG2();
        unsafe
        {
            var dst = Encoding.UTF8.GetBytes("SORCHA_BLS_THRESHOLD_G2_GENERATOR_V1");
            fixed (byte* p = dst)
            {
                Mcl.mclBnG2_hashAndMapTo(ref g2, (IntPtr)p, (UIntPtr)dst.Length);
            }
        }
        return g2;
    }

    private static byte[] BuildSignerBitfield(uint[] shareIndices, uint totalSigners)
    {
        var byteCount = (int)((totalSigners + 7) / 8);
        var bitfield = new byte[byteCount];
        foreach (var idx in shareIndices)
        {
            if (idx >= 1 && idx <= totalSigners)
            {
                var bitPos = (int)(idx - 1);
                bitfield[bitPos / 8] |= (byte)(1 << (bitPos % 8));
            }
        }
        return bitfield;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
