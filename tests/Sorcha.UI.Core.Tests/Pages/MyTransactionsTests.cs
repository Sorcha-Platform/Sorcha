// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Core.Models.Registers;
using Sorcha.UI.Core.Services;
using Sorcha.UI.Core.Services.Wallet;
using Sorcha.UI.Testing;
using Sorcha.UI.Testing.Builders;
using Xunit;
using MyTransactionsPage = Sorcha.UI.Web.Client.Pages.MyTransactions;

namespace Sorcha.UI.Core.Tests.Pages;

/// <summary>
/// Issue #1276 (UT-013) — My Transactions had no drill-down at all, and its "Sender" column showed
/// other people's wallet addresses. The second is not a data bug: the page is really "transactions
/// involving my wallet", so on a RECEIVED transaction the sender legitimately IS someone else.
/// Labelling that column "Sender" made the citizen's own history read as a list of strangers.
/// <para>
/// Both halves were already answerable from data the page had in hand. <c>Direction</c> and
/// <c>CounterpartyAddress</c> are computed by <c>MapToViewModel</c> for exactly this query and were
/// simply never rendered; and <c>GET /api/registers/{registerId}/transactions/{txId}</c> already
/// exists behind <see cref="ITransactionService.GetTransactionAsync"/>.
/// </para>
/// </summary>
public sealed class MyTransactionsTests : ComponentTestFixture
{
    private readonly Mock<ITransactionService> _transactions = new();
    private readonly Mock<IWalletApiService> _walletApi = new();

    private const string MyWallet = "ws11qzxrmine0000000000000000000000000000";
    private const string OtherWallet = "ws11qzxrtheirs00000000000000000000000000";
    private const string RegisterId = "020fa3d14ac44ad0b9d54f7d010be7f3";

    private static readonly TimeSpan LoadBudget = TimeSpan.FromSeconds(30);

    public MyTransactionsTests()
    {
        Services.AddSingleton(_transactions.Object);
        Services.AddSingleton(_walletApi.Object);
        // PayloadViewer's schema overlay. Production gets this from AddCoreServices →
        // AddRegisterServices, which the web client calls; the mock only stands in for the network.
        Services.AddSingleton(Mock.Of<IBlueprintSchemaService>());
        Services.AddSingleton(Mock.Of<ICurrentUserWalletService>());
        Services.AddSingleton(Mock.Of<IPayloadDecoderService>());

        _walletApi.Setup(w => w.GetWalletsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WalletDto> { new WalletDtoBuilder().WithAddress(MyWallet).Build() });
    }

    private static TransactionViewModel Tx(
        string txId, string sender, string? direction, string? counterparty,
        IReadOnlyList<PayloadViewModel>? payloads = null) => new()
    {
        TxId = txId,
        RegisterId = RegisterId,
        SenderWallet = sender,
        RecipientsWallets = sender == MyWallet ? [OtherWallet] : [MyWallet],
        TimeStamp = DateTime.UtcNow.AddHours(-2),
        DocketNumber = 14,
        Signature = "sig",
        Direction = direction,
        CounterpartyAddress = counterparty,
        Payloads = payloads ?? [],
    };

    // MudTablePager renders a MudSelect, whose popover needs a provider in the render tree —
    // without it MudPopoverService throws during OnInitializedAsync and the page never renders.
    private static readonly RenderFragment Page = builder =>
    {
        builder.OpenComponent<MudBlazor.MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<MyTransactionsPage>(1);
        builder.CloseComponent();
    };

    private void ListReturns(params TransactionViewModel[] txs) =>
        _transactions.Setup(t => t.QueryByWalletAsync(
                MyWallet, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionQueryResponse
            {
                Items = txs.Select(t => new TransactionQueryResultItem
                {
                    Transaction = t,
                    RegisterId = RegisterId,
                    RegisterName = "AIAS Assured Identity",
                }).ToList(),
                TotalCount = txs.Length,
            });

    [Fact]
    public void AReceivedTransaction_ReadsAsReceivedFromTheCounterparty()
    {
        ListReturns(Tx("aa11", sender: OtherWallet, direction: "Inbound", counterparty: OtherWallet));

        var cut = Render(Page);

        cut.WaitForAssertion(() =>
        {
            cut.Find("[data-testid='tx-direction-aa11']").TextContent.Should().Contain("Received");
            cut.Find("[data-testid='tx-counterparty-aa11']").Should().NotBeNull();
        }, LoadBudget);
    }

    [Fact]
    public void ASentTransaction_ReadsAsSent()
    {
        ListReturns(Tx("bb22", sender: MyWallet, direction: "Outbound", counterparty: OtherWallet));

        var cut = Render(Page);

        cut.WaitForAssertion(
            () => cut.Find("[data-testid='tx-direction-bb22']").TextContent.Should().Contain("Sent"),
            LoadBudget);
    }

    [Fact]
    public void TheTableNoLongerLabelsAColumnSender()
    {
        ListReturns(Tx("cc33", sender: OtherWallet, direction: "Inbound", counterparty: OtherWallet));

        var cut = Render(Page);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Counterparty");
            // "Sender" named the wrong thing on a page of the citizen's OWN transactions: on a
            // received transaction it is a stranger's address, which is what made the list read as
            // other people's business.
            cut.Markup.Should().NotContain(">Sender<");
        }, LoadBudget);
    }

    [Fact]
    public void AMissingCounterparty_RendersAPlaceholder_NotABlankCell()
    {
        // A control/genesis transaction has no recipients, so MapToViewModel leaves the
        // counterparty null. A blank cell reads as a rendering fault rather than an absence.
        ListReturns(Tx("dd44", sender: MyWallet, direction: "Outbound", counterparty: null));

        var cut = Render(Page);

        cut.WaitForAssertion(
            () => cut.Find("[data-testid='tx-counterparty-dd44']").TextContent.Trim()
                .Should().NotBeNullOrWhiteSpace(),
            LoadBudget);
    }

    [Fact]
    public void OpeningARow_FetchesTheFullTransaction_AndShowsWhatWasSubmitted()
    {
        ListReturns(Tx("ee55", sender: MyWallet, direction: "Outbound", counterparty: OtherWallet));

        // The list projection carries no payloads — the whole point of the drill-down is that it
        // goes back for the caller's own decrypted disclosure group.
        _transactions.Setup(t => t.GetTransactionAsync(RegisterId, "ee55", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tx("ee55", MyWallet, "Outbound", OtherWallet, payloads:
            [
                new PayloadViewModel
                {
                    Index = 0,
                    Hash = "h",
                    IsAccessible = true,
                    DecodedContent = "{\"postcode\":\"EH9 1JA\"}",
                    IsJson = true,
                    PrettyJson = "{\n  \"postcode\": \"EH9 1JA\"\n}",
                },
            ]));

        var cut = Render(Page);

        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid='tx-open-ee55']").Should().ContainSingle(),
            LoadBudget);

        cut.Find("[data-testid='tx-open-ee55']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='tx-detail-ee55']").Should().ContainSingle(
                "opening a row must reveal that transaction's detail");
            cut.Markup.Should().Contain("EH9 1JA",
                "the citizen opened their own transaction to see what it actually said");
        }, LoadBudget);

        _transactions.Verify(
            t => t.GetTransactionAsync(RegisterId, "ee55", It.IsAny<CancellationToken>()), Times.Once);
    }
}
