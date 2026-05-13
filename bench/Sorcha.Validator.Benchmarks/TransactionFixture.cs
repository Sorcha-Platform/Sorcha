// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Validator.Service.Models;

namespace Sorcha.Validator.Benchmarks;

/// <summary>
/// Builds realistic-shape transactions for micro-benchmarks. Mirrors the test
/// helpers in <c>ValidationEngineTests.CreateValidTransaction</c> but lives
/// outside the test project so we don't pull xunit into the benchmark binary.
/// </summary>
internal static class TransactionFixture
{
    private const string ShaOfEmptyObject =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public static Transaction Minimal()
    {
        return new Transaction
        {
            TransactionId = $"tx-{Guid.NewGuid():N}",
            RegisterId = "bench-register",
            BlueprintId = "bp-1",
            ActionId = "1",
            Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
            PayloadHash = ShaOfEmptyObject,
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new Signature
                {
                    PublicKey = new byte[32],
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow,
                }
            ],
        };
    }

    public static Transaction MediumPayload()
    {
        // Realistic blueprint-action shape: ~10 fields, ~400 bytes.
        const string payload = """
            {
              "applicantName": "Aoife O'Brien",
              "siteAddress": "12 Riverside Walk, Dublin 8, D08 X5R7",
              "projectType": "ResidentialExtension",
              "estimatedDuration": 90,
              "estimatedCost": 145000.00,
              "buildingHeightMeters": 4.5,
              "footprintSqMeters": 28,
              "neighbourConsents": ["consent-001", "consent-002"],
              "siteVisitRequested": true,
              "submittedAtIso": "2026-05-09T10:30:00Z"
            }
            """;
        var element = JsonSerializer.Deserialize<JsonElement>(payload);

        return new Transaction
        {
            TransactionId = $"tx-{Guid.NewGuid():N}",
            RegisterId = "bench-register",
            BlueprintId = "bp-permit-v3",
            ActionId = "2",
            Payload = element,
            // Hash isn't load-bearing for the benches that don't call ValidatePayloadHash;
            // benches that do call it provide a stub IHashProvider.
            PayloadHash = ShaOfEmptyObject,
            CreatedAt = DateTimeOffset.UtcNow,
            PreviousTransactionId = $"tx-{Guid.NewGuid():N}",
            Signatures =
            [
                new Signature
                {
                    PublicKey = new byte[32],
                    SignatureValue = new byte[64],
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow,
                }
            ],
        };
    }
}
