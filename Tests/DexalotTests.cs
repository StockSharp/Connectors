namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Dexalot;
using StockSharp.Dexalot.Native;
using StockSharp.Dexalot.Native.Model;
using StockSharp.Messages;

[TestClass]
public class DexalotTests : BaseTestClass
{
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
	public void SettingsRoundTripKeepsEndpointsWalletAndLimits()
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
		source.Save(storage);

		var target = new DexalotMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

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
		if (!Environment.GetEnvironmentVariable("STOCKSHARP_LIVE_TESTS")
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
}
