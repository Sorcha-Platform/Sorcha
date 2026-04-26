// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Constants;

/// <summary>
/// JWT audience identifiers issued for citizen-wallet contexts.
/// </summary>
public static class JwtAudiences
{
    /// <summary>
    /// Audience claim value present in JWTs scoped to the citizen wallet PWA.
    /// Required by all <c>/api/v1/wallet/*</c> and <c>/api/v1/me/devices/*</c>
    /// endpoints to refuse tokens issued for unrelated audiences.
    /// </summary>
    public const string CitizenWallet = "sorcha:citizen-wallet";
}
