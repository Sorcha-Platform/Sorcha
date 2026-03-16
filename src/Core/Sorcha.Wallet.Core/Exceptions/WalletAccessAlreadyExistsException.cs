// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Exceptions;

/// <summary>
/// Thrown when an access grant already exists for a subject on a wallet.
/// </summary>
public class WalletAccessAlreadyExistsException : Exception
{
    public WalletAccessAlreadyExistsException(string message) : base(message) { }
    public WalletAccessAlreadyExistsException(string message, Exception inner) : base(message, inner) { }
}
