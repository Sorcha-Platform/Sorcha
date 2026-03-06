// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using Moq;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for AlertDismissalService — per-user alert dismissal via localStorage.
/// </summary>
public class AlertDismissalServiceTests
{
    private readonly Mock<IJSRuntime> _jsRuntimeMock;
    private readonly AlertDismissalService _service;
    private readonly List<(string Method, string Key, string? Value)> _storageCalls = [];

    public AlertDismissalServiceTests()
    {
        _jsRuntimeMock = new Mock<IJSRuntime>();
        _service = new AlertDismissalService(_jsRuntimeMock.Object);

        // Capture setItem/removeItem calls for verification
        _jsRuntimeMock
            .Setup(js => js.InvokeAsync<IJSVoidResult>(It.IsAny<string>(), It.IsAny<object[]>()))
            .Callback<string, object[]>((method, args) =>
            {
                var key = args.Length > 0 ? args[0]?.ToString() : null;
                var value = args.Length > 1 ? args[1]?.ToString() : null;
                _storageCalls.Add((method, key!, value));
            })
            .ReturnsAsync(default(IJSVoidResult)!);
    }

    private void SetupLocalStorageEmpty()
    {
        _jsRuntimeMock
            .Setup(js => js.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync((string?)null);
    }

    private void SetupLocalStorageWith(params string[] alertIds)
    {
        var json = JsonSerializer.Serialize(alertIds.ToList());
        _jsRuntimeMock
            .Setup(js => js.InvokeAsync<string?>("localStorage.getItem", It.IsAny<object[]>()))
            .ReturnsAsync(json);
    }

    private static ServiceAlert CreateAlert(string id, string message = "Test") => new()
    {
        Id = id,
        Severity = AlertSeverity.Warning,
        Source = "test",
        Message = message,
        Timestamp = DateTimeOffset.UtcNow
    };

    #region DismissAlertAsync

    [Fact]
    public async Task DismissAlertAsync_EmptyStorage_SavesAlertId()
    {
        SetupLocalStorageEmpty();

        await _service.DismissAlertAsync("alert-1");

        var setCall = _storageCalls.Should().ContainSingle(c => c.Method == "localStorage.setItem").Subject;
        setCall.Key.Should().Be("sorcha_dismissed_alerts");
        setCall.Value.Should().Contain("alert-1");
    }

    [Fact]
    public async Task DismissAlertAsync_ExistingDismissals_AppendsAlertId()
    {
        SetupLocalStorageWith("alert-1");

        await _service.DismissAlertAsync("alert-2");

        var setCall = _storageCalls.Should().ContainSingle(c => c.Method == "localStorage.setItem").Subject;
        setCall.Value.Should().Contain("alert-1").And.Contain("alert-2");
    }

    [Fact]
    public async Task DismissAlertAsync_DuplicateId_DoesNotDuplicate()
    {
        SetupLocalStorageWith("alert-1");

        await _service.DismissAlertAsync("alert-1");

        var setCall = _storageCalls.Should().ContainSingle(c => c.Method == "localStorage.setItem").Subject;
        var ids = JsonSerializer.Deserialize<List<string>>(setCall.Value!);
        ids.Should().HaveCount(1);
    }

    #endregion

    #region IsAlertDismissedAsync

    [Fact]
    public async Task IsAlertDismissedAsync_DismissedAlert_ReturnsTrue()
    {
        SetupLocalStorageWith("alert-1", "alert-2");

        var result = await _service.IsAlertDismissedAsync("alert-1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsAlertDismissedAsync_UndismissedAlert_ReturnsFalse()
    {
        SetupLocalStorageWith("alert-1");

        var result = await _service.IsAlertDismissedAsync("alert-3");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsAlertDismissedAsync_EmptyStorage_ReturnsFalse()
    {
        SetupLocalStorageEmpty();

        var result = await _service.IsAlertDismissedAsync("alert-1");

        result.Should().BeFalse();
    }

    #endregion

    #region FilterDismissedAsync

    [Fact]
    public async Task FilterDismissedAsync_NoDismissals_ReturnsAllAlerts()
    {
        SetupLocalStorageEmpty();
        var alerts = new List<ServiceAlert> { CreateAlert("a1"), CreateAlert("a2") };

        var result = await _service.FilterDismissedAsync(alerts);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task FilterDismissedAsync_SomeDismissed_FiltersCorrectly()
    {
        SetupLocalStorageWith("a1");
        var alerts = new List<ServiceAlert> { CreateAlert("a1"), CreateAlert("a2"), CreateAlert("a3") };

        var result = await _service.FilterDismissedAsync(alerts);

        result.Should().HaveCount(2);
        result.Select(a => a.Id).Should().BeEquivalentTo(["a2", "a3"]);
    }

    [Fact]
    public async Task FilterDismissedAsync_AllDismissed_ReturnsEmpty()
    {
        SetupLocalStorageWith("a1", "a2");
        var alerts = new List<ServiceAlert> { CreateAlert("a1"), CreateAlert("a2") };

        var result = await _service.FilterDismissedAsync(alerts);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FilterDismissedAsync_EmptyAlerts_ReturnsEmpty()
    {
        SetupLocalStorageWith("a1");
        var alerts = new List<ServiceAlert>();

        var result = await _service.FilterDismissedAsync(alerts);

        result.Should().BeEmpty();
    }

    #endregion

    #region ClearDismissedAlertsAsync

    [Fact]
    public async Task ClearDismissedAlertsAsync_RemovesFromLocalStorage()
    {
        SetupLocalStorageWith("a1", "a2");

        await _service.ClearDismissedAlertsAsync();

        var removeCall = _storageCalls.Should().ContainSingle(c => c.Method == "localStorage.removeItem").Subject;
        removeCall.Key.Should().Be("sorcha_dismissed_alerts");
    }

    [Fact]
    public async Task ClearDismissedAlertsAsync_SubsequentFilter_ReturnsAllAlerts()
    {
        SetupLocalStorageWith("a1");

        // First filter should exclude a1
        var alerts = new List<ServiceAlert> { CreateAlert("a1"), CreateAlert("a2") };
        var filtered = await _service.FilterDismissedAsync(alerts);
        filtered.Should().HaveCount(1);

        // After clear, cache is reset — all alerts should pass
        await _service.ClearDismissedAlertsAsync();
        var filteredAfterClear = await _service.FilterDismissedAsync(alerts);
        filteredAfterClear.Should().HaveCount(2);
    }

    #endregion

    #region Cache Behavior

    [Fact]
    public async Task Service_CachesLocalStorageRead_OnlyReadsOnce()
    {
        SetupLocalStorageEmpty();

        await _service.IsAlertDismissedAsync("a1");
        await _service.IsAlertDismissedAsync("a2");
        await _service.IsAlertDismissedAsync("a3");

        _jsRuntimeMock.Verify(js => js.InvokeAsync<string?>(
            "localStorage.getItem",
            It.IsAny<object[]>()),
            Times.Once);
    }

    #endregion
}
