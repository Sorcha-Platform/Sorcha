// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json.Serialization;

using FluentAssertions;

using Sorcha.ContractTests.Shared;

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
        ["VerificationResult"] =
            "The CLI's verify command posts to the REGISTER Service's /receipts/verify and "
            + "/verification-bundles/verify, which return an anonymous { isValid, checks, errors } "
            + "object — not the Wallet Service's VerificationResult the name collides with. The CLI "
            + "type now matches that anonymous shape (isValid/checks/errors); there is no server type "
            + "to pair against, so this is not a wire-contract collision.",
        ["IssueCredentialRequest"] =
            "Deliberate curated SUBSET, not drift. The Wallet Service's issue request has 11 further "
            + "OPTIONAL fields the CLI intentionally does not expose: holderJwk, tenantId, "
            + "trustAnchor, issuerOrgName, skipRecipientStore, vct, issuanceBlueprintId, and the four "
            + "statusList* wiring fields. Those are issuer-infrastructure and status-list plumbing "
            + "supplied by blueprint-action-driven issuance, not sensible CLI flags. Every field the "
            + "CLI DOES send (credentialType, claims, recipientWallet, displayName, expiryDuration, "
            + "disclosableClaims) matches the server exactly, and the three the server REQUIRES "
            + "(credentialType, claims, recipientWallet) are all present — verified by "
            + "IssueCredentialRequest_SendsEveryRequiredServerField below. A future field the server "
            + "makes required would be caught by that test, not silently dropped.",
        ["InstanceParityResult"] =
            "The parity endpoint PROJECTS the service's internal InstanceParityResult record into a "
            + "different anonymous wire shape — { instanceId, registerId, inSync, detail, "
            + "rebuiltState, materializedState } — flattening the two Instance objects to their state "
            + "strings. The CLI type matches that projected wire shape exactly; the internal record "
            + "(inSync/detail/rebuilt/materialized, carrying full Instance objects) is not what "
            + "crosses the wire. A name collision with an internal type, not a shared contract.",
        ["RegisterSyncStatus"] =
            "The CLI's /health/sync response models the Register Service's RegisterSyncStatus, which "
            + "is a PRIVATE record inside RecoveryHealthEndpoints and therefore invisible to this "
            + "assembly. The CLI type matches it field-for-field. The public type the harness would "
            + "otherwise pair against lives in Peer.Service and is a different surface entirely — a "
            + "name collision, not a shared contract.",
        // ["LoginRequest"] is GONE (issue #1160). Its own justification said renaming the CLI type
        // was the real fix, so that is what happened: Sorcha.Cli.Models.LoginRequest is now
        // PasswordGrantRequest, which no server type shares a name with, so discovery pairs nothing
        // and no entry is needed. An entry removed by killing the collision is the outcome this list
        // is supposed to drive towards — do not re-add one to silence a future pairing.
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

        // Feature 181 US3 trusted-list admin. The Tenant Service suffixes its wire types "Response";
        // the CLI drops the suffix. Pair them so the trust command's DTOs are actually checked.
        ["TrustListSnapshotSummary"] = "TrustListSnapshotSummaryResponse",
        ["TrustListSnapshotDetail"] = "TrustListSnapshotDetailResponse",
        ["TrustListAnchor"] = "TrustListAnchorResponse",

        // Issue #1402 — the CLI's user-login request is deliberately named UserLoginRequest, not
        // LoginRequest, so it does not collide with the server's PRIVATE OAuth2TokenRequest shape
        // (used by the /api/service-auth/token form-and-JSON parser) and so a reader is not misled
        // into thinking this is the same PasswordGrantRequest/LoginRequest naming clash issue #1160
        // already fixed. It exchanges bodies with the Tenant Service's
        // Sorcha.Tenant.Service.Models.Dtos.LoginRequest (POST /api/auth/login), so pair against
        // that type by name explicitly.
        ["UserLoginRequest"] = "LoginRequest",
    };

    /// <summary>
    /// Where a CLI type name matches more than one server type, the assembly whose type is the
    /// genuine counterpart.
    /// </summary>
    private static readonly Dictionary<string, string> PreferredCounterpartAssembly = new(StringComparer.Ordinal)
    {
        // Declared in BOTH Blueprint.Service (an action-submission nested type) and Wallet.Service.
        // The CLI's credential issue command talks to the Wallet Service.
        ["IssuedCredentialResponse"] = "Sorcha.Wallet.Service",

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

    [Fact]
    public void IssueCredentialRequest_SendsEveryRequiredServerField()
    {
        // IssueCredentialRequest is a deliberate curated subset (see its NotAWireContract entry):
        // the CLI omits optional issuer-infrastructure fields on purpose. That is only safe as long
        // as it still sends everything the server REQUIRES — a field the server later makes required
        // must not slip through the subset. This is the guard that makes the subclaim honest.
        var serverType = Type.GetType(
            "Sorcha.Wallet.Service.Endpoints.IssueCredentialRequest, Sorcha.Wallet.Service");
        serverType.Should().NotBeNull("the server request type must be resolvable");

        var requiredServerNames = serverType!
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<System.ComponentModel.DataAnnotations.RequiredAttribute>() is not null)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                         ?? System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToHashSet(StringComparer.Ordinal);

        requiredServerNames.Should().NotBeEmpty("the server marks some fields [Required]");

        var cliType = CliTypes().Single(t => t.Name == "IssueCredentialRequest");
        var cliNames = WireShape.Of(cliType);

        requiredServerNames.Except(cliNames).Should().BeEmpty(
            "the CLI issue request may omit optional server fields, but never a required one");
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
