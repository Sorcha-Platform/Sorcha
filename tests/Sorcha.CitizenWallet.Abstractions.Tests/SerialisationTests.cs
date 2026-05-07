// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.CitizenWallet.Abstractions.Constants;
using Sorcha.CitizenWallet.Abstractions.Models;

namespace Sorcha.CitizenWallet.Abstractions.Tests;

public sealed class SerialisationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void EcP256PublicJwk_RoundTrips()
    {
        var original = new EcP256PublicJwk { Kty = "EC", Crv = "P-256", X = "abc", Y = "def" };
        var json = JsonSerializer.Serialize(original, Options);
        var parsed = JsonSerializer.Deserialize<EcP256PublicJwk>(json, Options);

        parsed.Should().BeEquivalentTo(original);
        json.Should().Contain("\"kty\":\"EC\"");
        json.Should().Contain("\"crv\":\"P-256\"");
    }

    [Fact]
    public void DeviceDelegationCredential_UsesSnakeCaseClaims()
    {
        var credential = new DeviceDelegationCredential
        {
            Issuer = "did:sorcha:holder:abc",
            Subject = "did:sorcha:device:def",
            IssuedAt = 1735689600,
            Expires = 1767225600,
            Vct = VctUris.CitizenDeviceDelegationV1,
            DelegatedCapabilities = [DelegatedCapabilities.PresentationHolderKeyBinding],
            Device = new DeviceDescriptor
            {
                Label = "Stuart's iPhone",
                Platform = "iOS 19",
                EnrolledAt = 1735689600
            },
            Confirmation = new ConfirmationMethod
            {
                Jwk = new EcP256PublicJwk { X = "a", Y = "b" }
            },
            Status = new StatusBlock
            {
                StatusList = new StatusListReference
                {
                    Uri = "https://sorcha.dev/status/1.statuslist+jwt",
                    Index = 4711
                }
            }
        };

        var json = JsonSerializer.Serialize(credential, Options);

        // Wire-format claim names per the JSON Schema in contracts/
        json.Should().Contain("\"iss\":\"did:sorcha:holder:abc\"");
        json.Should().Contain("\"sub\":\"did:sorcha:device:def\"");
        json.Should().Contain("\"iat\":1735689600");
        json.Should().Contain("\"exp\":1767225600");
        json.Should().Contain("\"vct\":\"https://sorcha.dev/vc/citizen-device-delegation/v1\"");
        json.Should().Contain("\"delegated_capabilities\":[\"presentation.holder-key-binding\"]");
        json.Should().Contain("\"enrolled_at\":1735689600");
        json.Should().Contain("\"status_list\":");
        json.Should().Contain("\"idx\":4711");
    }

    [Fact]
    public void SyncResponse_RoundTrips()
    {
        var original = new SyncResponse
        {
            SyncToken = "opaque-token",
            Credentials = new SyncCredentialChanges
            {
                Added = [new CachedCredentialPayload { Id = $"urn:credential:test:{Guid.NewGuid():N}", Vct = "test", Jwt = "jwt", IssuerDid = "did:test", IssuedAt = DateTimeOffset.UtcNow }]
            },
            Delegation = new SyncDelegationUpdate { Renewed = false },
            StatusListsToRefresh = ["https://sorcha.dev/status/1.statuslist+jwt"]
        };

        var json = JsonSerializer.Serialize(original, Options);
        var parsed = JsonSerializer.Deserialize<SyncResponse>(json, Options);

        parsed.Should().NotBeNull();
        parsed!.SyncToken.Should().Be("opaque-token");
        parsed.Credentials.Added.Should().HaveCount(1);
        parsed.StatusListsToRefresh.Should().ContainSingle()
            .Which.Should().Be("https://sorcha.dev/status/1.statuslist+jwt");
    }

    [Fact]
    public void EmbeddedJsonSchema_IsAccessible()
    {
        // The schema is embedded as a resource so server + verifier can load it for VC validation.
        var assembly = typeof(EcP256PublicJwk).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("device-delegation-credential.v1.json"));

        resourceName.Should().NotBeNull("schema must be embedded as a resource");

        using var stream = assembly.GetManifestResourceStream(resourceName!);
        stream.Should().NotBeNull();

        using var reader = new StreamReader(stream!);
        var content = reader.ReadToEnd();
        content.Should().Contain("citizen-device-delegation");
        content.Should().Contain("delegated_capabilities");
    }
}
