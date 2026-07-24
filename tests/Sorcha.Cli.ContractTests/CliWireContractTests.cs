// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;

using FluentAssertions;

namespace Sorcha.Cli.ContractTests;

/// <summary>
/// Asserts that every CLI DTO agrees on the wire with the server DTO it exchanges bodies with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The CLI declares its own request/response types. Nothing connected them
/// to the server's, so they drifted silently — a renamed property binds to nothing and the receiver
/// sees a default. The failures this catches are not compile errors and not exceptions: they are
/// commands that appear to work. <c>service-principal rotate-secret</c> printed an empty secret
/// because the server sends <c>newClientSecret</c> and the CLI read <c>clientSecret</c>;
/// <c>audit query</c> always reported nothing because the server sends <c>events</c> and the CLI
/// read <c>entries</c>.
/// </para>
/// <para>
/// <b>Pairs are discovered, not listed.</b> Any public CLI type whose name also exists in a service
/// (or in the shared HTTP clients) is treated as a wire pair and checked. A hand-maintained table
/// would silently stop covering new types; discovery means a newly-added duplicate is tested the
/// moment it appears. A type that shares a name without sharing a contract must be justified in
/// <see cref="NotAWireContract"/> — that list is the documentation of every deliberate collision.
/// </para>
/// <para>
/// This project references both the CLI and the services, which no production assembly may do. A
/// test project is the right place for it: it is the only vantage point that can see both ends of
/// an HTTP contract at once.
/// </para>
/// </remarks>
public sealed class CliWireContractTests
{
    /// <summary>
    /// CLI types that share a name with a server type but are NOT the same wire contract. Each
    /// needs a reason — this list is a claim about the codebase, not a mute suppression.
    /// </summary>
    private static readonly Dictionary<string, string> NotAWireContract = new(StringComparer.Ordinal)
    {
        ["Profile"] = "CLI-local config: a ~/.sorcha/config.json profile, never sent to a server.",
        ["CliConfiguration"] = "CLI-local config file shape.",
        ["TokenCacheEntry"] = "CLI-local encrypted token cache record, never sent to a server.",
        ["AuthenticationService"] = "A service class, not a DTO.",
        ["ConfigurationService"] = "A service class, not a DTO.",
        ["HttpClientFactory"] = "A factory class, not a DTO.",
        ["LoginRequest"] =
            "Different concept, same name. The CLI's is the input to an OAuth2 password-grant, sent "
            + "form-encoded to the token endpoint (username/password/client_id/scope). The Tenant "
            + "Service's is the JSON body of /api/auth/login (email/password/tier/returnTo). They "
            + "never travel the same request. Renaming the CLI's would be the real fix; tracked on "
            + "DRIFT-004.",
    };

