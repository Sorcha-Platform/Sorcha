// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.PresentationLifecycle.Abstractions;

/// <summary>
/// Context passed to an <see cref="IPresentationConsumer"/> when its verifier
/// callback is received. Carries only fields the consumer needs to verify the
/// payload — not the full pending-presentation state held in Blueprint Service.
/// </summary>
/// <param name="PresentationRequestId">Unique id for this attempt; same value
/// appeared in the PresentationInitiated transaction.</param>
/// <param name="InstanceId">Blueprint instance the attempt belongs to.</param>
/// <param name="ActionId">Action within the instance being responded to.</param>
/// <param name="RegisterId">Register owning the instance and the lifecycle transactions.</param>
/// <param name="BlueprintId">Published blueprint id.</param>
/// <param name="SubmitterWallet">Wallet that submitted the original attempt.</param>
/// <param name="RequirementsDigest">SHA-256 of the action's canonical credential
/// requirements at the time of submission. Consumers can use this to detect
/// blueprint drift between submission and callback.</param>
/// <param name="InitiatedAt">When the attempt was recorded on the register.</param>
/// <param name="VerifierClientId">Canonical DID of the verifier (the org operating
/// the presentation request, e.g. the council page) — <c>did:sorcha:org:{walletAddress}</c>.
/// Resolved by the lifecycle service from the blueprint's owning organisation (Spec 5).
/// Null when the org has no published DID document or resolution fails; consumers
/// fall back to a placeholder. Used as the OID4VP <c>client_id</c> (display identity
/// in the current unsigned-request flow).</param>
public sealed record PresentationInitiationContext(
    Guid PresentationRequestId,
    Guid InstanceId,
    int ActionId,
    string RegisterId,
    string BlueprintId,
    string SubmitterWallet,
    byte[] RequirementsDigest,
    DateTimeOffset InitiatedAt,
    string? VerifierClientId = null);
