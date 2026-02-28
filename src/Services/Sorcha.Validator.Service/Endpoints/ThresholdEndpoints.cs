// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Models;
using Sorcha.Validator.Service.Models;

namespace Sorcha.Validator.Service.Endpoints;

/// <summary>
/// API endpoints for BLS threshold signature setup and signing.
/// </summary>
public static class ThresholdEndpoints
{
    // In-memory store for threshold state (production would use distributed cache)
    private static readonly ConcurrentDictionary<string, ThresholdState> _thresholdStates = new();
    private static readonly ConcurrentDictionary<string, SignatureCollector> _signatureCollectors = new();

    public static RouteGroupBuilder MapThresholdEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/setup", SetupThreshold)
            .WithName("SetupThreshold")
            .WithSummary("Initialize BLS threshold signing for a register")
            .WithDescription("Generates threshold key shares using Shamir's Secret Sharing and distributes them to validators.");

        group.MapPost("/sign", SubmitPartialSignature)
            .WithName("SubmitThresholdSignature")
            .WithSummary("Submit a partial BLS signature for a docket")
            .WithDescription("Submits a validator's partial BLS signature share. When threshold is met, shares are aggregated into a single compact signature.");

        group.MapGet("/{registerId}/status", GetThresholdStatus)
            .WithName("GetThresholdStatus")
            .WithSummary("Get threshold signing status for a register")
            .WithDescription("Returns the current BLS threshold configuration and status for the specified register, including group public key, threshold value, validator count, and validator IDs.");

        return group;
    }

    private static IResult SetupThreshold(
        [FromBody] ThresholdSetupRequest request,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("ThresholdEndpoints");

        if (request.Threshold == 0 || request.TotalValidators == 0)
            return Results.BadRequest(new { Error = "Threshold and TotalValidators must be > 0" });
        if (request.Threshold > request.TotalValidators)
            return Results.BadRequest(new { Error = "Threshold must be <= TotalValidators" });
        if (request.ValidatorIds.Length != (int)request.TotalValidators)
            return Results.BadRequest(new { Error = "ValidatorIds count must equal TotalValidators" });

        using var provider = new BLSThresholdProvider();
        var keyResult = provider.GenerateThresholdKeyShares(
            request.Threshold, request.TotalValidators, request.ValidatorIds);

        if (!keyResult.IsSuccess)
        {
            logger.LogError("BLS threshold key generation failed: {Error}", keyResult.ErrorMessage);
            return Results.Problem(keyResult.ErrorMessage ?? "Key generation failed");
        }

        var keySet = keyResult.Value!;

        var state = new ThresholdState
        {
            RegisterId = request.RegisterId,
            GroupPublicKey = keySet.GroupPublicKey,
            Threshold = request.Threshold,
            TotalValidators = request.TotalValidators,
            KeyShares = keySet.KeyShares.ToDictionary(ks => ks.ValidatorId, ks => ks)
        };
        _thresholdStates[request.RegisterId] = state;

        logger.LogInformation(
            "BLS threshold initialized for register {RegisterId}: {T}-of-{N}",
            request.RegisterId, request.Threshold, request.TotalValidators);

        return Results.Ok(new ThresholdSetupResponse
        {
            RegisterId = request.RegisterId,
            GroupPublicKey = Convert.ToBase64String(keySet.GroupPublicKey),
            Threshold = request.Threshold,
            TotalValidators = request.TotalValidators,
            Status = "Threshold key shares generated. Distribute shares to validators via secure channel."
        });
    }

    private static IResult SubmitPartialSignature(
        [FromBody] ThresholdSignRequest request,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("ThresholdEndpoints");

        if (!_thresholdStates.TryGetValue(request.RegisterId, out var state))
            return Results.NotFound(new { Error = $"No threshold configuration for register {request.RegisterId}" });

        var collectorKey = $"{request.RegisterId}:{request.DocketHash}";
        var collector = _signatureCollectors.GetOrAdd(collectorKey, _ => new SignatureCollector
        {
            DocketHash = request.DocketHash,
            Threshold = state.Threshold,
            GroupPublicKey = state.GroupPublicKey,
            TotalSigners = state.TotalValidators
        });

        byte[] partialSig;
        try
        {
            partialSig = Convert.FromBase64String(request.PartialSignature);
        }
        catch (FormatException)
        {
            return Results.BadRequest(new { Error = "PartialSignature must be valid base64" });
        }

        collector.AddShare(request.ShareIndex, request.ValidatorId, partialSig);

        logger.LogDebug(
            "Partial signature received from {ValidatorId} for docket {DocketHash}: {Collected}/{Threshold}",
            request.ValidatorId, request.DocketHash, collector.CollectedCount, state.Threshold);

        if (collector.CollectedCount >= (int)state.Threshold)
        {
            using var provider = new BLSThresholdProvider();
            var aggResult = provider.AggregateSignatures(
                collector.GetPartialSignatures(),
                collector.GetShareIndices(),
                state.Threshold,
                state.TotalValidators);

            if (aggResult.IsSuccess)
            {
                logger.LogInformation(
                    "BLS threshold met for docket {DocketHash}: aggregate signature produced",
                    request.DocketHash);

                return Results.Ok(new ThresholdSignResponse
                {
                    DocketHash = request.DocketHash,
                    CollectedShares = collector.CollectedCount,
                    Threshold = state.Threshold,
                    ThresholdMet = true,
                    AggregateSignature = Convert.ToBase64String(aggResult.AggregateSignature!.Signature)
                });
            }

            logger.LogWarning("BLS aggregation failed for docket {DocketHash}: {Error}",
                request.DocketHash, aggResult.ErrorMessage);
        }

        return Results.Ok(new ThresholdSignResponse
        {
            DocketHash = request.DocketHash,
            CollectedShares = collector.CollectedCount,
            Threshold = state.Threshold,
            ThresholdMet = false
        });
    }

    private static IResult GetThresholdStatus(string registerId)
    {
        if (!_thresholdStates.TryGetValue(registerId, out var state))
            return Results.NotFound(new { Error = $"No threshold configuration for register {registerId}" });

        return Results.Ok(new
        {
            RegisterId = registerId,
            GroupPublicKey = Convert.ToBase64String(state.GroupPublicKey),
            Threshold = state.Threshold,
            TotalValidators = state.TotalValidators,
            ValidatorIds = state.KeyShares.Keys.ToArray()
        });
    }


    private class ThresholdState
    {
        public required string RegisterId { get; init; }
        public required byte[] GroupPublicKey { get; init; }
        public required uint Threshold { get; init; }
        public required uint TotalValidators { get; init; }
        public required Dictionary<string, BLSKeyShare> KeyShares { get; init; }
    }

    private class SignatureCollector
    {
        public required string DocketHash { get; init; }
        public required uint Threshold { get; init; }
        public required byte[] GroupPublicKey { get; init; }
        public required uint TotalSigners { get; init; }

        private readonly ConcurrentDictionary<uint, (string ValidatorId, byte[] Signature)> _shares = new();

        public int CollectedCount => _shares.Count;

        public void AddShare(uint shareIndex, string validatorId, byte[] partialSignature)
        {
            _shares[shareIndex] = (validatorId, partialSignature);
        }

        public byte[][] GetPartialSignatures() =>
            _shares.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value.Signature).ToArray();

        public uint[] GetShareIndices() =>
            _shares.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Key).ToArray();
    }

}
