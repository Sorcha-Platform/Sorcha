// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections;
using System.Reflection;
using System.Text.Json;

using FluentAssertions;

using Sorcha.ContractTests.Shared;

using ClientDtos = Sorcha.UI.Core.Services.Identity;
using ServerDtos = Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.UI.ContractTests;

/// <summary>
/// Pins the <b>organisation user invitation</b> wire contract: the Blazor client DTOs in
/// <c>Sorcha.UI.Components.User</c> must agree with what the Tenant Service's
/// <c>/api/organizations/{id}/invitations</c> endpoints bind and return.
/// </summary>
/// <remarks>
/// <para>
/// This is the test the codebase was missing, and it is not hypothetical — writing it required
/// fixing four live defects that both sides had been unit-tested past:
/// </para>
/// <list type="number">
/// <item><description>
/// The list endpoint returns a bare JSON array; the client deserialised into a
/// <c>{ invitations, totalCount }</c> envelope the server has never sent, so every call threw
/// <see cref="JsonException"/> and the admin Invitations page rendered an error instead of a list.
/// </description></item>
/// <item><description>
/// The client's invitation record declared <c>invitedByUserId</c> (a <see cref="Guid"/>),
/// <c>acceptedByUserId</c> and <c>acceptedAt</c>. The server sends <c>invitedBy</c> — a display
/// name — and neither accepted field.
/// </description></item>
/// <item><description>
/// <c>ResendInvitationAsync</c> POSTed to <c>/{id}/resend</c>, a route the service does not have.
/// The page discarded the returned false and reported "Invitation resent".
/// </description></item>
/// <item><description>
/// <c>GetInvitationAsync</c> GET-ed <c>/invitations/{id}</c>, also absent, so its 404 handler
/// turned it into a permanent null.
/// </description></item>
/// </list>
/// <para>
/// Each side was internally consistent and independently green. Only a test that can see both
/// assemblies at once catches this class of defect — hence this project's deliberate layering.
/// </para>
/// </remarks>
public sealed class OrgInvitationWireContractTests
{
    /// <summary>Client DTO ↔ server DTO pairings. Each pair travels the same HTTP body.</summary>
    private static readonly (string Label, Type Client, Type Server)[] Pairs =
    [
        ("create request", typeof(ClientDtos.CreateOrgInvitationRequest), typeof(ServerDtos.CreateOrgInvitationRequest)),
        ("invitation", typeof(ClientDtos.OrgInvitationDto), typeof(ServerDtos.OrgInvitationResponse)),
    ];

