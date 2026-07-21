// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.TransactionHandler.Enums;

/// <summary>
/// Represents the status of a transaction operation.
/// </summary>
public enum TransactionStatus
{
    /// <summary>
    /// Operation completed successfully
    /// </summary>
    Success = 0,

    /// <summary>
    /// Invalid signature detected
    /// </summary>
    InvalidSignature = 1,

    /// <summary>
    /// Invalid payload detected
    /// </summary>
    InvalidPayload = 2,

    /// <summary>
    /// Invalid metadata detected
    /// </summary>
    InvalidMetadata = 3,

    /// <summary>
    /// Invalid recipients detected
    /// </summary>
    InvalidRecipients = 4,

    /// <summary>
    /// Transaction is not signed
    /// </summary>
    NotSigned = 5,

    /// <summary>
    /// Serialization operation failed
    /// </summary>
    SerializationFailed = 6,

    /// <summary>
    /// Transaction version is not supported
    /// </summary>
    VersionNotSupported = 7,

    /// <summary>
    /// Access denied to payload
    /// </summary>
    AccessDenied = 8,

    /// <summary>
    /// The transaction is signed, but this transaction version cannot cryptographically
    /// verify the signature — verification is not implemented for it. This is a fail-closed
    /// result, never <see cref="Success"/>: a caller must treat it as "not verified", not
    /// "verified". The live validator path verifies via <c>ICryptoModule.VerifyAsync</c>;
    /// the V1 in-model verify is placeholder scaffolding with no production caller.
    /// </summary>
    VerificationNotImplemented = 9
}
