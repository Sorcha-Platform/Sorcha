// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sorcha.ServiceClients.Evm;

namespace Sorcha.ServiceClients.Tests.Evm;

/// <summary>Feature 182 (Phase 4) — write + query JSON-RPC methods for native ETH transacting.</summary>
public class EvmRpcClientWriteTests
{
    private const string Addr = "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf";

    [Fact]
    public async Task SendRawTransaction_Ok_ReturnsTxHash()
    {
        var client = Client(Result("\"0xabc123\""));
        var r = await client.SendRawTransactionAsync(11155111, "0x02f8...");
        r.Outcome.Should().Be(EvmRpcOutcome.Ok);
        r.TxHash.Should().Be("0xabc123");
    }

    [Fact]
    public async Task SendRawTransaction_RpcError_ReturnsError()
    {
        var client = Client("""{"jsonrpc":"2.0","id":1,"error":{"code":-32000,"message":"nonce too low"}}""");
        (await client.SendRawTransactionAsync(11155111, "0x02f8...")).Outcome.Should().Be(EvmRpcOutcome.Error);
    }

    [Fact]
    public async Task SendRawTransaction_UnconfiguredChain_NotConfigured_NoCall()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var client = new EvmRpcClient(new HttpClient(handler), Options.Create(new EvmRpcOptions { AllowPrivateAddresses = true }), NullLogger<EvmRpcClient>.Instance);
        var r = await client.SendRawTransactionAsync(11155111, "0x02");
        r.Outcome.Should().Be(EvmRpcOutcome.NotConfigured);
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTransactionCount_DecodesHexNonce()
    {
        var r = await Client(Result("\"0x3\"")).GetTransactionCountAsync(11155111, Addr);
        r.Outcome.Should().Be(EvmRpcOutcome.Ok);
        r.Value.Should().Be(3);
    }

    [Fact]
    public async Task EstimateGas_Decodes21000()
    {
        var r = await Client(Result("\"0x5208\"")).EstimateGasAsync(11155111, Addr, Addr, "0x64", "0x");
        r.Value.Should().Be(21000);
    }

    [Fact]
    public async Task MaxPriorityFee_And_ChainId_Decode()
    {
        (await Client(Result("\"0x59682f00\"")).GetMaxPriorityFeePerGasAsync(11155111)).Value.Should().Be(1500000000);
        (await Client(Result("\"0xaa36a7\"")).GetChainIdAsync(11155111)).Value.Should().Be(11155111);
    }

    [Fact]
    public async Task BaseFee_FromPendingBlock_Decodes()
    {
        var r = await Client(Result("""{"number":"0x10","baseFeePerGas":"0x7"}""")).GetBaseFeePerGasAsync(11155111);
        r.Outcome.Should().Be(EvmRpcOutcome.Ok);
        r.Value.Should().Be(7);
    }

    [Fact]
    public async Task BaseFee_MissingField_ReturnsError()
    {
        var r = await Client(Result("""{"number":"0x10"}""")).GetBaseFeePerGasAsync(11155111);
        r.Outcome.Should().Be(EvmRpcOutcome.Error);
    }

    [Fact]
    public async Task Receipt_NullResult_IsPending()
    {
        var r = await Client("""{"jsonrpc":"2.0","id":1,"result":null}""").GetTransactionReceiptAsync(11155111, "0xdead");
        r.Outcome.Should().Be(EvmRpcOutcome.Ok);
        r.Receipt.Should().BeNull();
    }

    [Fact]
    public async Task Receipt_Success_MapsMined()
    {
        var r = await Client(Result("""{"status":"0x1","blockNumber":"0x4e2","gasUsed":"0x5208"}""")).GetTransactionReceiptAsync(11155111, "0xdead");
        r.Receipt.Should().NotBeNull();
        r.Receipt!.Success.Should().BeTrue();
        r.Receipt.BlockNumber.Should().Be(1250);
        r.Receipt.GasUsed.Should().Be(21000);
    }

    [Fact]
    public async Task Receipt_Reverted_MapsFailure()
    {
        var r = await Client(Result("""{"status":"0x0","blockNumber":"0x4e2","gasUsed":"0x5208"}""")).GetTransactionReceiptAsync(11155111, "0xdead");
        r.Receipt!.Success.Should().BeFalse();
    }

    private static string Result(string resultJson) => $$"""{"jsonrpc":"2.0","id":1,"result":{{resultJson}}}""";

    private static EvmRpcClient Client(string body)
    {
        var options = new EvmRpcOptions { AllowPrivateAddresses = true };
        options.Rpc["11155111"] = "https://sepolia.rpc.example.test/";
        return new EvmRpcClient(
            new HttpClient(new StubHandler(HttpStatusCode.OK, body)),
            Options.Create(options),
            NullLogger<EvmRpcClient>.Instance);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
