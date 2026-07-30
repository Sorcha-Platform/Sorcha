// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.GrpcServices;

/// <summary>
/// Which validator gRPC methods an <b>unauthenticated</b> caller may invoke.
///
/// <para><b>Why not simply require a token for everything.</b> Validator-to-validator consensus is
/// federated across <em>installations</em>, and Sorcha's service tokens are installation-scoped by
/// design (Feature 136: issuer <c>urn:sorcha:{installation}</c>, audience
/// <c>{installation}:service</c>, and bearer validation deliberately rejects another
/// installation's tokens). A blanket authorization requirement would therefore not merely create a
/// rolling-deploy window — it would <b>permanently sever consensus between installations</b>. The
/// same reasoning is already encoded in <c>PeerAuthInterceptor</c>, which validates a token when one
/// is present and otherwise lets the peer through "with lower trust" (FR-014), and sets
/// <c>ValidateAudience = false</c> precisely because "peer-to-peer traffic may not have audience
/// set".</para>
///
/// <para><b>What actually authenticates consensus.</b> Not the transport — the payload. Votes,
/// signatures and dockets carry signatures that are verified against the <em>validator roster</em>
/// downstream (<c>ConsensusEngine.CollectVoteFromValidatorAsync</c> resolves the voter via
/// <c>IValidatorRegistry.GetValidatorAsync</c>; <c>DocketConfirmer</c> calls
/// <c>IValidatorRegistry.IsRegisteredAsync</c>). The roster is installation-neutral, which is
/// exactly why it — and not an installation-scoped JWT — is the right trust anchor here. A forged
/// vote or docket from a stranger fails roster verification regardless of transport auth.</para>
///
/// <para><b>So the residual risk is not forgery.</b> It is (a) methods that mutate local state
/// without a roster-verified payload, and (b) resource exhaustion. This policy therefore opens only
/// the federation-necessary surface and closes the rest.</para>
///
/// <para><b>Fails closed.</b> Any method not explicitly listed as federation-reachable requires
/// authentication, so a newly added RPC is private by default rather than silently exposed. The
/// reflection guard in <c>ValidatorGrpcAccessPolicyTests</c> additionally forces every RPC on the
/// generated service base to be a deliberate decision.</para>
/// </summary>
public static class ValidatorGrpcAccessPolicy
{
    /// <summary>
    /// Methods an unauthenticated (or foreign-installation) caller may invoke.
    /// Every one of these is either read-only or carries a roster-verified signature in its payload.
    /// </summary>
    private static readonly HashSet<string> FederationReachableMethods = new(StringComparer.Ordinal)
    {
        // Consensus voting — the vote's signature is resolved against the roster by ConsensusEngine.
        "RequestVote",

        // Peer docket validation — read-only with respect to the chain; validates and answers.
        "ValidateDocket",

        // Consensus signature exchange — SignatureCollector rejects duplicates and invalid entries.
        "ExchangeSignature",

        // Confirmed-docket delivery — DocketConfirmer checks the initiator IS a registered
        // validator for the register before accepting it.
        "ReceiveConfirmedDocket",

        // Liveness. Deliberately open: federated peers legitimately probe reachability, and a
        // validator that cannot be liveness-checked cannot be federated with. Carries status only.
        "GetHealthStatus",
    };

    /// <summary>
    /// Methods that are intra-installation only and therefore require authentication.
    /// Listed explicitly (rather than merely omitted) so the reasoning is recorded next to the name.
    /// </summary>
    private static readonly Dictionary<string, string> AuthenticatedOnlyMethods = new(StringComparer.Ordinal)
    {
        // Mempool ingest. Unlike the consensus methods there is no roster signature gating entry —
        // admission is decided by transaction validation alone — so an open endpoint is an
        // unbounded invitation to spend this node's validation budget. It also has no
        // cross-installation caller: the only in-repo gRPC clients are ConsensusEngine and
        // SignatureCollector, neither of which calls it, and cross-node transaction distribution
        // goes through the Peer service instead.
        ["ReceiveTransaction"] = "mempool ingest is intra-installation only and has no roster gate",
    };

    /// <summary>
    /// True when <paramref name="method"/> may be invoked without authentication.
    /// Accepts either a bare method name or a full gRPC path
    /// (<c>/sorcha.validator.v1.ValidatorService/RequestVote</c>).
    /// </summary>
    public static bool IsFederationReachable(string method) =>
        FederationReachableMethods.Contains(BareMethodName(method));

    /// <summary>
    /// The recorded reason a method is authenticated-only, or <c>null</c> when it is not in that set.
    /// </summary>
    public static string? AuthenticatedOnlyReason(string method) =>
        AuthenticatedOnlyMethods.TryGetValue(BareMethodName(method), out var reason) ? reason : null;

    /// <summary>Every method name this policy has an explicit decision for.</summary>
    public static IReadOnlyCollection<string> ClassifiedMethods =>
        [.. FederationReachableMethods, .. AuthenticatedOnlyMethods.Keys];

    /// <summary>Strips the gRPC service path, leaving the bare method name.</summary>
    private static string BareMethodName(string method)
    {
        if (string.IsNullOrEmpty(method)) return string.Empty;

        var slash = method.LastIndexOf('/');
        return slash >= 0 && slash < method.Length - 1 ? method[(slash + 1)..] : method;
    }
}
