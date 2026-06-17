// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Verifier.Engine.Models;
using Xunit;

namespace Sorcha.Verifier.Tests.Models;

/// <summary>
/// Feature 155 — the enriched <see cref="VerificationOutcome.Layers"/> contract. Asserts the field
/// defaults to empty (back-compat with existing construction sites) and survives a System.Text.Json
/// round-trip, since the verifier app's /status endpoint serialises it to the polling UI.
/// </summary>
public sealed class VerificationOutcomeLayersTests
{
    [Fact]
    public void VerificationOutcome_Layers_DefaultsToEmpty()
    {
        var outcome = new VerificationOutcome
        {
            Accepted = true,
            DisclosedClaims = new Dictionary<string, object?>(),
            Errors = [],
            CompletedAt = DateTimeOffset.UnixEpoch,
        };

        outcome.Layers.Should().NotBeNull();
        outcome.Layers.Should().BeEmpty();
    }

    [Fact]
    public void VerificationOutcome_WithLayers_RoundTripsThroughJson()
    {
        var outcome = new VerificationOutcome
        {
            Accepted = true,
            DisclosedClaims = new Dictionary<string, object?> { ["age_over_18"] = true },
            Errors = [],
            CompletedAt = DateTimeOffset.UnixEpoch,
            IssuerSignature = IssuerSignatureStatus.Verified,
            Layers =
            [
                new ValidationLayerResult
                {
                    Layer = ValidationLayer.Revocation,
                    Status = LayerStatus.Pass,
                    Headline = "Not revoked",
                    Detail = new Dictionary<string, string> { ["idx"] = "1842", ["status"] = "0 (valid)" },
                },
                new ValidationLayerResult
                {
                    Layer = ValidationLayer.RegisterAnchor,
                    Status = LayerStatus.Unverified,
                    Headline = "Anchor not found",
                },
            ],
        };

        var json = JsonSerializer.Serialize(outcome);
        var round = JsonSerializer.Deserialize<VerificationOutcome>(json);

        round.Should().NotBeNull();
        round!.Layers.Should().HaveCount(2);
        round.Layers[0].Layer.Should().Be(ValidationLayer.Revocation);
        round.Layers[0].Status.Should().Be(LayerStatus.Pass);
        round.Layers[0].Detail["idx"].Should().Be("1842");
        round.Layers[1].Layer.Should().Be(ValidationLayer.RegisterAnchor);
        round.Layers[1].Status.Should().Be(LayerStatus.Unverified);
        round.Layers[1].Detail.Should().BeEmpty();
    }
}
