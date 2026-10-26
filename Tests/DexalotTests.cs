namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Dexalot;
using StockSharp.Dexalot.Native;
using StockSharp.Dexalot.Native.Model;
using StockSharp.Messages;

/// <summary>
/// Tests for the Dexalot connector.
/// </summary>
[TestClass]
public class DexalotTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(1, false)]
	[DataRow(2, false)]
	[DataRow(10, false)]
	[DataRow(1, true)]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandleHistoryReadsPublicCandlesBeforeFinishing(int maximum, bool closesDuringDownload)
	{
		var from = new DateTime(2024, 1, 2, 10, 5, 0, DateTimeKind.Utc);
		using var adapter = new ClockedAdapter
		{
			Time = from.AddMinutes(closesDuringDownload ? 4 : 30).AddSeconds(59),
		};
		using var rest = new DexalotRestClient("https://api.example.test/privapi", null, null);
		using var socket = new DexalotSocketClient("wss://ws.example.test/");
		using var rpc = new DexalotEvmClient("https://rpc.example.test/", null, null);
		var requests = 0;
		using var http = new HttpClient(new HistoryHttpHandler(request =>
		{
			AreEqual("/api/trading/candle-chunk", request.RequestUri.AbsolutePath);
			IsTrue(request.RequestUri.Query.Contains("pair=ALOT%2FUSDC"));
			IsTrue(request.RequestUri.Query.Contains("intervalnum=5"));
			IsTrue(request.RequestUri.Query.Contains("intervalstr=minutes"));
			AreEqual("https://app.example.test", request.Headers.GetValues("Origin").Single());
			IsFalse(request.Headers.Contains("x-signature"));
			requests++;
			if (closesDuringDownload)
				adapter.Time = from.AddMinutes(5).AddSeconds(1);
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent("""
				[
				 {"date":"2024-01-02T10:15:00Z","open":"40","high":"42","low":"39","close":"41","volume":"4"},
				 {"date":"2024-01-02T10:10:00Z","open":"30","high":"32","low":"29","close":"31","volume":"3"},
				 {"date":"2024-01-02T10:07:00Z","open":null,"high":null,"low":null,"close":null,"volume":null},
				 {"date":"2024-01-02T10:05:00Z","open":"20","high":"22","low":"19","close":"21","volume":"2"},
				 {"date":"2024-01-02T10:00:00Z","open":"10","high":"12","low":"9","close":"11","volume":"1"}
				]
				"""),
			};
		}));
		HistoryHttpHandler.ReplaceClient(rest, http, "_http");
		const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
		typeof(DexalotMessageAdapter).GetField("_restClient", fields).SetValue(adapter, rest);
		typeof(DexalotMessageAdapter).GetField("_socketClient", fields).SetValue(adapter, socket);
		typeof(DexalotMessageAdapter).GetField("_evmClient", fields).SetValue(adapter, rpc);
		var pairs = (Dictionary<string, DexalotPair>)typeof(DexalotMessageAdapter)
			.GetField("_pairs", fields).GetValue(adapter);
		pairs.Add("ALOT/USDC", Pair());
		var messages = new List<Message>();
		adapter.NewOutMessageAsync += (message, _) =>
		{
			messages.Add(message);
			return default;
		};
		await adapter.SendInMessageAsync(new MarketDataMessage
		{
			TransactionId = 42,
			IsSubscribe = true,
			SecurityId = Pair().ToStockSharp(),
			DataType2 = TimeSpan.FromMinutes(5).TimeFrame(),
			From = from,
			To = from.AddMinutes(5),
			Count = maximum,
		}, CancellationToken);

		AreEqual(1, requests, "Historical candles must not depend on the next WebSocket trade.");
		IsFalse(messages.OfType<SubscriptionResponseMessage>().Any(message => message.Error is not null));
		var candles = messages.OfType<TimeFrameCandleMessage>().ToArray();
		AreEqual(Math.Min(maximum, 2), candles.Length);
		AreEqual(from, candles[0].OpenTime);
		AreEqual(20m, candles[0].OpenPrice);
		AreEqual(2m, candles[0].TotalVolume);
		var expectedState = closesDuringDownload ? CandleStates.Active : CandleStates.Finished;
		IsTrue(candles.All(candle => candle.OriginalTransactionId == 42 && candle.State == expectedState),
			"A candle that was forming when the download started must remain active.");
		var finished = messages.OfType<SubscriptionFinishedMessage>().Single();
		AreEqual(42L, finished.OriginalTransactionId);
		IsTrue(messages.IndexOf(finished) > messages.IndexOf(candles[^1]));
	}

	[TestMethod]
	public void EmptyCandleBucketsDoNotBecomeFabricatedCandles()
	{
		var candles = DexalotMessageAdapter.ParseCandles(JArray.Parse("""
			[{"date":"2024-01-02T10:00:00Z","open":null,"high":null,"low":null,"close":null,"volume":null}]
			"""));

		AreEqual(0, candles.Length);
	}

	[TestMethod]
	public void IncompleteNonemptyCandlesAreRejected()
		=> ThrowsExactly<InvalidDataException>(() => DexalotMessageAdapter.ParseCandles(JArray.Parse("""
			[{"date":"2024-01-02T10:00:00Z","open":"10","high":null,"low":"9","close":"11","volume":"1"}]
			""")));

	[TestMethod]
	[DataRow(5, 5, "minutes")]
	[DataRow(15, 15, "minutes")]
	[DataRow(30, 30, "minutes")]
	[DataRow(60, 1, "hours")]
	[DataRow(240, 4, "hours")]
	[DataRow(1440, 1, "day")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task PublicCandleRequestsUseTheDocumentedInterval(int minutes, int interval, string unit)
	{
		using var rest = new DexalotRestClient("https://api.example.test/privapi", null, null);
		using var http = new HttpClient(new HistoryHttpHandler(request =>
		{
			AreEqual("/api/trading/candle-chunk", request.RequestUri.AbsolutePath);
			var query = request.RequestUri.Query.TrimStart('?').Split('&')
				.Select(part => part.Split('=', 2))
				.ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
			AreEqual("ALOT/USDC", query["pair"]);
			AreEqual(interval.ToString(CultureInfo.InvariantCulture), query["intervalnum"]);
			AreEqual(unit, query["intervalstr"]);
			AreEqual("500", query["count"]);
			AreEqual("https://app.example.test", request.Headers.GetValues("Origin").Single());
			IsFalse(request.Headers.Contains("x-signature"));
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
		}));
		HistoryHttpHandler.ReplaceClient(rest, http, "_http");

		var candles = await rest.GetCandlesAsync("ALOT/USDC", TimeSpan.FromMinutes(minutes), CancellationToken);

		AreEqual(0, candles.Count);
	}

	private sealed class ClockedAdapter() : DexalotMessageAdapter(new IncrementalIdGenerator())
	{
		public DateTime Time { get; set; }
		public override DateTime CurrentTime => Time;
	}

	[TestMethod]
	public void DefaultsUsePublishedProductionEndpoints()
	{
		var adapter = new DexalotMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://api.dexalot.com/privapi",
			adapter.RestEndpoint);
		AreEqual("wss://api.dexalot.com/api/ws",
			adapter.WebSocketEndpoint);
		AreEqual(
			"https://subnets.avax.network/dexalot/mainnet/rpc",
			adapter.RpcEndpoint);
		AreEqual(100, adapter.OrderBookDepth);
		AreEqual(TimeSpan.FromSeconds(5),
			adapter.PrivatePollingInterval);
		AreEqual(TimeSpan.FromMinutes(2), adapter.ReceiptTimeout);
		AreEqual(DexalotSelfTradePrevention.CancelTaker,
			adapter.SelfTradePrevention);
		AreEqual(6, DexalotMessageAdapter.AllTimeFrames.Count());
	}

	[TestMethod]
	public async Task SettingsRoundTripKeepsEndpointsWalletAndLimits()
	{
		var source = new DexalotMessageAdapter(
			new IncrementalIdGenerator())
		{
			RestEndpoint = "https://rest.example.test/",
			WebSocketEndpoint = "wss://ws.example.test/",
			RpcEndpoint = "https://rpc.example.test/",
			TradePairsAddress =
				"0x1111111111111111111111111111111111111111",
			PortfolioAddress =
				"0x2222222222222222222222222222222222222222",
			WalletAddress =
				"0x3333333333333333333333333333333333333333",
			PrivateKey = "secret".Secure(),
			Pairs = "ALOT/USDC;AVAX/USDC",
			OrderBookDepth = 250,
			PrivatePollingInterval = TimeSpan.FromSeconds(9),
			ReceiptTimeout = TimeSpan.FromMinutes(4),
			SelfTradePrevention =
				DexalotSelfTradePrevention.CancelBoth,
		};
		var storage = new SettingsStorage();
		await source.SaveAsync(storage, CancellationToken);

		var target = new DexalotMessageAdapter(
			new IncrementalIdGenerator());
		await target.LoadAsync(storage, CancellationToken);

		AreEqual("https://rest.example.test", target.RestEndpoint);
		AreEqual("wss://ws.example.test", target.WebSocketEndpoint);
		AreEqual("https://rpc.example.test", target.RpcEndpoint);
		AreEqual(source.TradePairsAddress, target.TradePairsAddress);
		AreEqual(source.PortfolioAddress, target.PortfolioAddress);
		AreEqual(source.WalletAddress, target.WalletAddress);
		AreEqual("secret", target.PrivateKey.UnSecure());
		AreEqual(source.Pairs, target.Pairs);
		AreEqual(250, target.OrderBookDepth);
		AreEqual(TimeSpan.FromSeconds(9),
			target.PrivatePollingInterval);
		AreEqual(TimeSpan.FromMinutes(4), target.ReceiptTimeout);
		AreEqual(DexalotSelfTradePrevention.CancelBoth,
			target.SelfTradePrevention);
	}

	[TestMethod]
	public void AbiAndWireUnitsAreDecodedWithoutPrecisionLoss()
	{
		AreEqual(
			"0x000000000000000000000000000000000000000000000000000000000000002a",
			DexalotEvmClient.CreateClientOrderId(42));
		AreEqual(24_300,
			0.0243m.ToBaseUnits(6));
		AreEqual(0.0243m,
			((System.Numerics.BigInteger)24_300).FromBaseUnits(6));

		var pair = Pair();
		var book = DexalotMessageAdapter.ParseBook(pair, JObject.Parse(
			"""
			{
			  "buyBook": [{ "prices": "24300,24200",
			    "quantities": "1000000000000000000,2500000000000000000" }],
			  "sellBook": [{ "prices": "24400",
			    "quantities": "3000000000000000000" }]
			}
			"""));

		AreEqual(2, book.Bids.Length);
		AreEqual(0.0243m, book.Bids[0].Price);
		AreEqual(1m, book.Bids[0].Volume);
		AreEqual(0.0244m, book.Asks[0].Price);
		AreEqual(3m, book.Asks[0].Volume);
	}

	[TestMethod]
	public void TradeAndCandleSnapshotsUseDocumentedSchema()
	{
		var trades = DexalotMessageAdapter.ParseTrades(JArray.Parse(
			"""
			[{"execId":2409590088,"price":"0.0243","quantity":"95.82",
			  "takerSide":0,"ts":"2026-07-28T04:44:30.000Z"}]
			"""));
		var candles = DexalotMessageAdapter.ParseCandles(JArray.Parse(
			"""
			[{"open":"0.024","high":"0.025","low":"0.023",
			  "close":"0.0243","date":"2026-07-28T04:00:00.000Z",
			  "volume":"1234.5"}]
			"""));

		AreEqual(1, trades.Length);
		AreEqual("2409590088", trades[0].Id);
		AreEqual(0.0243m, trades[0].Price);
		AreEqual(Sides.Buy, trades[0].Side);
		AreEqual(1, candles.Length);
		AreEqual(0.025m, candles[0].High);
		AreEqual(1234.5m, candles[0].Volume);
	}

	[TestMethod]
	public void TradesAreAggregatedIntoTimeFrameCandles()
	{
		var day = new DateTime(2026, 7, 28, 10, 0, 0,
			DateTimeKind.Utc);
		var candles = DexalotMessageAdapter.AggregateTrades(
		[
			new()
			{
				Id = "1",
				Time = day.AddMinutes(1),
				Price = 10,
				Volume = 2,
			},
			new()
			{
				Id = "2",
				Time = day.AddMinutes(3),
				Price = 12,
				Volume = 3,
			},
			new()
			{
				Id = "3",
				Time = day.AddMinutes(4),
				Price = 9,
				Volume = 4,
			},
			new()
			{
				Id = "4",
				Time = day.AddMinutes(6),
				Price = 11,
				Volume = 5,
			},
		], TimeSpan.FromMinutes(5));

		AreEqual(2, candles.Length);
		AreEqual(day, candles[0].OpenTime);
		AreEqual(10m, candles[0].Open);
		AreEqual(12m, candles[0].High);
		AreEqual(9m, candles[0].Low);
		AreEqual(9m, candles[0].Close);
		AreEqual(9m, candles[0].Volume);
		AreEqual(day.AddMinutes(5), candles[1].OpenTime);
		AreEqual(11m, candles[1].Close);
	}

	[TestMethod]
	[TestCategory("Integration")]
	public async Task LiveApisReturnPairsDeploymentsBookAndWebSocketTrades()
	{
		if (!Env("STOCKSHARP_LIVE_TESTS")
			.EqualsIgnoreCase("1"))
			Inconclusive("Set STOCKSHARP_LIVE_TESTS=1 for live API tests.");
		using var rest = new DexalotRestClient(
			"https://api.dexalot.com/privapi", null, null);
		var reference = await rest.LoadReferenceDataAsync(
			CancellationToken);
		var pair = reference.Pairs.Single(item =>
			item.Pair == "ALOT/USDC");
		using var rpc = new DexalotEvmClient(
			"https://subnets.avax.network/dexalot/mainnet/rpc",
			null, null);
		await rpc.VerifyAsync(CancellationToken);
		var book = await rpc.GetBookAsync(
			reference.TradePairs.Address, pair, 20, CancellationToken);

		using var socket = new DexalotSocketClient(
			"wss://api.dexalot.com/api/ws");
		var completion = new TaskCompletionSource<JObject>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		socket.MessageReceived += message =>
		{
			if (message.Value<string>("type")
				.EqualsIgnoreCase("lastTrade") &&
				message.Value<string>("pair")
					.EqualsIgnoreCase(pair.Pair))
				completion.TrySetResult(message);
		};
		await socket.ConnectAsync(CancellationToken);
		await socket.SubscribePairAsync(pair, true, CancellationToken);
		var tradeMessage = await completion.Task.WaitAsync(
			TimeSpan.FromSeconds(20), CancellationToken);

		IsGreater(reference.Pairs.Length, 10);
		AreEqual(432204L, reference.Network.ChainId);
		IsFalse(reference.TradePairs.Address.IsEmpty());
		IsFalse(reference.Portfolio.Address.IsEmpty());
		IsGreater(book.Bids.Length, 0);
		IsGreater(book.Asks.Length, 0);
		IsGreater(DexalotMessageAdapter.ParseTrades(
			tradeMessage["data"]).Length, 0);
	}

	private static DexalotPair Pair()
		=> new()
		{
			Environment = "production-multi-subnet",
			Pair = "ALOT/USDC",
			Base = "ALOT",
			Quote = "USDC",
			BaseDisplayDecimals = 2,
			QuoteDisplayDecimals = 4,
			BaseDecimals = 18,
			QuoteDecimals = 6,
			Status = "deployed",
		};

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new DexalotMessageAdapter(new IncrementalIdGenerator())
		{
			Pairs = "AVAX/USDC",
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "AVAX/USDC",
		BoardCode = BoardCodes.Dexalot,
	};

	/// <inheritdoc />
	protected override int CandlesDepth => 24 * 12;
}
