// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Multi-node audit CRITICAL #2 — default implementation of
/// <see cref="IInboundCredentialStatusHandler"/>. Applies issuer-driven credential
/// lifecycle transitions to the holder's locally cached row when a
/// <c>TransactionType.CredentialStatusChange</c> tx arrives via the inbound
/// notification pipeline.
/// </summary>
public sealed class InboundCredentialStatusHandler : IInboundCredentialStatusHandler
{
    private readonly IRegisterServiceClient _registerClient;
    private readonly ICredentialStore _credentialStore;
    private readonly ILogger<InboundCredentialStatusHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public InboundCredentialStatusHandler(
        IRegisterServiceClient registerClient,
        ICredentialStore credentialStore,
        ILogger<InboundCredentialStatusHandler> logger)
    {
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InboundCredentialStatusResult> TryApplyAsync(
        string walletAddress,
        string transactionId,
        string registerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var tx = await _registerClient.GetTransactionAsync(registerId, transactionId, cancellationToken);
            if (tx is null)
            {
                return InboundCredentialStatusResult.Skipped;
            }

            // Only inspect credential-status txs — every other type is an Action carrying
            // a credential issuance, which is the Feature 106 detector's job.
            if (tx.MetaData?.TransactionType != TransactionType.CredentialStatusChange)
            {
                return InboundCredentialStatusResult.Skipped;
            }

            var rawPayload = tx.Payloads?.FirstOrDefault()?.Data;
            if (string.IsNullOrWhiteSpace(rawPayload))
            {
                _logger.LogDebug(
                    "CredentialStatusChange tx {TxId} has no payload data",
                    transactionId);
                return InboundCredentialStatusResult.Skipped;
            }

            byte[] payloadBytes;
            try
            {
                payloadBytes = Convert.FromBase64String(rawPayload);
            }
            catch (FormatException)
            {
                _logger.LogWarning(
                    "CredentialStatusChange tx {TxId} payload is not base64 — skipping",
                    transactionId);
                return InboundCredentialStatusResult.Skipped;
            }

            CredentialStatusChangePayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<CredentialStatusChangePayload>(payloadBytes, JsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "CredentialStatusChange tx {TxId} payload failed to deserialise — skipping",
                    transactionId);
                return InboundCredentialStatusResult.Skipped;
            }

            if (payload is null
                || string.IsNullOrWhiteSpace(payload.CredentialId)
                || string.IsNullOrWhiteSpace(payload.NewStatus)
                || string.IsNullOrWhiteSpace(payload.IssuerWallet)
                || string.IsNullOrWhiteSpace(payload.SubjectDid))
            {
                _logger.LogWarning(
                    "CredentialStatusChange tx {TxId} payload missing required fields — skipping",
                    transactionId);
                return InboundCredentialStatusResult.Skipped;
            }

            // Recipient binding — payload must target this wallet.
            if (!string.Equals(payload.SubjectDid, walletAddress, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "CredentialStatusChange tx {TxId} not addressed to wallet {Wallet} (subject {Subject})",
                    transactionId, walletAddress, payload.SubjectDid);
                return InboundCredentialStatusResult.Skipped;
            }

            // Tx-level issuer binding — sender on the register tx must match the payload's
            // declared issuer. Cheap defence against a hostile sender forging a payload that
            // claims to be from a different issuer than the one who actually submitted.
            if (!string.IsNullOrEmpty(tx.SenderWallet)
                && !string.Equals(tx.SenderWallet, payload.IssuerWallet, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "CredentialStatusChange tx {TxId} sender {Sender} does not match payload issuer {Issuer} — dropping",
                    transactionId, tx.SenderWallet, payload.IssuerWallet);
                return InboundCredentialStatusResult.Skipped;
            }

            // Credential-level issuer binding — only the credential's original issuer may
            // change its status. Verifies against the locally stored credential row.
            var credential = await _credentialStore.GetByIdForWalletAsync(
                payload.CredentialId, walletAddress, cancellationToken);
            if (credential is null)
            {
                _logger.LogDebug(
                    "CredentialStatusChange tx {TxId}: no local credential {CredentialId} for wallet {Wallet}",
                    transactionId, payload.CredentialId, walletAddress);
                return InboundCredentialStatusResult.Skipped;
            }

            if (!string.Equals(credential.IssuerDid, payload.IssuerWallet, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "CredentialStatusChange tx {TxId}: payload issuer {Issuer} does not match credential {CredentialId} original issuer {Original} — dropping",
                    transactionId, payload.IssuerWallet, payload.CredentialId, credential.IssuerDid);
                return InboundCredentialStatusResult.Skipped;
            }

            if (!TryParseStatus(payload.NewStatus, out var newStatus))
            {
                _logger.LogWarning(
                    "CredentialStatusChange tx {TxId}: unsupported status '{Status}' — only Active / Suspended / Revoked are propagated",
                    transactionId, payload.NewStatus);
                return InboundCredentialStatusResult.Skipped;
            }

            var updated = await _credentialStore.UpdateStatusAsync(
                payload.CredentialId, walletAddress, newStatus, cancellationToken);
            if (!updated)
            {
                _logger.LogInformation(
                    "CredentialStatusChange tx {TxId}: UpdateStatusAsync returned false for credential {CredentialId} (transition may be invalid or already applied)",
                    transactionId, payload.CredentialId);
                return InboundCredentialStatusResult.Skipped;
            }

            _logger.LogInformation(
                "Applied CredentialStatusChange tx {TxId}: credential {CredentialId} → {NewStatus} for wallet {Wallet}",
                transactionId, payload.CredentialId, newStatus, walletAddress);

            return new InboundCredentialStatusResult
            {
                Applied = true,
                CredentialId = payload.CredentialId,
                NewStatus = newStatus.ToString(),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "InboundCredentialStatusHandler: unexpected error processing tx {TxId} for wallet {Wallet}",
                transactionId, walletAddress);
            return InboundCredentialStatusResult.Skipped;
        }
    }

    /// <summary>
    /// Maps the wire-format status string to the wallet's local enum. Only the three
    /// issuer-driven transitions are propagated; <c>Expired</c> / <c>Consumed</c> /
    /// <c>PendingAcceptance</c> / <c>Declined</c> are local-lifecycle states owned by
    /// the holder's node and never carried in a CredentialStatusChange tx.
    /// </summary>
    private static bool TryParseStatus(string raw, out CredentialStatus status)
    {
        switch (raw.Trim())
        {
            case "Active":
                status = CredentialStatus.Active;
                return true;
            case "Suspended":
                status = CredentialStatus.Suspended;
                return true;
            case "Revoked":
                status = CredentialStatus.Revoked;
                return true;
            default:
                status = CredentialStatus.Active;
                return false;
        }
    }
}
