namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.FluidDex;
using StockSharp.FluidDex.Native;
using StockSharp.FluidDex.Native.Model;
using StockSharp.Messages;

/// <summary>
/// Tests for the FluidDex connector.
/// </summary>
[TestClass]
public class FluidDexTests : LiveMarketDataTestBase
{
	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task RpcHistoryWaitsForTheServersRetryAfter()
	{
		var attempts = 0;
		await using var server = new HistoryHttpServer(() =>
		{
			var response = new HttpResponseMessage(++attempts == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)
			{
				Content = new StringContent("""{"jsonrpc":"2.0","id":1,"result":[]}"""),
			};
			if (attempts == 1)
				response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
			return response;
		}, CancellationToken);
		using var adapter = new FluidDexMessageAdapter(new IncrementalIdGenerator());
		using var client = new FluidDexRpcClient(server.Address.AbsoluteUri, FluidDexChains.Ethereum, adapter.FactoryAddress, adapter.ResolverAddress, null, null);

		await client.GetLogsAsync(new FluidDexMarket { PoolId = "0x667701e51b4d1ca244f17c78f7ab8744b4c99f9b" }, 90, 100, CancellationToken);

		var times = server.RequestTimes.ToArray();
		AreEqual(2, times.Length);
		IsTrue(times[1] - times[0] >= TimeSpan.FromSeconds(1), "The native retry loop must wait for the full server delay.");
	}

	[TestMethod]
	[DataRow(1)]
	[DataRow(2)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task LogQueriesRetryTheSameRangeWhenAnRpcNodeLagsBehindItsHead(int failures)
	{
		var attempts = 0;
		using var client = CreateLogClient(payload =>
		{
			AreEqual("eth_getLogs", payload["method"].Value<string>());
			AreEqual("0x5a", payload["params"][0]["fromBlock"].Value<string>());
			AreEqual("0x64", payload["params"][0]["toBlock"].Value<string>());
			var response = new JObject { ["jsonrpc"] = "2.0", ["id"] = payload["id"] };
			if (attempts++ < failures)
				response["error"] = new JObject { ["code"] = -32602, ["message"] = "block range extends beyond current head block" };
			else
				response["result"] = new JArray();
			return response;
		});

		var logs = await client.GetLogsAsync(new FluidDexMarket { PoolId = "0x667701e51b4d1ca244f17c78f7ab8744b4c99f9b" }, 90, 100, CancellationToken);

		AreEqual(0, logs.Length);
		AreEqual(failures + 1, attempts);
	}

	[TestMethod]
	[DataRow("block range extends beyond current head block", 4)]
	[DataRow("invalid address", 1)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task LogRetryIsBoundedAndDoesNotHideInvalidParameters(string message, int expectedAttempts)
	{
		var attempts = 0;
		using var client = CreateLogClient(payload =>
		{
			attempts++;
			return new JObject
			{
				["jsonrpc"] = "2.0", ["id"] = payload["id"],
				["error"] = new JObject { ["code"] = -32602, ["message"] = message },
			};
		});

		await ThrowsAsync<InvalidOperationException>(async () =>
			await client.GetLogsAsync(new FluidDexMarket { PoolId = "0x667701e51b4d1ca244f17c78f7ab8744b4c99f9b" }, 90, 100, CancellationToken));

		AreEqual(expectedAttempts, attempts);
	}

	private FluidDexRpcClient CreateLogClient(Func<JObject, JObject> response)
	{
		using var adapter = new FluidDexMessageAdapter(new IncrementalIdGenerator());
		var client = new FluidDexRpcClient("https://rpc.example.test", FluidDexChains.Ethereum, adapter.FactoryAddress, adapter.ResolverAddress, null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(request =>
		{
			var payload = JObject.Parse(request.Content.ReadAsStringAsync(CancellationToken).GetAwaiter().GetResult());
			return new(HttpStatusCode.OK) { Content = new StringContent(response(payload).ToString()) };
		})), "_http");
		return client;
	}

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
	// Allow a wider window when this pool has no swap in the latest two minutes.
	protected override int CandlesDepth => 10;

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2 == DataType.Ticks || message.DataType2.IsTFCandles)
			message.Count = 1;
	}
}
