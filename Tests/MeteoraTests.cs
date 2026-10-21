namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Meteora;
using StockSharp.Meteora.Native;
using StockSharp.Meteora.Native.Model;

/// <summary>
/// Tests for the Meteora connector.
/// </summary>
[TestClass]
public class MeteoraTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(false, false)]
	[DataRow(true, false)]
	[DataRow(false, true)]
	[DataRow(true, true)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task RpcRateLimitsHonorRetryAfter(bool useDate, bool useRpcError)
	{
		var timer = Stopwatch.StartNew();
		var requests = new List<TimeSpan>();
		using var client = new MeteoraRpcClient("https://rpc.example.test", MeteoraClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(_ =>
		{
			requests.Add(timer.Elapsed);
			if (requests.Count == 1)
			{
				var response = new HttpResponseMessage(useRpcError ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests)
				{
					Content = new StringContent("""{"jsonrpc":"2.0","id":null,"error":{"code":429,"message":"Connection rate limits exceeded"}}"""),
				};
				response.Headers.RetryAfter = useDate
					? new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(1))
					: new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
				return response;
			}
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent("""{"jsonrpc":"2.0","id":2,"result":{"context":{"slot":1},"value":[null]}}"""),
			};
		})) { BaseAddress = new("https://rpc.example.test") }, "_httpClient");

		var accounts = await client.GetAccountsAsync([new string('1', 32)], CancellationToken);

		AreEqual(1, accounts.Length);
		AreEqual(2, requests.Count);
		IsTrue(requests[1] - requests[0] >= TimeSpan.FromMilliseconds(950),
			$"RPC retried before Retry-After elapsed: {requests[1] - requests[0]}.");
	}

	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task ExhaustedRpcRateLimitsStillFail(bool useRpcError)
	{
		var requests = 0;
		using var client = new MeteoraRpcClient("https://rpc.example.test", MeteoraClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(_ =>
		{
			requests++;
			var response = new HttpResponseMessage(useRpcError ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests)
			{
				Content = new StringContent("""{"jsonrpc":"2.0","id":null,"error":{"code":429,"message":"Connection rate limits exceeded"}}"""),
			};
			response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(10));
			return response;
		})) { BaseAddress = new("https://rpc.example.test") }, "_httpClient");

		var error = await ThrowsAsync<InvalidOperationException>(async () =>
			await client.GetAccountsAsync([new string('1', 32)], CancellationToken));

		IsTrue(error.Message.Contains("429"), error.Message);
		AreEqual(4, requests);
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task RequestedTradeCountStopsReadingTransactionsWithinThePage()
	{
		using var bytes = new MemoryStream();
		using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
		{
			writer.Write(MeteoraExtensions.SwapDiscriminator);
			writer.Write(new byte[64]);
			writer.Write(0);
			writer.Write(0);
			writer.Write(1_000_000UL);
			writer.Write(2_000_000UL);
			writer.Write(true);
		}
		var log = "Program data: " + Convert.ToBase64String(bytes.ToArray());
		var reads = 0;
		using var client = new MeteoraRpcClient("https://rpc.example.test", MeteoraClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(request =>
		{
			var body = JObject.Parse(request.Content.ReadAsStringAsync(CancellationToken).GetAwaiter().GetResult());
			JToken result;
			if (body["method"].Value<string>() == "getSignaturesForAddress")
				result = new JArray(new JObject { ["signature"] = new string('1', 64), ["blockTime"] = 1760000000 },
					new JObject { ["signature"] = new string('1', 63) + "2", ["blockTime"] = 1760000000 },
					new JObject { ["signature"] = new string('1', 63) + "3", ["blockTime"] = 1760000000 });
			else
			{
				reads++;
				result = new JObject { ["slot"] = 123, ["blockTime"] = 1760000000, ["meta"] = new JObject { ["logMessages"] = new JArray(log) } };
			}
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = body["id"], ["result"] = result }.ToString()),
			};
		})) { BaseAddress = new("https://rpc.example.test") }, "_httpClient");
		using var adapter = new MeteoraMessageAdapter(new IncrementalIdGenerator());
		typeof(MeteoraMessageAdapter).GetField("_rpcClient", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(adapter, client);
		var market = new MeteoraMarket
		{
			PoolAddress = new string('1', 32),
			TokenX = new() { Decimals = 6 },
			TokenY = new() { Decimals = 6 },
		};
		var loading = (ValueTask<MeteoraTrade[]>)typeof(MeteoraMessageAdapter).GetMethod("LoadTradesAsync", BindingFlags.Instance | BindingFlags.NonPublic)
			.Invoke(adapter, [market, DateTime.UnixEpoch, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1, CancellationToken]);
		var trades = await loading;

		AreEqual(1, trades.Length);
		AreEqual(2m, trades[0].Price);
		AreEqual(1, reads);
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
		using var client = new MeteoraRpcClient(http.BaseAddress.ToString(),
			MeteoraClusters.Mainnet, null, null);
		HistoryHttpHandler.ReplaceClient(client, http, "_httpClient");

		var transaction = await client.GetTransactionAsync(new string('1', 64), CancellationToken);
		var request = JObject.Parse(await body);

		AreEqual("getTransaction", request["method"].Value<string>());
		AreEqual(1, request["params"][1]["maxSupportedTransactionVersion"].Value<int>());
		AreEqual(123L, transaction.Slot);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new MeteoraMessageAdapter(new IncrementalIdGenerator())
	{
		// pin the pool instead of taking whatever tops the volume ranking today
		Pools = "5rCf1DM8LjKTw4YqhnoLcngyZYeNnQqztScTogYHAS6|SOL|USDC",
		MaximumDiscoveredPools = 0,
	};

	// Allow connection setup to honor the public Solana RPC Retry-After response.
	protected override TimeSpan ConnectTimeout => TimeSpan.FromSeconds(60);

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SOL-USDC",
		BoardCode = BoardCodes.Meteora,
	};

	/// <inheritdoc />
	protected override int CandlesDepth => 2;

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2 == DataType.Ticks || message.DataType2.IsTFCandles)
			message.Count = 1;
	}
}
