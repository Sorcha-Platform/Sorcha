// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Moq;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Components.Forms.Controls;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.DeviceKeys;
using Sorcha.UI.Core.Services.HolderKeys;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms.Controls;

/// <summary>
/// Citizen OID4VP device-bound cnf (Phase 1, #1195). DeviceKeyRenderer must, on init, write THIS
/// DEVICE's public JWK into /holderJwk (the SD-JWT cnf key, Option A) while the wallet's DELIVERY
/// keys (encryptionPublicKey + algorithm) still come from the holder-keys endpoint. When the device
/// key is unavailable the renderer surfaces its error/retry state rather than binding a wrong key.
/// </summary>
public class DeviceKeyRendererTests : BunitContext
{
    private const string DeviceJwkJson =
        """{"kty":"EC","crv":"P-256","x":"DEVICE-X","y":"DEVICE-Y"}""";
    private const string WalletHolderJwkJson =
        """{"kty":"EC","crv":"P-256","x":"WALLET-X","y":"WALLET-Y"}""";

    private readonly Mock<IDeviceKeyProvider> _device = new();
    private readonly Mock<IHolderKeyClient> _holderKeys = new();

    public DeviceKeyRendererTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(_device.Object);
        Services.AddSingleton(_holderKeys.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static FormContext MakeContext() => new() { AllFieldsDisclosed = true };

    private static Control MakeControl() => new()
    {
        ControlType = ControlTypes.DeviceKey,
        Scope = "/deviceKeys",
        Title = "Device Keys"
    };

    private IRenderedComponent<Bunit.Rendering.ContainerFragment> RenderRenderer(FormContext ctx)
        => Render(builder =>
        {
            builder.OpenComponent<CascadingValue<FormContext>>(0);
            builder.AddAttribute(1, "Value", ctx);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenComponent<DeviceKeyRenderer>(0);
                inner.AddAttribute(1, "Control", MakeControl());
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        });

    [Fact]
    public void OnInit_WritesDeviceJwkToHolderJwkSlot_AndWalletDeliveryKeys()
    {
        var deviceJwk = JsonDocument.Parse(DeviceJwkJson).RootElement.Clone();
        _device.Setup(d => d.GetDevicePublicJwkAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync((JsonElement?)deviceJwk);
        _holderKeys.Setup(h => h.GetHolderKeysAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new HolderKeysView
                   {
                       HolderJwk = JsonDocument.Parse(WalletHolderJwkJson).RootElement.Clone(),
                       EncryptionPublicKey = "wallet-enc-key",
                       Algorithm = "NISTP256",
                       WalletAddress = "sorcha1citizen"
                   });

        var ctx = MakeContext();
        var cut = RenderRenderer(ctx);

        cut.WaitForAssertion(() =>
            cut.Markup.Should().Contain("device-key-captured"));

        // /holderJwk carries THE DEVICE KEY (Option A cnf), not the wallet's holder JWK.
        var holderJwk = ctx.FormData["/deviceKeys/holderJwk"].Should().BeOfType<JsonElement>().Subject;
        holderJwk.GetProperty("x").GetString().Should().Be("DEVICE-X",
            because: "the cnf holder-binding key must be the device key, not the wallet key");

        // Delivery keys stay the wallet's keys from the holder-keys endpoint.
        ctx.FormData["/deviceKeys/encryptionPublicKey"].Should().Be("wallet-enc-key");
        ctx.FormData["/deviceKeys/algorithm"].Should().Be("NISTP256");
    }

    [Fact]
    public void OnInit_NullDeviceKey_ShowsErrorState()
    {
        _device.Setup(d => d.GetDevicePublicJwkAsync(It.IsAny<CancellationToken>()))
               .ReturnsAsync((JsonElement?)null);
        _holderKeys.Setup(h => h.GetHolderKeysAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new HolderKeysView
                   {
                       HolderJwk = JsonDocument.Parse(WalletHolderJwkJson).RootElement.Clone(),
                       EncryptionPublicKey = "wallet-enc-key",
                       Algorithm = "NISTP256"
                   });

        var ctx = MakeContext();
        var cut = RenderRenderer(ctx);

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("device-key-error"));
        ctx.FormData.Should().NotContainKey("/deviceKeys/holderJwk",
            because: "no key material is written when the device key is unavailable");
    }
}
