// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Moq;
using Sorcha.Blueprint.Models.Canonical;
using Sorcha.ServiceClients.Register;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// A stand-in Register Service client that assigns publication ids the way the real one does
/// (Feature 195).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why tests now need this at all.</b> A definition's identity is assigned by the register, so
/// <c>PublishService</c> can no longer record a published definition without one — there would be
/// nothing to record it AS. Publishing with no register client is not a degraded mode, it is a
/// definition that exists on one node and nowhere else: resolvable here, unresolvable everywhere
/// else, and indistinguishable from a healthy publish until something needs it.
/// </para>
/// <para>
/// <b>It computes the real id rather than returning a placeholder.</b> A fake returning
/// <c>"tx-1"</c> would let a test pass while the definition it stored could never be verified by
/// recovery, which recomputes the id from the bytes. Tests that assert on identity therefore get the
/// same answer production would.
/// </para>
/// </remarks>
internal static class FakePublishingRegister
{
    /// <summary>
    /// A mock whose publish call assigns the genuine publication id for the definition it is given.
    /// </summary>
    internal static Mock<IRegisterServiceClient> Mock()
    {
        var mock = new Mock<IRegisterServiceClient>();

        mock.Setup(c => c.PublishBlueprintToRegisterAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string registerId, string blueprintId, string json, string _, CancellationToken _) =>
                new BlueprintPublicationResult
                {
                    PublicationTxId = BlueprintPublicationId.ComputeFromDefinition(registerId, blueprintId, json)
                });

        return mock;
    }

    /// <summary>Convenience for the many call sites that only need the object.</summary>
    internal static IRegisterServiceClient Client() => Mock().Object;
}
