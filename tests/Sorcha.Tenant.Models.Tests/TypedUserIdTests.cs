// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Xunit;
using Sorcha.Tenant.Models.Identity;

namespace Sorcha.Tenant.Models.Tests;

/// <summary>
/// Issue #1709 — typing the inbox boundary must not change the wire, log lines, or deterministic
/// idempotency keys. Each typed id must be indistinguishable from its underlying GUID in all three.
/// </summary>
public class TypedUserIdTests
{
    private static readonly Guid Raw = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

    private static readonly JsonSerializerOptions[] OptionSets =
    [
        new JsonSerializerOptions(),
        new JsonSerializerOptions(JsonSerializerDefaults.Web),
    ];

    public sealed record PlatformEnvelope(PlatformUserId Id, PlatformUserId? Maybe);

    public sealed record GuidEnvelope(Guid Id, Guid? Maybe);

    public sealed record IdentityEnvelope(UserIdentityId Id, UserIdentityId? Maybe);

    [Fact]
    public void PlatformUserId_Serialize_MatchesUnderlyingGuid()
    {
        foreach (var options in OptionSets)
        {
            JsonSerializer.Serialize(new PlatformUserId(Raw), options)
                .Should().Be(JsonSerializer.Serialize(Raw, options));
        }
    }

    [Fact]
    public void UserIdentityId_Serialize_MatchesUnderlyingGuid()
    {
        foreach (var options in OptionSets)
        {
            JsonSerializer.Serialize(new UserIdentityId(Raw), options)
                .Should().Be(JsonSerializer.Serialize(Raw, options));
        }
    }

    [Fact]
    public void PlatformUserId_Deserialize_FromGuidJson_RoundTrips()
    {
        var json = JsonSerializer.Serialize(Raw);
        JsonSerializer.Deserialize<PlatformUserId>(json).Should().Be(new PlatformUserId(Raw));
    }

    [Fact]
    public void UserIdentityId_Deserialize_FromGuidJson_RoundTrips()
    {
        var json = JsonSerializer.Serialize(Raw);
        JsonSerializer.Deserialize<UserIdentityId>(json).Should().Be(new UserIdentityId(Raw));
    }

    [Fact]
    public void Nullable_Serialize_MatchesNullableGuid_BothPresentAndNull()
    {
        foreach (var options in OptionSets)
        {
            var guidJson = JsonSerializer.Serialize(new GuidEnvelope(Raw, null), options);
            JsonSerializer.Serialize(new PlatformEnvelope(new PlatformUserId(Raw), null), options).Should().Be(guidJson);
            JsonSerializer.Serialize(new IdentityEnvelope(new UserIdentityId(Raw), null), options).Should().Be(guidJson);

            var guidJsonPresent = JsonSerializer.Serialize(new GuidEnvelope(Raw, Raw), options);
            JsonSerializer.Serialize(new PlatformEnvelope(new PlatformUserId(Raw), new PlatformUserId(Raw)), options)
                .Should().Be(guidJsonPresent);
            JsonSerializer.Serialize(new IdentityEnvelope(new UserIdentityId(Raw), new UserIdentityId(Raw)), options)
                .Should().Be(guidJsonPresent);
        }
    }

    [Fact]
    public void Nullable_Deserialize_FromGuidJson_BothPresentAndNull()
    {
        var json = JsonSerializer.Serialize(new GuidEnvelope(Raw, null));
        var platform = JsonSerializer.Deserialize<PlatformEnvelope>(json)!;
        platform.Id.Should().Be(new PlatformUserId(Raw));
        platform.Maybe.Should().BeNull();

        var jsonPresent = JsonSerializer.Serialize(new GuidEnvelope(Raw, Raw));
        var identity = JsonSerializer.Deserialize<IdentityEnvelope>(jsonPresent)!;
        identity.Maybe.Should().Be(new UserIdentityId(Raw));

        JsonSerializer.Deserialize<PlatformUserId?>("null").Should().BeNull();
        JsonSerializer.Deserialize<UserIdentityId?>("null").Should().BeNull();
    }

    [Fact]
    public void ToString_AndFormatSpecifiers_MatchUnderlyingGuid()
    {
        // Log lines and the deterministic inbox SourceEventId / CorrelationKey strings interpolate
        // these ids with `:N` / `:D`. A struct that ignored the specifier would silently change every
        // idempotency key and collapse nothing on retry.
        var p = new PlatformUserId(Raw);
        var u = new UserIdentityId(Raw);

        p.ToString().Should().Be(Raw.ToString());
        u.ToString().Should().Be(Raw.ToString());
        $"{p:N}".Should().Be($"{Raw:N}");
        $"{u:N}".Should().Be($"{Raw:N}");
        $"{p:D}".Should().Be($"{Raw:D}");
        $"{p}".Should().Be($"{Raw}");
    }

    [Fact]
    public void Equality_IsByValue_AndTheTwoKindsNeverCompareEqual()
    {
        new PlatformUserId(Raw).Should().Be(new PlatformUserId(Raw));
        new UserIdentityId(Raw).Should().Be(new UserIdentityId(Raw));
        new PlatformUserId(Raw).Equals(new UserIdentityId(Raw)).Should().BeFalse();
    }

    [Fact]
    public void FromNullable_PreservesNull()
    {
        PlatformUserId.FromNullable(null).Should().BeNull();
        PlatformUserId.FromNullable(Raw).Should().Be(new PlatformUserId(Raw));
        UserIdentityId.FromNullable(null).Should().BeNull();
        UserIdentityId.FromNullable(Raw).Should().Be(new UserIdentityId(Raw));
    }
}