    public static TheoryData<string, Type, Type> ContractPairs()
    {
        var data = new TheoryData<string, Type, Type>();
        foreach (var (label, client, server) in Pairs)
        {
            data.Add(label, client, server);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ContractPairs))]
    public void ClientAndServerDtos_AgreeOnJsonPropertyNames(string label, Type clientType, Type serverType)
    {
        WireShape.Of(clientType).Should().BeEquivalentTo(
            WireShape.Of(serverType),
            $"the {label} DTOs sit on opposite ends of the same HTTP body; a name that exists on "
            + $"only one side is silently dropped on the wire, not a compile error.\n"
            + WireShape.Describe(clientType, serverType));
    }

    [Fact]
    public void ContractPairs_CoverEveryServerOrgInvitationDto()
    {
        // Anti-vacuity: a pairing list that silently fell out of date would let a new endpoint DTO
        // ship unguarded while this suite stayed green.
        var covered = Pairs.Select(p => p.Server).ToHashSet();

        Type[] expected =
        [
            typeof(ServerDtos.CreateOrgInvitationRequest),
            typeof(ServerDtos.OrgInvitationResponse),
        ];

        covered.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void ListEndpoint_ReturnsABareCollection_OnBothSides()
    {
        // Defect 1. The server's list operation returns List<OrgInvitationResponse>, which
        // serialises as a top-level JSON array. A client modelling that as an object with an
        // `invitations` property cannot deserialise it at all — and the failure surfaces only at
        // runtime, as a JsonException inside a catch-and-render-error handler.
        var serverElement = ElementTypeOf(
            typeof(Sorcha.Tenant.Service.Services.IInvitationService)
                .GetMethod(nameof(Sorcha.Tenant.Service.Services.IInvitationService.ListInvitationsAsync))!
                .ReturnType);

        var clientElement = ElementTypeOf(
            typeof(ClientDtos.IInvitationClientService)
                .GetMethod(nameof(ClientDtos.IInvitationClientService.GetInvitationsAsync))!
                .ReturnType);

        serverElement.Should().NotBeNull("the server list operation must return a collection");
        clientElement.Should().NotBeNull(
            "the client must model the list response as a collection, not an envelope — the server "
            + "sends a top-level JSON array");

        WireShape.Of(clientElement!).Should().BeEquivalentTo(
            WireShape.Of(serverElement!),
            "the list element shapes must agree.\n" + WireShape.Describe(clientElement!, serverElement!));
    }

    [Fact]
    public void ClientInterface_DeclaresOnlyOperationsTheServerImplements()
    {
        // Defects 3 and 4. A client method aimed at a route that does not exist cannot fail loudly:
        // it returns a default or a swallowed 404, and the caller reports success.
        var clientOperations = typeof(ClientDtos.IInvitationClientService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        clientOperations.Should().BeEquivalentTo(
            ["GetInvitationsAsync", "CreateInvitationAsync", "RevokeInvitationAsync"],
            "the Tenant Service implements exactly create, list and revoke for org invitations "
            + "(see InvitationEndpoints.MapInvitationEndpoints). A client method with no endpoint "
            + "behind it fails silently — if you add one here, add the endpoint first.");
    }

    [Theory]
    [InlineData("Consumer")]
    [InlineData("Designer")]
    [InlineData("Auditor")]
    [InlineData("Administrator")]
    public void EveryRoleTheUiOffers_BindsToTheServersUserRoleEnum(string uiRole)
    {
        // The client models Role as a string; the server models it as the UserRole enum, serialised
        // by the Tenant Service's configured options (camelCase members, kebab-case string enums).
        // Whether a PascalCase role name survives that converter is not something to infer from the
        // BCL's matching rules — pin it against the real options object.
        var options = new JsonSerializerOptions();
        Sorcha.Serialization.SorchaJson.Configure(options);

        var json = $$"""{"email":"invitee@example.test","role":"{{uiRole}}","expiryDays":7}""";

        var act = () => JsonSerializer.Deserialize<ServerDtos.CreateOrgInvitationRequest>(json, options);

        var bound = act.Should().NotThrow(
            $"the Invitations page offers '{uiRole}' in its role picker and sends it verbatim")
            .Which;

        bound.Should().NotBeNull();
        bound!.Role.ToString().Should().Be(uiRole);
    }

    [Fact]
    public void TheAmbiguousName_CreateInvitationRequest_IsRetiredEverywhere()
    {
        // "Invitation" means two unrelated things in Sorcha: this org-user invite, and the
        // register invitation envelope (Sorcha.ServiceClients.Invitation). Three projects once
        // declared a type called exactly CreateInvitationRequest across both concepts, with
        // disjoint shapes — so the obvious "consolidate the duplicate" move would have produced a
        // type satisfying neither contract. The names now carry the concept; keep it that way.
        Assembly[] assemblies =
        [
            typeof(ClientDtos.IInvitationClientService).Assembly,
            typeof(ServerDtos.CreateOrgInvitationRequest).Assembly,
        ];

        var offenders = assemblies
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => t.Name == "CreateInvitationRequest")
            .Select(t => t.FullName!)
            .ToList();

        offenders.Should().BeEmpty(
            "the bare name does not say which invitation concept it means. Use "
            + "CreateOrgInvitationRequest (email a person a role) or CreateRegisterInvitationRequest "
            + "(bring an organisation onto a private register).");
    }

    /// <summary>
    /// The element type of a (possibly <see cref="Task{TResult}"/>-wrapped) collection, or null if
    /// the type is not a collection.
    /// </summary>
    private static Type? ElementTypeOf(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            type = type.GetGenericArguments()[0];
        }

        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return null;
        }

        return type.IsGenericType ? type.GetGenericArguments().FirstOrDefault() : null;
    }
}
