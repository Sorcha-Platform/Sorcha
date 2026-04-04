// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Sorcha.Wallet.Core.Domain.Enums;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Builds Sorcha-specific BIP32 derivation paths for hierarchical deterministic key generation.
/// </summary>
/// <remarks>
/// Path structure: <c>m/SorchaPurpose'/orgHash'/deptId'/userHash'/usage/index</c>
/// where hardened levels use the apostrophe notation and the final two levels
/// (usage, index) are unhardened to allow extended public key derivation.
/// </remarks>
public static class DerivationPathBuilder
{
    /// <summary>Sorcha-specific BIP32 purpose value ("SOR" in hex: 0x53=S, 0x4F=O, 0x52=R).</summary>
    public const uint SorchaPurpose = 0x534F52; // decimal 5,456,978

    /// <summary>
    /// Builds a full Sorcha BIP32 derivation path for a specific key.
    /// </summary>
    /// <param name="orgId">The organization identifier.</param>
    /// <param name="departmentId">The department identifier within the organization.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="usage">The intended key usage (unhardened level).</param>
    /// <param name="index">The key index (unhardened level).</param>
    /// <returns>
    /// A derivation path string in the format
    /// <c>m/5456978'/orgHash'/deptId'/userHash'/usage/index</c>.
    /// </returns>
    public static string Build(Guid orgId, uint departmentId, Guid userId, KeyUsage usage, uint index)
    {
        var orgIndex = GuidToDerivationIndex(orgId);
        var userIndex = GuidToDerivationIndex(userId);

        return $"m/{SorchaPurpose}'/{orgIndex}'/{departmentId}'/{userIndex}'/{(uint)usage}/{index}";
    }

    /// <summary>
    /// Converts a <see cref="Guid"/> to a 31-bit derivation index by hashing with SHA-256.
    /// </summary>
    /// <param name="id">The GUID to convert.</param>
    /// <returns>A 31-bit unsigned integer suitable for use as a BIP32 derivation index.</returns>
    public static uint GuidToDerivationIndex(Guid id)
    {
        var hash = SHA256.HashData(id.ToByteArray());
        var value = BitConverter.ToUInt32(hash, 0);
        return value & 0x7FFFFFFF;
    }
}
