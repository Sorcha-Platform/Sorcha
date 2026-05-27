// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.ServiceClients.Register;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// Feature 138 US4 (T050) — a recovered blueprint whose content does not match the sealed
/// <c>ContentHash</c> is rejected and not stored.
/// </summary>
public sealed class BlueprintRecoveryProvenanceTests
{
    private const string GenuineBlueprint = """{"title":"Permit","participants":[],"actions":[]}""";

    [Fact]
    public void TryVerifyProvenance_TamperedContent_Rejected()
    {
        // The sealed hash is over the genuine blueprint…
        var sealedHash = BlueprintContentHash.Compute(GenuineBlueprint);
        // …but the content served has been tampered (an extra action injected).
        const string tampered = """{"title":"Permit","participants":[],"actions":[{"id":"evil"}]}""";

        var ok = BlueprintRecoveryService.TryVerifyProvenance(tampered, sealedHash, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("hash_mismatch");
    }

    [Fact]
    public void TryVerifyProvenance_MalformedContent_Rejected()
    {
        var sealedHash = BlueprintContentHash.Compute(GenuineBlueprint);

        var ok = BlueprintRecoveryService.TryVerifyProvenance("not-json{", sealedHash, out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("hash_mismatch");
    }

    [Fact]
    public void TryVerifyProvenance_WrongHashForGenuineContent_Rejected()
    {
        // Genuine content, but the claimed sealed hash is for something else.
        var ok = BlueprintRecoveryService.TryVerifyProvenance(
            GenuineBlueprint,
            contentHash: new string('a', 64),
            out var reason);

        ok.Should().BeFalse();
        reason.Should().Be("hash_mismatch");
    }
}