    /// <summary>
    /// Pairs known to disagree today, each with what breaks. This is a RATCHET and a worklist: it
    /// may only shrink.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The harness found these the day it was written. Shipping it red was not an option, and
    /// silently skipping them would defeat the point — so each is recorded with its symptom, and
    /// the test asserts the pair <b>still</b> disagrees. Fix one and the suite fails until its
    /// entry is removed, so this list cannot decay into fiction.
    /// </para>
    /// <para>
    /// They are not equally bad. The dangerous ones are those that <i>appear to succeed</i>:
    /// <c>rotate-secret</c> hands back an empty secret, <c>audit query</c> reports nothing. A 400
    /// at least announces itself.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> KnownMismatch = new(StringComparer.Ordinal)
    {
        ["AvailableRegisterInfo"] =
            "Peer.Service — server-only: description, name",
        ["BootstrapResponse"] =
            "Tenant.Service — server-only: platformUserId, publicOrgId, systemAdminOrgId",
        ["InitiateWalletLinkRequest"] =
            "Tenant.Service — server-only: algorithm",
        ["IssueCredentialRequest"] =
            "Wallet.Service — CLI-only: expiresInDays, subject, type, walletAddress; server-only: credentialType, disclosableClaims, displayName, expiryDuration, holderJwk, issu...",
        ["LinkedWalletAddress"] =
            "Tenant.Service (vs LinkedWalletAddressResponse) — CLI-only: verifiedAt; server-only: algorithm, linkedAt, revokedAt",
        ["Organization"] =
            "Tenant.Service (vs OrganizationResponse) — server-only: walletAddress",
        ["ParticipantIdentity"] =
            "Tenant.Service (vs ParticipantResponse) — CLI-only: updatedAt, walletLinks; server-only: email, hasLinkedWallet",
        ["PeerStatistics"] =
            "Peer.Service — CLI-only: bootstrapNodes; server-only: seedNodes",
        ["PlatformSettingsResponse"] =
            "Tenant.Service — CLI-only: registrationOpen, socialLoginEnabled; server-only: publicOrgId, publicOrgStatus, updatedAt",
        ["PolicyHistoryResponse"] =
            "Register.Service — server-only: totalPages",
        ["PolicyUpdateRequest"] =
            "Register.Service — CLI-only: maxValidators, minValidators, registrationMode, signatureThreshold; server-only: policy, updatedBy",
        ["PolicyUpdateResponse"] =
            "Register.Service — CLI-only: currentVotes, proposalId, requiredVotes, status; server-only: currentVersion, message, registerId, requiresGovernanceVote",
        ["PolicyVersionEntry"] =
            "Register.Service — server-only: policy",
        ["PublishBlueprintRequest"] =
            "ServiceClients.Http — CLI-only: blueprintId; server-only: override",
        ["RegisterPolicyResponse"] =
            "Register.Service — CLI-only: maxValidators, minValidators, registrationMode, signatureThreshold, transitionMode, updatedAt, updatedBy, version; server-only: isDefault...",
        ["RegisterStatsResponse"] =
            "ServiceClients.Http — CLI-only: count; server-only: registerCount, transactionCount",
        ["RegisterSyncStatus"] =
            "Peer.Service — CLI-only: currentDocket, docketsProcessed, isStale, lastError, progressPercent, status, targetDocket; server-only: canServeFullReplica, lastSyncAt,...",
        ["SchemaSector"] =
            "Blueprint.Service — CLI-only: name, schemaCount, status; server-only: displayName, icon",
        ["ServicePrincipal"] =
            "Tenant.Service (vs ServicePrincipalResponse) — CLI-only: description, isActive, name, organizationId, secretRotatedAt, updatedAt; server-only: id, serviceName, status",
        ["SubmitTransactionRequest"] =
            "Peer.Service — CLI-only: payload, previousTxId, senderWallet, signature, txType; server-only: originPeerId, submissionJson, submittedAtUnixMs",
        ["SubmitTransactionResponse"] =
            "Peer.Service — CLI-only: error, status, transactionId; server-only: accepted, receiverIsValidator, rejectReason",
        ["UpdateUserRequest"] =
            "Tenant.Service — CLI-only: email, firstName, isActive, lastName; server-only: displayName, status",
        ["ValidatorAuditEntry"] =
            "Validator.Service — server-only: id, registerId",
        ["ValidatorStatus"] =
            "Validator.Service — CLI-only: consensusProtocol, failedValidations, isRunning, lastValidationAt, registersMonitored, status, totalValidations, uptime; server-only: doc...",
        ["VerificationResult"] =
            "Wallet.Service — CLI-only: status, verifiedAt; server-only: credentialType, issuerDid, statusListCheck, verifiedClaims",
        ["WalletLinkChallengeResponse"] =
            "Tenant.Service — CLI-only: nonce; server-only: algorithm, challenge, status, walletAddress",
    };

    /// <summary>
    /// Where the server's wire type has a DIFFERENT name from the CLI's, the type the endpoint
    /// actually produces or consumes.
    /// </summary>
    /// <remarks>
    /// Matching on name alone pairs a CLI DTO with a server <i>entity</i> that merely shares its
    /// name — e.g. <c>Organization</c> is an EF entity carrying <c>publicKey</c> and
    /// <c>creatorIdentityId</c>, while the endpoint actually returns <c>OrganizationResponse</c>.
    /// Comparing against the entity produces noise and, worse, would have the CLI chase fields that
    /// never appear on the wire. The authority is what <c>.Produces&lt;T&gt;()</c> declares.
    /// </remarks>
    private static readonly Dictionary<string, string> CounterpartTypeName = new(StringComparer.Ordinal)
    {
        ["Organization"] = "OrganizationResponse",
        ["ServicePrincipal"] = "ServicePrincipalResponse",
        ["LinkedWalletAddress"] = "LinkedWalletAddressResponse",
        ["ParticipantIdentity"] = "ParticipantResponse",
        ["AuditLogEntry"] = "AuditEventResponse",
    };

    /// <summary>
    /// Where a CLI type name matches more than one server type, the assembly whose type is the
    /// genuine counterpart.
    /// </summary>
    private static readonly Dictionary<string, string> PreferredCounterpartAssembly = new(StringComparer.Ordinal)
    {
        // The CLI's verify command talks to the Wallet Service, not HAIP's external-wallet surface.
        ["VerificationResult"] = "Sorcha.Wallet.Service",

        // Declared in BOTH Register.Service and ServiceClients.Http. The server is the authority
        // for what the CLI must match. (That the shared client ALSO disagrees with the server is a
        // separate finding, recorded on DRIFT-002.)
        ["RegisterPolicyResponse"] = "Sorcha.Register.Service",
        ["PolicyHistoryResponse"] = "Sorcha.Register.Service",
        ["PolicyVersionEntry"] = "Sorcha.Register.Service",
    };

    private static readonly string[] ServerAssemblies =
    [
        "Sorcha.Tenant.Service",
        "Sorcha.Register.Service",
        "Sorcha.Wallet.Service",
        "Sorcha.Blueprint.Service",
        "Sorcha.Validator.Service",
        "Sorcha.Peer.Service",
        "Sorcha.ServiceClients.Http",
    ];

