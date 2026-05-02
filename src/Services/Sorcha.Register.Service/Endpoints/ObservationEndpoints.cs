// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Core.LocalRelationship;
using Sorcha.Register.Core.Observations;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models.Observations;

namespace Sorcha.Register.Service.Endpoints;

/// <summary>
/// Feature 108 — internal endpoints that Peer.Service and Validator.Service POST to
/// when they observe a peer advert or seal a docket respectively. All endpoints require
/// the <c>CanReportRegisterObservation</c> service-to-service policy.
/// </summary>
public static class ObservationEndpoints
{
    /// <summary>Maximum clock skew tolerated on <c>ObservedAt</c> before the observation is rejected.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapObservationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/internal/registers/{registerId}/peer-height-observation", async (
            string registerId,
            PeerHeightObservation payload,
            IObservationStore store,
            IReadOnlyRegisterRepository registers,
            ILogger<PeerHeightObservation> logger,
            CancellationToken ct) =>
        {
            if (!string.Equals(payload.RegisterId, registerId, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Path registerId does not match payload.RegisterId" });

            if (payload.NetworkHeight < 0)
                return Results.BadRequest(new { error = "NetworkHeight must be >= 0" });

            var now = DateTimeOffset.UtcNow;
            if ((payload.ObservedAt - now).Duration() > MaxClockSkew)
                return Results.BadRequest(new { error = $"ObservedAt exceeds ±{MaxClockSkew.TotalSeconds:F0}s clock skew tolerance" });

            var register = await registers.GetRegisterAsync(registerId, ct);
            if (register is null) return Results.NotFound(new { error = $"Register '{registerId}' not known locally" });

            store.RecordPeerHeight(payload);
            logger.LogDebug(
                "Recorded peer height observation: register={RegisterId} peer={SourcePeerId} networkHeight={NetworkHeight}",
                registerId, payload.SourcePeerId, payload.NetworkHeight);

            return Results.Accepted();
        })
        .WithName("ReportPeerHeightObservation")
        .WithTags("Registers — Feature 108")
        .WithSummary("Record a peer's advert-height claim for a register (Feature 108)")
        .WithDescription("Internal service-to-service endpoint called by Peer.Service when it observes a remote peer advertising a docket height for a register. The observation feeds the sync-state resolver so the local node can tell whether it is at network head or trailing. Rejects observations more than ±5 minutes from local clock to bound skew. Not part of the public API surface.")
        .RequireAuthorization(AuthorizationPolicies.CanReportRegisterObservation)
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound)
        .ExcludeFromDescription();

        app.MapPost("/api/internal/registers/{registerId}/validator-observation", async (
            HttpContext ctx,
            string registerId,
            ValidatorSealingObservation payload,
            IObservationStore store,
            IRegisterLocalRelationshipService relationshipService,
            IReadOnlyRegisterRepository registers,
            ILogger<ValidatorSealingObservation> logger,
            CancellationToken ct) =>
        {
            if (!string.Equals(payload.RegisterId, registerId, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "Path registerId does not match payload.RegisterId" });

            if (payload.LastSealedHeight < 0 || payload.MempoolDepth < 0)
                return Results.BadRequest(new { error = "Numeric fields must be >= 0" });

            var register = await registers.GetRegisterAsync(registerId, ct);
            if (register is null) return Results.NotFound(new { error = $"Register '{registerId}' not known locally" });

            // Gate: caller must be on the roster — provide validator key via X-Validator-Public-Key header.
            if (!ctx.Request.Headers.TryGetValue("X-Validator-Public-Key", out var keyHeader) ||
                keyHeader.Count == 0 || string.IsNullOrWhiteSpace(keyHeader[0]))
            {
                return Results.BadRequest(new { error = "X-Validator-Public-Key header is required" });
            }

            byte[] validatorKey;
            try { validatorKey = Convert.FromBase64String(keyHeader[0]!); }
            catch (FormatException) { return Results.BadRequest(new { error = "X-Validator-Public-Key is not valid Base64" }); }

            var all = await relationshipService.DeriveAllAsync(validatorKey, ct);
            var isValidator = all.Any(r => r.RegisterId == registerId && r.IsValidator);
            if (!isValidator)
            {
                logger.LogWarning(
                    "Rejected validator sealing observation for register {RegisterId}: caller's key is not on the roster",
                    registerId);
                return Results.Forbid();
            }

            store.RecordValidatorSealing(payload);
            logger.LogDebug(
                "Recorded validator sealing observation: register={RegisterId} lastSealed={Height} mempool={Depth}",
                registerId, payload.LastSealedHeight, payload.MempoolDepth);

            return Results.Accepted();
        })
        .WithName("ReportValidatorSealingObservation")
        .WithTags("Registers — Feature 108")
        .WithSummary("Record the local validator's sealing progress for a register (Feature 108)")
        .WithDescription("Internal service-to-service endpoint called by Validator.Service when it seals a docket. Records the last sealed height and current mempool depth so the sync-state resolver knows the local node's contribution to network progress. The caller must present its validator public key via the X-Validator-Public-Key header and must be on the register's roster. Not part of the public API surface.")
        .RequireAuthorization(AuthorizationPolicies.CanReportRegisterObservation)
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .ExcludeFromDescription();

        return app;
    }
}
