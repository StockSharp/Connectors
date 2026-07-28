namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.CoinSwitch;
using StockSharp.CoinSwitch.Native;
using StockSharp.CoinSwitch.Native.Model;
using StockSharp.Messages;

[TestClass]
public class CoinSwitchTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedServiceAddresses()
	{
		var adapter = new CoinSwitchMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(CoinSwitchProductTypes.Spot, adapter.ProductType);
		AreEqual("coinswitchx", adapter.SpotExchange);
		AreEqual("https://coinswitch.co", adapter.RestEndpoint);
		AreEqual("https://dma.coinswitch.co", adapter.HftEndpoint);
		AreEqual("wss://ws.coinswitch.co", adapter.WebSocketEndpoint);
		AreEqual(TimeSpan.FromSeconds(10), adapter.PollingInterval);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new CoinSwitchMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "001122".Secure(),
			Secret = "aabbcc".Secure(),
			ProductType = CoinSwitchProductTypes.Options,
			SpotExchange = "c2c1",
			RestEndpoint = "https://rest.example.test/",
			HftEndpoint = "https://hft.example.test/",
			WebSocketEndpoint = "wss://ws.example.test/",
			PollingInterval = TimeSpan.FromSeconds(17),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new CoinSwitchMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("001122", target.Key.UnSecure());
		AreEqual("aabbcc", target.Secret.UnSecure());
		AreEqual(CoinSwitchProductTypes.Options, target.ProductType);
		AreEqual("c2c1", target.SpotExchange);
		AreEqual("https://rest.example.test", target.RestEndpoint);
		AreEqual("https://hft.example.test", target.HftEndpoint);
		AreEqual("wss://ws.example.test", target.WebSocketEndpoint);
		AreEqual(TimeSpan.FromSeconds(17), target.PollingInterval);
	}

	[TestMethod]
	public void SignerUsesPublishedEd25519Message()
	{
		const string seed =
			"000102030405060708090a0b0c0d0e0f" +
			"101112131415161718191a1b1c1d1e1f";
		using var signer = new CoinSwitchSigner(seed.Secure());

		AreEqual(
			"GET/trade/api/v2/time1700000000000",
			CoinSwitchSigner.CreateMessage(
				"GET",
				"/trade/api/v2/time",
				1700000000000));
		AreEqual(
			"36731d7e34c953365c30009db28780c18549a873f3cab07231b5d778d7360d8d" +
			"277d9f0ff50452fa9e785bfa07498c47e18bfdd94aabb5fa9e14960357024504",
			signer.Sign(
				"GET",
				"/trade/api/v2/time",
				1700000000000));
	}

	[TestMethod]
	public void SpotDiscoveryUsesExchangeKeyedPayloads()
	{
		const string coins =
			"{\"data\":{\"coinswitchx\":[\"BTC/INR\",\"ETH/USDT\"]}}";
		const string rules =
			"{\"data\":{\"coinswitchx\":{\"BTC/INR\":{" +
			"\"quote\":{\"min\":\"150\",\"max\":\"2500000\"}," +
			"\"precision\":{\"base\":6,\"quote\":2,\"limit\":0}}}}}";

		var symbols = CoinSwitchRestClient.DeserializeSpotSymbols(
			coins,
			"coinswitchx");
		var info = CoinSwitchRestClient.DeserializeSpotTradeInfo(
			rules,
			"coinswitchx");

		AreEqual(2, symbols.Length);
		AreEqual("BTC/INR", symbols[0]);
		AreEqual(0.000001m, info["BTC/INR"].VolumeStep);
		AreEqual(0.01m, info["BTC/INR"].PriceStep);
		AreEqual(150m, info["BTC/INR"].MinimumQuote);
	}

	[TestMethod]
	public void SpotTickerDepthTradesAndCandlesUsePublishedShapes()
	{
		const string tickerJson =
			"{\"data\":{\"BTC/INR\":{\"symbol\":\"BTC/INR\"," +
			"\"openPrice\":\"5100000\",\"lowPrice\":\"5000000\"," +
			"\"highPrice\":\"5200000\",\"lastPrice\":\"5150000\"," +
			"\"baseVolume\":\"12.5\",\"quoteVolume\":\"64000000\"," +
			"\"percentageChange\":\"0.98\",\"bidPrice\":\"5149000\"," +
			"\"askPrice\":\"5151000\",\"at\":1785208000000}}}";
		const string depthJson =
			"{\"data\":{\"symbol\":\"BTC/INR\"," +
			"\"timestamp\":1785208000100," +
			"\"bids\":[[\"5149000\",\"0.2\"]]," +
			"\"asks\":[[\"5151000\",\"0.3\"]]}}";
		const string tradesJson =
			"{\"data\":[{\"E\":1785208000200,\"m\":false," +
			"\"p\":\"5150000\",\"q\":\"0.01\",\"s\":\"BTC/INR\"," +
			"\"t\":\"trade-1\",\"e\":\"coinswitchx\"}]}";
		const string candlesJson =
			"{\"data\":[{\"o\":\"5100000\",\"h\":\"5200000\"," +
			"\"l\":\"5000000\",\"c\":\"5150000\",\"interval\":\"60\"," +
			"\"symbol\":\"BTC/INR\",\"close_time\":\"1785207600000\"," +
			"\"volume\":\"1.25\",\"start_time\":\"1785204000000\"}]}";

		var ticker = CoinSwitchRestClient.DeserializeSpotTicker(
			tickerJson,
			"BTC/INR");
		var depth = CoinSwitchRestClient.Deserialize<
			CoinSwitchDepth>(depthJson);
		var trades = CoinSwitchRestClient.Deserialize<
			CoinSwitchTrade[]>(tradesJson);
		var candles = CoinSwitchRestClient.Deserialize<
			CoinSwitchCandle[]>(candlesJson);

		AreEqual(5150000m, ticker.LastPrice);
		AreEqual(5149000m, ticker.BidPrice);
		AreEqual(0.2m, depth.Bids[0][1]);
		AreEqual("trade-1", trades[0].TradeId);
		AreEqual(Sides.Buy, trades[0].OriginSide);
		AreEqual(5100000m, candles[0].Open);
		AreEqual(1785204000000L, candles[0].StartTime);
	}

	[TestMethod]
	public void SpotOrderAndPortfolioUsePublishedShapes()
	{
		const string orderJson =
			"{\"data\":{\"order_id\":\"order-1\"," +
			"\"client_order_id\":\"client-1\",\"symbol\":\"BTC/USDT\"," +
			"\"price\":61000,\"average_price\":60990," +
			"\"orig_qty\":0.05,\"executed_qty\":0.02," +
			"\"status\":\"PARTIALLY_EXECUTED\",\"side\":\"BUY\"," +
			"\"exchange\":\"c2c1\",\"created_time\":1785208000000," +
			"\"updated_time\":1785208001000}}";
		const string portfolioJson =
			"{\"data\":[{\"currency\":\"ETH\"," +
			"\"blocked_balance_order\":\"0.0019\"," +
			"\"main_balance\":\"0.25\",\"buy_average_price\":110882.72}]}";

		var order = CoinSwitchRestClient.Deserialize<
			CoinSwitchSpotOrder>(orderJson);
		var balances = CoinSwitchRestClient.Deserialize<
			CoinSwitchSpotBalance[]>(portfolioJson);

		AreEqual(OrderStates.Active, order.Status.ToSpotOrderState());
		AreEqual(0.03m, order.RemainingQuantity);
		AreEqual("client-1", order.ClientOrderId);
		AreEqual(0.25m, balances[0].Available);
		AreEqual(0.0019m, balances[0].Blocked);
	}

	[TestMethod]
	public void FuturesInstrumentUsesPublishedTradingRules()
	{
		const string json =
			"{\"data\":{\"BTCUSDT\":{\"symbol\":\"btc\"," +
			"\"base_asset\":\"btc\",\"quote_asset\":\"usdt\"," +
			"\"status\":\"TRADING\",\"type\":\"PERPETUAL_FUTURES\"," +
			"\"min_base_quantity\":\"0.001\"," +
			"\"base_quantity_step_size\":\"0.001\"," +
			"\"quantity_precision\":3,\"price_precision\":2," +
			"\"tick_size\":1,\"max_base_quantity\":\"952\"}}}";

		var instruments =
			CoinSwitchRestClient.DeserializeFuturesInstruments(json);
		var instrument = instruments[0];

		AreEqual("BTCUSDT", instrument.NativeSymbol);
		AreEqual("BTC/USDT", instrument.SecurityCode);
		AreEqual(0.01m, instrument.PriceStep);
		AreEqual(0.001m, instrument.VolumeStep);
		AreEqual(0.001m, instrument.MinimumVolume);
	}

	[TestMethod]
	public void FuturesMarketDataUsesExchangeEnvelope()
	{
		const string tickerJson =
			"{\"data\":{\"EXCHANGE_2\":{\"last_price\":\"95136.60\"," +
			"\"symbol\":\"BTCUSDT\",\"exchange\":\"EXCHANGE_2\"," +
			"\"timestamp\":1785208000000," +
			"\"best_ask_price\":\"95136.70\"," +
			"\"best_bid_price\":\"95136.60\"," +
			"\"mark_price\":95136.7,\"index_price\":95046.53," +
			"\"funding_rate\":0.00039681," +
			"\"open_interest\":\"67529.878\"}}}";
		const string tradesJson =
			"{\"data\":[{\"E\":1785208000100,\"p\":0.39391," +
			"\"q\":133,\"e\":\"EXCHANGE_2\"," +
			"\"s\":\"DOGEUSDT\",\"m\":true}]}";

		var ticker = CoinSwitchRestClient.DeserializeFuturesTicker(
			tickerJson,
			"EXCHANGE_2");
		var trades = CoinSwitchRestClient.Deserialize<
			CoinSwitchTrade[]>(tradesJson);

		AreEqual(95136.60m, ticker.LastPrice);
		AreEqual(95136.7m, ticker.MarkPrice);
		AreEqual(0.00039681m, ticker.FundingRate);
		AreEqual(Sides.Sell, trades[0].OriginSide);
	}

	[TestMethod]
	public void FuturesOrderUsesTerminalPartialState()
	{
		const string json =
			"{\"data\":{\"order_id\":\"future-order-1\"," +
			"\"exchange\":\"EXCHANGE_2\",\"symbol\":\"DOGEUSDT\"," +
			"\"side\":\"BUY\",\"status\":\"PARTIALLY_EXECUTED\"," +
			"\"order_type\":\"LIMIT\",\"quantity\":\"22\"," +
			"\"exec_quantity\":\"12\",\"price\":\"0.28\"," +
			"\"avg_execution_price\":\"0.279\"," +
			"\"execution_fee\":\"0.0041\",\"reduce_only\":false," +
			"\"created_at\":1785208000000," +
			"\"updated_at\":1785208001000}}";

		var order = CoinSwitchRestClient.Deserialize<
			CoinSwitchFuturesOrder>(json);

		AreEqual(OrderStates.Done, order.Status.ToFuturesOrderState());
		AreEqual(10m, order.RemainingQuantity);
		AreEqual(0.279m, order.AverageExecutionPrice);
	}

	[TestMethod]
	public void HftOptionEnvelopeAndInstrumentAreSupported()
	{
		const string json =
			"{\"retCode\":0,\"retMsg\":\"OK\",\"result\":{" +
			"\"category\":\"option\",\"list\":[{" +
			"\"symbol\":\"BTC-29MAR26-60000-C\"," +
			"\"status\":\"Trading\",\"baseCoin\":\"BTC\"," +
			"\"quoteCoin\":\"USDT\",\"optionsType\":\"Call\"," +
			"\"launchTime\":\"1785208000000\"," +
			"\"deliveryTime\":\"1785294400000\"," +
			"\"priceFilter\":{\"tickSize\":\"0.1\"}," +
			"\"lotSizeFilter\":{\"minOrderQty\":\"0.01\"," +
			"\"maxOrderQty\":\"100\",\"qtyStep\":\"0.01\"}}]}}";

		var instruments =
			CoinSwitchRestClient.DeserializeHftInstruments(json);

		AreEqual(1, instruments.Length);
		AreEqual("BTC-29MAR26-60000-C", instruments[0].Symbol);
		AreEqual(OptionTypes.Call, instruments[0].OptionType);
		AreEqual(60000m, instruments[0].Strike);
		AreEqual(0.1m, instruments[0].PriceStep);
	}

	[TestMethod]
	public void SocketProtocolUsesSocketIoV4NamespaceFrames()
	{
		AreEqual(
			"wss://ws.coinswitch.co/pro/realtime-rates-socket/" +
			"spot/coinswitchx/?EIO=4&transport=websocket",
			CoinSwitchSocketProtocol.CreateEndpoint(
				"wss://ws.coinswitch.co",
				CoinSwitchProductTypes.Spot,
				"coinswitchx"));
		AreEqual(
			"42/coinswitchx,[\"FETCH_TRADES_CS_PRO\"," +
			"{\"event\":\"subscribe\",\"pair\":\"BTC,INR\"}]",
			CoinSwitchSocketProtocol.EncodeEvent(
				"/coinswitchx",
				"FETCH_TRADES_CS_PRO",
				new JObject
				{
					["event"] = "subscribe",
					["pair"] = "BTC,INR",
				}));

		IsTrue(CoinSwitchSocketProtocol.TryParseEvent(
			"42/coinswitchx,[\"FETCH_TRADES_CS_PRO\"," +
			"{\"E\":1,\"s\":\"BTC,INR\"}]",
			out var eventName,
			out var payload));
		AreEqual("FETCH_TRADES_CS_PRO", eventName);
		AreEqual("BTC,INR", payload.Value<string>("s"));
	}

	[TestMethod]
	public void SymbolsIntervalsAndStatesUseProductFormats()
	{
		AreEqual(
			"BTC,INR",
			"BTC/INR".ToCoinSwitchSocketSymbol(
				CoinSwitchProductTypes.Spot));
		AreEqual(
			"BTCUSDT",
			"BTC/USDT".ToCoinSwitchNativeSymbol(
				CoinSwitchProductTypes.Futures));
		AreEqual(
			"BTC/USDT",
			"BTCUSDT".ToCoinSwitchSecurityCode("USDT"));
		AreEqual(
			60,
			TimeSpan.FromHours(1).ToCoinSwitchInterval());
		AreEqual(
			OrderStates.Failed,
			"DISCARDED".ToSpotOrderState());
	}
}