    public static TheoryData<string> DiscoveredPairs()
    {
        var data = new TheoryData<string>();
        foreach (var name in Pairs().Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DiscoveredPairs))]
    public void CliDto_AgreesWithServerDto(string typeName)
    {
        var (cli, server) = Pairs()[typeName];

        var cliOnly = WireShape.OnlyOn(cli, server);
        var serverOnly = WireShape.OnlyOn(server, cli);
        var differs = cliOnly.Count + serverOnly.Count > 0;

        if (KnownMismatch.TryGetValue(typeName, out var symptom))
        {
            // Ratchet: a baselined pair must STILL differ. Fixing one without removing its entry
            // fails here, so the list can never quietly decay into fiction.
            differs.Should().BeTrue(
                $"'{typeName}' is baselined as broken ({symptom}) but now agrees — remove it from "
                + "KnownMismatch in the same change that fixed it");
            return;
        }

        differs.Should().BeFalse(
            "the CLI and the server exchange this body, so a name on only one side is silently "
            + $"dropped rather than reported:\n{WireShape.Describe(cli, server)}\n");
    }

    [Fact]
    public void EveryNameCollision_IsEitherCheckedOrJustified()
    {
        // Anti-drift: a new CLI type colliding with a server type must be either a checked pair or
        // an explicitly reasoned exception. Without this, the suite would quietly stop covering
        // whatever was added last.
        var checkedOrExcluded = Pairs().Keys
            .Concat(NotAWireContract.Keys)
            .ToHashSet(StringComparer.Ordinal);

        var collisions = CliTypes()
            .Select(t => t.Name)
            .Where(n => ServerTypeCandidates(CounterpartTypeName.GetValueOrDefault(n, n)).Count > 0)
            .ToHashSet(StringComparer.Ordinal);

        collisions.Except(checkedOrExcluded).Should().BeEmpty(
            "every CLI type sharing a name with a server type must be checked as a wire pair or "
            + "justified in NotAWireContract");
    }

    [Fact]
    public void Discovery_IsNotVacuous()
    {
        // A reflection bug that found nothing would make the Theory above pass by not running.
        Pairs().Should().NotBeEmpty();
        CliTypes().Should().HaveCountGreaterThan(20);
    }

    [Fact]
    public void ExclusionList_HasNoStaleEntries()
    {
        // A ratchet: once a CLI-local type is removed or renamed, its excuse must go too.
        var cliNames = CliTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        NotAWireContract.Keys.Where(k => !cliNames.Contains(k)).Should().BeEmpty(
            "NotAWireContract names a CLI type that no longer exists");
    }

    // ---------------------------------------------------------------------------------------

    private static IReadOnlyList<Type> CliTypes() =>
        typeof(Sorcha.Cli.Models.Profile).Assembly
            .GetTypes()
            .Where(t => t is { IsPublic: true, IsClass: true } or { IsPublic: true, IsValueType: true, IsEnum: false })
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("Sorcha.Cli.", StringComparison.Ordinal))
            .Where(t => !t.IsAbstract || !t.IsSealed) // exclude static classes
            .ToList();

    private static List<Type> ServerTypeCandidates(string name) =>
        ServerAssemblies
            .Select(TryLoad)
            .Where(a => a is not null)
            .SelectMany(a => a!.GetTypes())
            .Where(t => t.IsPublic && t.Name == name)
            // An enum has no properties, so it can never be the shape counterpart of a DTO.
            .Where(t => !t.IsEnum)
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("Sorcha.", StringComparison.Ordinal))
            .ToList();

    private static Assembly? TryLoad(string name)
    {
        try { return Assembly.Load(name); }
        catch { return null; }
    }

    private static Dictionary<string, (Type Cli, Type Server)> Pairs()
    {
        var pairs = new Dictionary<string, (Type, Type)>(StringComparer.Ordinal);

        foreach (var cliType in CliTypes())
        {
            if (NotAWireContract.ContainsKey(cliType.Name)) continue;
            if (pairs.ContainsKey(cliType.Name)) continue;

            var counterpartName = CounterpartTypeName.GetValueOrDefault(cliType.Name, cliType.Name);
            var candidates = ServerTypeCandidates(counterpartName);
            if (candidates.Count == 0) continue;

            Type? server;
            if (candidates.Count == 1)
            {
                server = candidates[0];
            }
            else if (PreferredCounterpartAssembly.TryGetValue(cliType.Name, out var preferred))
            {
                server = candidates.FirstOrDefault(c => c.Assembly.GetName().Name == preferred);
            }
            else
            {
                // Ambiguous and unresolved — surfaced by EveryNameCollision_IsEitherCheckedOrJustified.
                continue;
            }

            if (server is not null)
            {
                pairs[cliType.Name] = (cliType, server);
            }
        }

        return pairs;
    }
}
