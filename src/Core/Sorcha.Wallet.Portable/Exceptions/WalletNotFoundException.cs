// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Exceptions;

/// <summary>
/// Thrown when a wallet or wallet access grant is not found.
/// </summary>
public class WalletNotFoundException : Exception
{
    public WalletNotFoundException(string message) : base(message) { }
    public WalletNotFoundException(string message, Exception inner) : base(message, inner) { }
}
