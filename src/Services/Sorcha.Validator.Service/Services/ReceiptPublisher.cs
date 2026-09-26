// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Register;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Generates and stores the transaction receipts for a docket that has just been written to the
/// Register Service. THE one step every docket-write path calls (#1704).
/// </summary>
/// <remarks>
/// <para>
/// The validator has three paths that write a sealed docket to the Register Service —
/// <c>DocketBuildTriggerService</c> (the live seal path), <c>ValidatorOrchestrator</c> and
/// <c>DocketDistributor</c> (gRPC). Only the last generated receipts, so on every ordinary register
/// the <c>receipts</c> collection stayed empty and <c>GET …/verification-bundle</c> could never
/// succeed for any transaction. Same shape as #1370 (a projection drifting across parallel write
/// paths): the fix is one home, not a third copy.
/// </para>
/// <para>
/// Best-effort by design: a receipt failure is logged and never fails the docket write. The docket
/// is the ledger; a receipt is an attestation about it that can be produced again later.
/// </para>
/// </remarks>
public sealed class ReceiptPublisher : IReceiptPublisher
{
    private readonly IReceiptGenerator _receiptGenerator;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<ReceiptPublisher> _logger;

    /// <summary>Creates the publisher.</summary>
    public ReceiptPublisher(
        IReceiptGenerator receiptGenerator,
        IRegisterServiceClient registerClient,
        ILogger<ReceiptPublisher> logger)
    {
        _receiptGenerator = receiptGenerator ?? throw new ArgumentNullException(nameof(receiptGenerator));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> PublishForDocketAsync(Models.Docket docket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(docket);

        try
        {
            var receipts = await _receiptGenerator.GenerateReceiptsForDocketAsync(docket, cancellationToken);
            if (receipts.Length == 0)
            {
                return true;
            }

            var stored = await _registerClient.WriteReceiptBatchAsync(
                docket.RegisterId, docket.DocketNumber, receipts, cancellationToken);
            if (!stored)
            {
                _logger.LogWarning(
                    "Register Service did not store the {Count} receipt(s) for docket {DocketNumber} on register {RegisterId}",
                    receipts.Length, docket.DocketNumber, docket.RegisterId);
            }

            return stored;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Intentional error boundary: a receipt must never fail the docket it attests to.
            _logger.LogWarning(ex,
                "Failed to publish receipts for docket {DocketNumber} on register {RegisterId}",
                docket.DocketNumber, docket.RegisterId);
            return false;
        }
    }
}
