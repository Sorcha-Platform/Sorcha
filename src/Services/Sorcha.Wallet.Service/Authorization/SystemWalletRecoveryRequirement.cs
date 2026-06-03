// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Authorization;

namespace Sorcha.Wallet.Service.Authorization;

/// <summary>
/// Authorization requirement for the system-wallet <c>recover</c> operation (BIP39 import that seats
/// a validator docket-signing wallet). Satisfied by <see cref="SystemWalletRecoveryAuthorizationHandler"/>
/// for either a service-tier caller or a platform-tier administrator. Marker type — carries no state.
/// </summary>
public sealed class SystemWalletRecoveryRequirement : IAuthorizationRequirement
{
}
