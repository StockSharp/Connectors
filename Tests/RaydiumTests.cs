namespace StockSharp.Connectors.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Raydium;
using StockSharp.Raydium.Native;
using StockSharp.Raydium.Native.Model;

/// <summary>
/// Tests for the Raydium connector.
/// </summary>
[TestClass]
public class RaydiumTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task TransactionRequestAcceptsVersionOne()
	{
		Task<string> body = null;
		using var http = new HttpClient(new HistoryHttpHandler(request =>
		{
			body = request.Content.ReadAsStringAsync(CancellationToken);
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent(
					"""{"jsonrpc":"2.0","id":1,"result":{"slot":123,"version":1,"blockTime":1760000000,"meta":{"err":null},"transaction":{"message":{"transactionConfig":{}}}}}"""),
			};
		}))
		{
			BaseAddress = new("https://rpc.example.test"),
		};
		using var client = new RaydiumRpcClient(http.BaseAddress.ToString(),
			RaydiumClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, http, "_httpClient");

		var transaction = await client.GetTransactionAsync(new string('1', 64), CancellationToken);
		var request = JObject.Parse(await body);

		AreEqual("getTransaction", request["method"].Value<string>());
		AreEqual(1, request["params"][1]["maxSupportedTransactionVersion"].Value<int>());
		AreEqual(123L, transaction.Slot);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new RaydiumMessageAdapter(new IncrementalIdGenerator())
	{
		// api.mainnet-beta.solana.com answers 429 to all but the first few getTransaction
		// calls, and the trade history reads one transaction per pool signature
		RpcEndpoint = "https://solana-rpc.publicnode.com",

		// pin both WSOL/USDC pools instead of taking whatever tops the volume ranking today
		Pools = "58oQChx4yWmvKdwLLZzBi4ChoCc2fqCUWBkwMihLYQo2|WSOL|USDC;" +
			"3ucNos4NbumPLZNWztqGHNFFgkHeRMBQAVemeeomsUxv|WSOL|USDC",
		MaximumDiscoveredPools = 0,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "WSOL-USDC",
		BoardCode = BoardCodes.Raydium,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Trades are rebuilt from the pool transaction log, which costs one JSON-RPC round trip
	/// per inspected signature, so a history request takes well over half a minute.
	/// </remarks>
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(90);
}
