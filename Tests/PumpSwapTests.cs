namespace StockSharp.Connectors.Tests;

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.PumpSwap;
using StockSharp.PumpSwap.Native;
using StockSharp.PumpSwap.Native.Model;

/// <summary>
/// Tests for the PumpSwap connector.
/// </summary>
[TestClass]
public class PumpSwapTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ARepeatedHistoryConnectionFailureEscapesAfterTheRetryLimit()
	{
		var attempts = 0;
		var expected = new HttpRequestException("Connection reset by peer.");
		using var client = new PumpSwapRpcClient("https://rpc.example.test", PumpSwapClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(_ =>
		{
			attempts++;
			throw expected;
		})) { BaseAddress = new("https://rpc.example.test") }, "_httpClient");

		var actual = await ThrowsAsync<HttpRequestException>(async () =>
			await client.GetTransactionAsync(new string('1', 64), CancellationToken));

		AreEqual(expected, actual);
		AreEqual(3, attempts);
	}

	[TestMethod]
	[DataRow(1)]
	[DataRow(2)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task TransactionHistoryRetriesAResetConnection(int failures)
	{
		var attempts = 0;
		using var client = new PumpSwapRpcClient("https://rpc.example.test", PumpSwapClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(_ =>
		{
			if (attempts++ < failures)
				throw new HttpRequestException("Connection reset by peer.", new IOException("Connection reset by peer."));
			return new(HttpStatusCode.OK) { Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":{"slot":123}}""") };
		})) { BaseAddress = new("https://rpc.example.test") }, "_httpClient");

		var transaction = await client.GetTransactionAsync(new string('1', 64), CancellationToken);

		AreEqual(123L, transaction.Slot);
		AreEqual(failures + 1, attempts);
	}

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
		using var client = new PumpSwapRpcClient(http.BaseAddress.ToString(),
			PumpSwapClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, http, "_httpClient");

		var transaction = await client.GetTransactionAsync(new string('1', 64), CancellationToken);
		var request = JObject.Parse(await body);

		AreEqual("getTransaction", request["method"].Value<string>());
		AreEqual(1, request["params"][1]["maxSupportedTransactionVersion"].Value<int>());
		AreEqual(123L, transaction.Slot);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new PumpSwapMessageAdapter(new IncrementalIdGenerator())
	{
		// api.mainnet-beta.solana.com answers 429 to all but the first few getTransaction
		// calls, and the trade history reads one transaction per pool signature
		RpcEndpoint = "https://solana-rpc.publicnode.com",

		// the shipped default pool trades a few times a day, so a tick or candle request
		// covering the last hour finds nothing at all
		Pools = "DPzKoJVewaH1wpchD3gWKeeGm7G2mXkBW48uRniAgbVx",
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "CUPSEY-WSOL",
		BoardCode = BoardCodes.PumpSwap,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Trades are rebuilt from the pool transaction log, which costs one JSON-RPC round trip
	/// per inspected signature, so a history request takes well over half a minute.
	/// </remarks>
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(90);
}
