// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Models;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Shared builder for Feature 138 US5 presentation-replay tests. Wires a
/// <see cref="VerifiablePresentationValidator"/> with a stub status-list cache (so the test can
/// isolate KB-JWT freshness from revocation) and the standard verifier session.
/// </summary>
internal static class VpValidatorTestHarness
{
    public const string Vct = "https://sorcha.dev/vc/test/v1";
    public const string Nonce = "verifier-nonce-us5";
    public const string ClientId = "did:sorcha:verifier:00000000000000000000000000000001";

    public static VerifiablePresentationValidator BuildValidator(
        StatusListVerdict statusVerdict = StatusListVerdict.Active,
        TimeProvider? clock = null,
        TimeSpan? clockSkew = null,
        TimeSpan? kbJwtMaxLifetime = null)
    {
        var statusList = new Mock<IStatusListCache>();
        statusList
            .Setup(s => s.CheckAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(statusVerdict);

        return new VerifiablePresentationValidator(
            statusList.Object,
            new OptOutIssuerKeyResolver(),
            clock ?? TimeProvider.System,
            NullLogger<VerifiablePresentationValidator>.Instance,
            requireIssuerSignature: false,
            metrics: null,
            clockSkew: clockSkew,
            kbJwtMaxLifetime: kbJwtMaxLifetime);
    }

    public static VerifierSession Session() => new()
    {
        SessionId = "sess-us5",
        ClientId = ClientId,
        Nonce = Nonce,
        RequiredVct = Vct,
        RequiredClaims = ["givenName"],
        OptionalClaims = [],
        Purpose = "test",
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
    };

    public static Dictionary<string, JsonElement> Claims(params (string Name, string Value)[] pairs)
    {
        var d = new Dictionary<string, JsonElement>();
        foreach (var (n, v) in pairs)
            d[n] = JsonSerializer.SerializeToElement(v);
        return d;
    }
}
