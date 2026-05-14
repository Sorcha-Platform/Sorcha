// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.UI.Components.User.Services.Signing;

namespace Sorcha.Wallet.Pwa.Services.Signing;

/// <summary>
/// v1 managed-mode implementation of <see cref="IUserSigner"/> (Feature 125,
/// T015). Wraps the existing device-key signing path —
/// <see cref="IDeviceKeyService"/> — so all four
/// <see cref="SigningOperation"/> classes route through the same WebCrypto
/// device key. The wallet's holder→device delegation credential is what
/// gives the device-key signature its on-chain authority; the holder-side
/// key never leaves the Wallet Service, matching the managed-mode contract.
/// </summary>
/// <remarks>
/// <para>
/// Self-custody (<c>SelfCustodyUserSigner</c>) and co-signed
/// (<c>CoSignedUserSigner</c>) implementations are scoped for v2 per Spec 2
/// §4 and FR-025 / FR-026. They will register against the same interface
/// with no UI rewrite required.
/// </para>
/// <para>
/// The display label is intentionally simple in v1 ("Sign with your Sorcha
/// Wallet"). PR-B (US3 — multi-context UI) wires <c>IUserContext</c> through
/// so the label can reflect the active organisation name.
/// </para>
/// </remarks>
public sealed class ManagedUserSigner : IUserSigner
{
    private readonly IDeviceKeyService _deviceKey;
    private readonly ILogger<ManagedUserSigner> _logger;

    /// <summary>Initialise a new managed-mode signer.</summary>
    public ManagedUserSigner(IDeviceKeyService deviceKey, ILogger<ManagedUserSigner> logger)
    {
        _deviceKey = deviceKey ?? throw new ArgumentNullException(nameof(deviceKey));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public UserCustodyMode CustodyMode => UserCustodyMode.Managed;

    /// <inheritdoc />
    public string DisplayLabel => "Sign with your Sorcha Wallet";

    /// <inheritdoc />
    public async Task<SigningResult> SignAsync(SigningRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.PayloadToSign is null || request.PayloadToSign.Length == 0)
        {
            return SigningResult.Fail(
                "ERR_USERSIGNER_NO_PAYLOAD",
                "Cannot sign an empty payload.");
        }

        try
        {
            var signature = await _deviceKey.SignAsync(request.PayloadToSign, ct).ConfigureAwait(false);
            return SigningResult.Ok(signature, "ES256");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Managed-mode device-key signing failed for operation {Operation}.", request.Operation);
            return SigningResult.Fail(
                "ERR_USERSIGNER_DEVICE_KEY_FAILED",
                "Couldn't complete the signature on this device. Try again or sign in on another device.");
        }
    }
}
