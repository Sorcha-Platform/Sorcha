// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using FluentAssertions;

using ClientDtos = Sorcha.ServiceClients.Invitation;
using ServerDtos = Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Tests.Contracts;

/// <summary>
/// Pins the register-invitation wire contract: the shared client DTOs in
/// <c>Sorcha.ServiceClients.Http</c> must serialise to exactly the JSON shape the Tenant Service
/// endpoints bind.
/// </summary>
/// <remarks>
/// <para>
/// This is the test the codebase was missing. The CLI carried a third, hand-written copy of these
/// DTOs whose property names were plain camelCase (<c>registerId</c>) against a server binding
/// snake_case (<c>register_id</c>), and which said <c>expiresInHours</c> where the server said
/// <c>expires_in_days</c>. Every <c>sorcha invitation</c> subcommand failed against a live server,
/// and nothing in the build or the test suite noticed — each side was internally consistent and
/// unit-tested in isolation. The CLI copy is now deleted; this test stops the two remaining
/// definitions from drifting apart the same way.
/// </para>
/// <para>
/// It compares the effective JSON property <i>names</i> rather than serialising instances, so it
/// works for records with <c>required</c> members without needing to construct valid values, and it
/// catches a rename on either side regardless of type.
/// </para>
/// </remarks>
public sealed class RegisterInvitationWireContractTests
{
    /// <summary>
    /// Client DTO ↔ server DTO pairings. Each pair travels the same HTTP body in one direction.
    /// </summary>
    private static readonly (string Label, Type Client, Type Server)[] Pairs =
    [
        ("create request", typeof(ClientDtos.CreateInvitationRequest), typeof(ServerDtos.CreateRegisterInvitationRequest)),
        ("create response", typeof(ClientDtos.InvitationCreatedResponse), typeof(ServerDtos.InvitationCreatedResponse)),
        ("accept request", typeof(ClientDtos.AcceptInvitationRequest), typeof(ServerDtos.AcceptInvitationRequest)),
        ("accept response", typeof(ClientDtos.InvitationAcceptedResponse), typeof(ServerDtos.InvitationAcceptedResponse)),
        ("list item", typeof(ClientDtos.InvitationSummary), typeof(ServerDtos.InvitationSummary)),
        ("list response", typeof(ClientDtos.InvitationListResponse), typeof(ServerDtos.InvitationListResponse)),
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
        var clientNames = JsonPropertyNames(clientType);
        var serverNames = JsonPropertyNames(serverType);

        clientNames.Should().BeEquivalentTo(
            serverNames,
            $"the {label} DTOs sit on opposite ends of the same HTTP body; a name that exists on "
            + "only one side is silently dropped on the wire, not a compile error");
    }

    [Fact]
    public void CreateRequest_UsesDaysNotHours()
    {
        // The specific drift that broke `sorcha invitation create`: the CLI sent an expiry in hours
        // to a server that only ever reads days, so the field was ignored and every invitation
        // quietly took the 7-day default.
        var names = JsonPropertyNames(typeof(ClientDtos.CreateInvitationRequest));

        names.Should().Contain("expires_in_days");
        names.Should().NotContain(n => n.Contains("hour", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContractPairs_CoverEveryServerInvitationEndpointDto()
    {
        // Anti-vacuity: a pairing list that silently fell out of date would let a new endpoint DTO
        // ship unguarded while this suite stayed green.
        var covered = Pairs.Select(p => p.Server).ToHashSet();

        Type[] expected =
        [
            typeof(ServerDtos.CreateRegisterInvitationRequest),
            typeof(ServerDtos.InvitationCreatedResponse),
            typeof(ServerDtos.AcceptInvitationRequest),
            typeof(ServerDtos.InvitationAcceptedResponse),
            typeof(ServerDtos.InvitationSummary),
            typeof(ServerDtos.InvitationListResponse),
        ];

        covered.Should().BeEquivalentTo(expected);
    }

    /// <summary>
    /// The JSON names a type actually emits: an explicit <see cref="JsonPropertyNameAttribute"/>
    /// wins, otherwise the serializer's naming policy applies.
    /// </summary>
    private static HashSet<string> JsonPropertyNames(Type type)
    {
        var policy = JsonNamingPolicy.CamelCase;

        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            // Records synthesise EqualityContract; it is never serialised.
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                         ?? policy.ConvertName(p.Name))
            .ToHashSet(StringComparer.Ordinal);
    }
}
