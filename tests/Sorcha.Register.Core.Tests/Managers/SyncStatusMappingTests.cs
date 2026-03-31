// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models.Enums;
using Xunit;

namespace Sorcha.Register.Core.Tests.Managers;

/// <summary>
/// Tests the mapping from peer sync state strings to RegisterStatus values.
/// This mapping is used by the Register Service when processing sync state updates
/// from the Peer Service to derive the appropriate register operational status.
/// </summary>
public class SyncStatusMappingTests
{
    /// <summary>
    /// Maps a sync state string and peer connection status to a RegisterStatus.
    /// Extracted from the Register Service endpoint logic for testability.
    /// </summary>
    private static RegisterStatus MapSyncStateToStatus(
        string syncState,
        bool peerConnectionActive,
        RegisterStatus currentStatus)
    {
        return syncState switch
        {
            "Subscribing" => RegisterStatus.Checking,
            "Syncing" => RegisterStatus.Recovery,
            "FullyReplicated" or "Active" => RegisterStatus.Online,
            "Error" => RegisterStatus.Offline,
            _ when !peerConnectionActive => RegisterStatus.Offline,
            _ => currentStatus
        };
    }

    [Theory]
    [InlineData("Subscribing", true, RegisterStatus.Online, RegisterStatus.Checking)]
    [InlineData("Subscribing", false, RegisterStatus.Offline, RegisterStatus.Checking)]
    [InlineData("Syncing", true, RegisterStatus.Online, RegisterStatus.Recovery)]
    [InlineData("Syncing", false, RegisterStatus.Online, RegisterStatus.Recovery)]
    [InlineData("FullyReplicated", true, RegisterStatus.Offline, RegisterStatus.Online)]
    [InlineData("FullyReplicated", false, RegisterStatus.Offline, RegisterStatus.Online)]
    [InlineData("Active", true, RegisterStatus.Offline, RegisterStatus.Online)]
    [InlineData("Active", false, RegisterStatus.Offline, RegisterStatus.Online)]
    [InlineData("Error", true, RegisterStatus.Online, RegisterStatus.Offline)]
    [InlineData("Error", false, RegisterStatus.Online, RegisterStatus.Offline)]
    public void MapSyncStateToStatus_KnownSyncStates_MapsToExpectedStatus(
        string syncState,
        bool peerConnectionActive,
        RegisterStatus currentStatus,
        RegisterStatus expectedStatus)
    {
        // Act
        var result = MapSyncStateToStatus(syncState, peerConnectionActive, currentStatus);

        // Assert
        result.Should().Be(expectedStatus);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Idle")]
    [InlineData("")]
    public void MapSyncStateToStatus_UnknownState_PeerInactive_ReturnsOffline(string syncState)
    {
        // Arrange
        var peerConnectionActive = false;
        var currentStatus = RegisterStatus.Online;

        // Act
        var result = MapSyncStateToStatus(syncState, peerConnectionActive, currentStatus);

        // Assert
        result.Should().Be(RegisterStatus.Offline);
    }

    [Theory]
    [InlineData(RegisterStatus.Offline)]
    [InlineData(RegisterStatus.Online)]
    [InlineData(RegisterStatus.Checking)]
    [InlineData(RegisterStatus.Recovery)]
    public void MapSyncStateToStatus_UnknownState_PeerActive_PreservesCurrentStatus(
        RegisterStatus currentStatus)
    {
        // Arrange
        var syncState = "SomeUnknownState";
        var peerConnectionActive = true;

        // Act
        var result = MapSyncStateToStatus(syncState, peerConnectionActive, currentStatus);

        // Assert
        result.Should().Be(currentStatus);
    }
}
