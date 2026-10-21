namespace StockSharp.Connectors.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.FluidDex;
using StockSharp.FluidDex.Native;
using StockSharp.Messages;

/// <summary>
/// Tests for the FluidDex connector.
/// </summary>
[TestClass]
public class FluidDexTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(990, 0, 990)]
	[DataRow(990, 1, 991)]
	[DataRow(999, 0, 999)]
	[DataRow(1000, 0, 1000)]
	[DataRow(1001, 0, 1000)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task RecentBlockSearchStaysWithinRetainedHistory(int block, int seconds, int expected)
	{
		using var client = CreateClient(600);
		var result = await client.FindBlockAsync(DateTime.UnixEpoch.AddSeconds(block * 12 + seconds), 1000, CancellationToken);
		AreEqual(new BigInteger(expected), result);
	}

	[TestMethod]
	[DataRow(0, 0, 0)]
	[DataRow(0, 1, 1)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ArchiveSearchStillIncludesGenesis(int block, int seconds, int expected)
	{
		using var client = CreateClient(0);
		var result = await client.FindBlockAsync(DateTime.UnixEpoch.AddSeconds(block * 12 + seconds), 1000, CancellationToken);
		AreEqual(new BigInteger(expected), result);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task UnavailableOldHistoryStillFails()
	{
		using var client = CreateClient(600);
		var error = await ThrowsAsync<InvalidOperationException>(async () => await client.FindBlockAsync(DateTime.UnixEpoch.AddSeconds(500 * 12), 1000, CancellationToken));
		IsTrue(error.Message.Contains("pruned history"));
	}

	private FluidDexRpcClient CreateClient(int earliest)
	{
		using var adapter = new FluidDexMessageAdapter(new IncrementalIdGenerator());
		var client = new FluidDexRpcClient("https://rpc.example.test", FluidDexChains.Ethereum, adapter.FactoryAddress, adapter.ResolverAddress, null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(request =>
		{
			var payload = JObject.Parse(request.Content.ReadAsStringAsync(CancellationToken).GetAwaiter().GetResult());
			var height = Convert.ToInt32(payload["params"][0].Value<string>()[2..], 16);
			var response = new JObject { ["jsonrpc"] = "2.0", ["id"] = payload["id"] };
			if (height < earliest)
				response["error"] = new JObject { ["code"] = 4444, ["message"] = "pruned history unavailable" };
			else
				response["result"] = new JObject { ["timestamp"] = "0x" + (height * 12).ToString("x") };
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToString()) };
		})), "_http");
		return client;
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new FluidDexMessageAdapter(new IncrementalIdGenerator())
	{
		Pools = "0x667701e51b4d1ca244f17c78f7ab8744b4c99f9b",

		// The default public RPC serves recent logs without an archive subscription.
		HistoryBlockCount = 50,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "USDT-USDC-F7",
		BoardCode = BoardCodes.FluidDex,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Candles are rebuilt from swap logs: the start block is found by bisecting the chain and
	/// every matched log costs another block read.
	/// </remarks>
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(60);

	/// <inheritdoc />
	protected override int CandlesDepth => 2;

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2 == DataType.Ticks || message.DataType2.IsTFCandles)
			message.Count = 1;
	}
}
