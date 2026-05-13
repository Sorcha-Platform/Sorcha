// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Citizen.Wallet.Services;
using Sorcha.ServiceClients.CitizenWallet;
using Sorcha.UI.Core.Models.Presentation;
using Xunit;

namespace Sorcha.Citizen.Wallet.Tests.Services;

/// <summary>
/// Tests for <see cref="EnrolmentService"/> (Feature 114, T109 part 2).
/// Mocks the device key, citizen wallet client, sync service, and verifies
/// that the enrolment ceremony composes them correctly: generate key → call
/// /devices/enrol with the public JWK → persist delegation locally → run
/// initial sync → return result.
/// </summary>
public sealed class EnrolmentServiceTests
{
    [Fact]
    public async Task EnrolAsync_HappyPath_StitchesAllStepsTogether()
    {
        // 1. Device key returns a known JWK.
        var jwkJson = """{"kty":"EC","crv":"P-256","x":"abcdef","y":"012345"}""";
        var deviceKey = new Mock<IDeviceKeyService>();
        deviceKey.Setup(k => k.GetPublicJwkAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<JsonElement>(jwkJson));

        // 2. Wallet client captures the request and returns a delegation.
        DeviceEnrolmentRequest? sentRequest = null;
        var deviceId = Guid.NewGuid();
        var client = new Mock<ICitizenWalletClient>();
        client.Setup(c => c.EnrolDeviceAsync(It.IsAny<DeviceEnrolmentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeviceEnrolmentRequest, CancellationToken>((req, _) => sentRequest = req)
            .ReturnsAsync(new DeviceEnrolmentResponse
            {
                DeviceId = deviceId,
                DelegationCredential = "eyJ.delegation.compact",
                HolderPublicJwk = new EcP256PublicJwk { X = "h-x", Y = "h-y" },
                StatusListUri = "https://verify.test/status/0.statuslist+jwt",
                StatusListIndex = 7,
                DelegationExpiresAt = DateTimeOffset.UtcNow.AddYears(1),
            });

        // 3. Delegation store + cache + meta observable.
        var delegations = new InMemoryDelegationStore();
        var cache = new InMemoryCredentialCache();
        var meta = new InMemoryDeviceMetaStore();

        // 4. Sync service runs and reports success.
        var sync = new Mock<ISyncService>();
        sync.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncOutcome(SyncMode.FullSnapshot, 0, 0, 0, []));

        var sut = new EnrolmentService(deviceKey.Object, client.Object, delegations,
            meta, sync.Object, cache, TimeProvider.System,
            NullLogger<EnrolmentService>.Instance);

        var result = await sut.EnrolAsync("My iPhone", "iOS 19 / Safari 19", "Mozilla/5.0...");

        result.DeviceId.Should().Be(deviceId);
        result.CredentialsLoaded.Should().Be(0);
        sentRequest.Should().NotBeNull();
        sentRequest!.DeviceLabel.Should().Be("My iPhone");
        sentRequest.DevicePublicJwk.X.Should().Be("abcdef");
        sentRequest.DevicePublicJwk.Y.Should().Be("012345");
        sentRequest.Platform.Should().Be("iOS 19 / Safari 19");
        (await delegations.GetCurrentAsync()).Should().Be("eyJ.delegation.compact");
        sync.Verify(s => s.SyncAsync(It.IsAny<CancellationToken>()), Times.Once);

        var persistedMeta = await meta.GetAsync();
        persistedMeta.Should().NotBeNull();
        persistedMeta!.DeviceId.Should().Be(deviceId);
        persistedMeta.Label.Should().Be("My iPhone");
        persistedMeta.DelegationExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(360));
    }

    [Fact]
    public async Task EnrolAsync_LabelTrimmedAndPlatformDefault()
    {
        var deviceKey = new Mock<IDeviceKeyService>();
        deviceKey.Setup(k => k.GetPublicJwkAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Deserialize<JsonElement>(
                """{"kty":"EC","crv":"P-256","x":"x","y":"y"}"""));

        DeviceEnrolmentRequest? sentRequest = null;
        var client = new Mock<ICitizenWalletClient>();
        client.Setup(c => c.EnrolDeviceAsync(It.IsAny<DeviceEnrolmentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeviceEnrolmentRequest, CancellationToken>((req, _) => sentRequest = req)
            .ReturnsAsync(new DeviceEnrolmentResponse
            {
                DeviceId = Guid.NewGuid(),
                DelegationCredential = "eyJ",
                HolderPublicJwk = new EcP256PublicJwk(),
                StatusListUri = "https://x",
                StatusListIndex = 0,
                DelegationExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            });

        var sync = new Mock<ISyncService>();
        sync.Setup(s => s.SyncAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncOutcome(SyncMode.Delta, 0, 0, 0, []));

        var sut = new EnrolmentService(deviceKey.Object, client.Object,
            new InMemoryDelegationStore(), new InMemoryDeviceMetaStore(),
            sync.Object, new InMemoryCredentialCache(), TimeProvider.System,
            NullLogger<EnrolmentService>.Instance);

        await sut.EnrolAsync("  Padded label  ", "  ", "  ");

        sentRequest!.DeviceLabel.Should().Be("Padded label");
        sentRequest.Platform.Should().BeEmpty();
        sentRequest.UserAgent.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrolAsync_RejectsBlankLabel()
    {
        var sut = new EnrolmentService(
            new Mock<IDeviceKeyService>().Object,
            new Mock<ICitizenWalletClient>().Object,
            new InMemoryDelegationStore(),
            new InMemoryDeviceMetaStore(),
            new Mock<ISyncService>().Object,
            new InMemoryCredentialCache(),
            TimeProvider.System,
            NullLogger<EnrolmentService>.Instance);

        var act = () => sut.EnrolAsync("   ", "iOS", "ua");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task EnrolAsync_RejectsLabelLongerThan120Chars()
    {
        var sut = new EnrolmentService(
            new Mock<IDeviceKeyService>().Object,
            new Mock<ICitizenWalletClient>().Object,
            new InMemoryDelegationStore(),
            new InMemoryDeviceMetaStore(),
            new Mock<ISyncService>().Object,
            new InMemoryCredentialCache(),
            TimeProvider.System,
            NullLogger<EnrolmentService>.Instance);

        var act = () => sut.EnrolAsync(new string('a', 121), "iOS", "ua");
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
