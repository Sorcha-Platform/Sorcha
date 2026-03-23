// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Builds canonical content for vote signing and verification (SEC-AUDIT 4.1).
/// The canonical format ensures all validators sign the same deterministic content
/// for a given vote, preventing signature reuse across different contexts.
/// </summary>
public static class VoteSigningHelper
{
    /// <summary>
    /// Builds the canonical content that must be signed for a consensus vote.
    /// Format: SHA256("{DocketId}:{DocketHash}:{Approved}:{ValidatorId}")
    /// </summary>
    /// <param name="docketId">Docket being voted on</param>
    /// <param name="docketHash">Hash of the docket content</param>
    /// <param name="approved">Whether the vote is an approval</param>
    /// <param name="validatorId">Validator casting the vote</param>
    /// <returns>Hex-encoded SHA256 hash of the canonical vote content</returns>
    public static string BuildVoteSigningContent(string docketId, string docketHash, bool approved, string validatorId)
    {
        var canonical = $"{docketId}:{docketHash}:{approved}:{validatorId}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hash);
    }
}
