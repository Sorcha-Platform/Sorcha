// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Tests.CitizenWallet;

/// <summary>
/// Feature 114 / US5 PR3 — unit coverage for <see cref="CitizenPresentationStoreForwarder"/>:
/// the forwarder simply persists the mapped entry into <see cref="ICitizenPresentationStore"/>.
/// </summary>
public class CitizenPresentationStoreForwarderTests
{
    [Fact]
    public async Task ForwardAsync_CallsUpsertWithSameUserAndEntry()
    {
        var store = new Mock<ICitizenPresentationStore>();
        var forwarder = new CitizenPresentationStoreForwarder(store.Object);

        var platformUserId = Guid.NewGuid();
        var entry = new PresentationLogEntry
        {
            Id = Guid.NewGuid(),
            CredentialId = Guid.NewGuid(),
            VerifierLabel = "Strathcarron Council",
            DisclosedClaims = ["givenName"],
            PresentedAt = DateTimeOffset.UtcNow,
            Outcome = PresentationLogOutcome.Presented
        };

        await forwarder.ForwardAsync(platformUserId, entry);

        store.Verify(s => s.UpsertAsync(
            platformUserId,
            It.Is<PresentationLogEntry>(e => e.Id == entry.Id && e.CredentialId == entry.CredentialId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForwardAsync_NullEntry_Throws()
    {
        var forwarder = new CitizenPresentationStoreForwarder(Mock.Of<ICitizenPresentationStore>());

        await FluentActions.Invoking(() => forwarder.ForwardAsync(Guid.NewGuid(), null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }
}
