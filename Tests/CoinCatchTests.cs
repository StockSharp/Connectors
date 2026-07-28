namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinCatch;
using StockSharp.CoinCatch.Native;
using StockSharp.CoinCatch.Native.Model;
using StockSharp.Messages;

[TestClass]
public class CoinCatchTests : BaseTestClass
{
	[TestMethod]
	public void DefaultsUsePublishedServiceAddresses()
	{
		var adapter = new CoinCatchMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(CoinCatchProductTypes.Spot, adapter.ProductType);
		AreEqual("https://api.coincatch.com", adapter.RestEndpoint);
		AreEqual(
			"wss://ws.coincatch.com/public/v1/stream",
			adapter.PublicWebSocketEndpoint);
		AreEqual(
			"wss://ws.coincatch.com/private/v1/stream",
			adapter.PrivateWebSocketEndpoint);
		AreEqual(TimeSpan.FromSeconds(5), adapter.PollingInterval);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new CoinCatchMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "key".Secure(),
			Secret = "secret".Secure(),
			Passphrase = "pass".Secure(),
			ProductType = CoinCatchProductTypes.UsdtFutures,
			RestEndpoint = "https://rest.example.test/",
			PublicWebSocketEndpoint = "wss://public.example.test/",
			PrivateWebSocketEndpoint = "wss://private.example.test/",
			PollingInterval = TimeSpan.FromSeconds(13),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new CoinCatchMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("key", target.Key.UnSecure());
		AreEqual("secret", target.Secret.UnSecure());
		AreEqual("pass", target.Passphrase.UnSecure());
		AreEqual(CoinCatchProductTypes.UsdtFutures, target.ProductType);
		AreEqual("https://rest.example.test", target.RestEndpoint);
		AreEqual(
			"wss://public.example.test",
			target.PublicWebSocketEndpoint);
		AreEqual(
			"wss://private.example.test",
			target.PrivateWebSocketEndpoint);
		AreEqual(TimeSpan.FromSeconds(13), target.PollingInterval);
	}

	[TestMethod]
	public void AuthenticatorUsesPublishedPreHashAndHmac()
	{
		const string body =
			"{\"symbol\":\"TRXUSDT_SPBL\",\"side\":\"buy\"," +
			"\"orderType\":\"limit\",\"force\":\"normal\"," +
			"\"price\":\"0.046317\",\"quantity\":\"1212\"}";
		using var authenticator = new CoinCatchAuthenticator(
			"key".Secure(), "secret".Secure(), "pass".Secure());

		AreEqual(
			"1659927638003POST/api/spot/v1/trade/orders" + body,
			CoinCatchAuthenticator.CreatePreHash(
				1659927638003,
				"POST",
				"/api/spot/v1/trade/orders",
				string.Empty,
				body));
		AreEqual(
			"FOgh6oCC0h/JduIcjrUjysVgg7DwrLTIOBRYDcOhtfc=",
			authenticator.Sign(
				1659927638003,
				"POST",
				"/api/spot/v1/trade/orders",
				[],
				body));
	}

	[TestMethod]
	public void SpotSymbolsUsePublishedShape()
	{
		const string json =
			"{\"code\":\"00000\",\"data\":[{" +
			"\"symbol\":\"BTCUSDT_SPBL\",\"symbolName\":\"BTCUSDT\"," +
			"\"baseCoin\":\"BTC\",\"quoteCoin\":\"USDT\"," +
			"\"minTradeAmount\":\"0.0001\",\"maxTradeAmount\":\"100\"," +
			"\"priceScale\":2,\"quantityScale\":6," +
			"\"status\":\"online\"}]}";

		var symbol = CoinCatchRestClient.DeserializeSymbols(
			json, CoinCatchProductTypes.Spot)[0];

		AreEqual("BTCUSDT_SPBL", symbol.Symbol);
		AreEqual("BTC/USDT", symbol.SecurityCode);
		AreEqual(0.01m, symbol.PriceStep);
		AreEqual(0.000001m, symbol.VolumeStep);
		AreEqual(SecurityStates.Trading, symbol.Status.ToSecurityState());
	}

	[TestMethod]
	public void FuturesSymbolsUsePublishedShape()
	{
		const string json =
			"{\"code\":\"00000\",\"data\":[{" +
			"\"symbol\":\"BTCUSDT_UMCBL\",\"symbolName\":\"BTCUSDT\"," +
			"\"baseCoin\":\"BTC\",\"quoteCoin\":\"USDT\"," +
			"\"minTradeNum\":\"0.001\",\"priceEndStep\":\"5\"," +
			"\"pricePlace\":\"1\",\"volumePlace\":\"3\"," +
			"\"sizeMultiplier\":\"0.001\"," +
			"\"symbolType\":\"perpetual\",\"symbolStatus\":\"normal\"}]}";

		var symbol = CoinCatchRestClient.DeserializeSymbols(
			json, CoinCatchProductTypes.UsdtFutures)[0];

		AreEqual("BTCUSDT_UMCBL", symbol.Symbol);
		AreEqual("BTC/USDT", symbol.SecurityCode);
		AreEqual(0.5m, symbol.PriceStep);
		AreEqual(0.001m, symbol.VolumeStep);
		AreEqual(0.001m, symbol.MinimumTradeAmount);
		AreEqual(SecurityStates.Trading, symbol.Status.ToSecurityState());
	}

	[TestMethod]
	public void SpotMarketDataUsesPublishedShapes()
	{
		const string tickerJson =
			"{\"code\":\"00000\",\"data\":{\"symbol\":\"BTCUSDT_SPBL\"," +
			"\"high24h\":\"68000\",\"low24h\":\"65000\"," +
			"\"close\":\"67500\",\"baseVol\":\"12.5\"," +
			"\"quoteVol\":\"840000\",\"buyOne\":\"67499\"," +
			"\"sellOne\":\"67501\",\"bidSz\":\"0.4\",\"askSz\":\"0.3\"," +
			"\"ts\":1785208000000}}";
		const string depthJson =
			"{\"code\":\"00000\",\"data\":{\"asks\":[[\"67501\",\"0.3\"]]," +
			"\"bids\":[[\"67499\",\"0.4\"]],\"timestamp\":1785208000100}}";
		const string tradesJson =
			"{\"code\":\"00000\",\"data\":[{\"symbol\":\"BTCUSDT_SPBL\"," +
			"\"tradeId\":\"trade-1\",\"side\":\"buy\"," +
			"\"fillPrice\":\"67500\",\"fillQuantity\":\"0.01\"," +
			"\"fillTime\":1785208000200}]}";

		var ticker = CoinCatchRestClient.Deserialize<CoinCatchTicker>(
			tickerJson);
		var depth = CoinCatchRestClient.Deserialize<CoinCatchOrderBook>(
			depthJson);
		var trades = CoinCatchRestClient.Deserialize<CoinCatchTrade[]>(
			tradesJson);

		AreEqual(67500m, ticker.LastPrice);
		AreEqual(67499m, ticker.BidPrice);
		AreEqual(0.3m, depth.Asks[0].Size);
		AreEqual("trade-1", trades[0].TradeId);
		AreEqual(Sides.Buy, trades[0].Side.ToSide());
	}

	[TestMethod]
	public void FuturesMarketDataUsesPublishedShapes()
	{
		const string tickerJson =
			"{\"code\":\"00000\",\"data\":{\"symbol\":\"BTCUSDT_UMCBL\"," +
			"\"last\":\"67500\",\"bestAsk\":\"67501\",\"bestBid\":\"67499\"," +
			"\"baseVolume\":\"54.2\",\"quoteVolume\":\"3640000\"," +
			"\"indexPrice\":\"67495\",\"fundingRate\":\"0.0001\"," +
			"\"holdingAmount\":\"120.5\",\"timestamp\":1785208000000}}";
		const string tradesJson =
			"{\"code\":\"00000\",\"data\":[{\"symbol\":\"BTCUSDT_UMCBL\"," +
			"\"tradeId\":\"future-trade-1\",\"side\":\"sell\"," +
			"\"price\":\"67500\",\"size\":\"0.02\"," +
			"\"timestamp\":1785208000100}]}";
		const string candlesJson =
			"{\"code\":\"00000\",\"data\":[[1785207600000,\"67000\"," +
			"\"67600\",\"66900\",\"67500\",\"4.5\",\"303000\"]]}";

		var ticker = CoinCatchRestClient.Deserialize<CoinCatchTicker>(
			tickerJson);
		var trades = CoinCatchRestClient.Deserialize<CoinCatchTrade[]>(
			tradesJson);
		var candles = CoinCatchRestClient.DeserializeCandles(
			candlesJson, CoinCatchProductTypes.UsdtFutures);

		AreEqual(67500m, ticker.LastPrice);
		AreEqual(67495m, ticker.IndexPrice);
		AreEqual(120.5m, ticker.OpenInterest);
		AreEqual(Sides.Sell, trades[0].Side.ToSide());
		AreEqual(67000m, candles[0].Open);
		AreEqual(303000m, candles[0].QuoteVolume);
	}

	[TestMethod]
	public void SpotTradingUsesPublishedShapes()
	{
		const string orderJson =
			"{\"code\":\"00000\",\"data\":[{" +
			"\"symbol\":\"BTCUSDT_SPBL\",\"orderId\":\"order-1\"," +
			"\"clientOrderId\":\"ss-42\",\"price\":\"67000\"," +
			"\"quantity\":\"0.05\",\"fillPrice\":\"66990\"," +
			"\"fillQuantity\":\"0.02\",\"orderType\":\"limit\"," +
			"\"side\":\"buy\",\"status\":\"partial_fill\"," +
			"\"cTime\":1785208000000,\"uTime\":1785208001000}]}";
		const string balanceJson =
			"{\"code\":\"00000\",\"data\":[{\"coinName\":\"USDT\"," +
			"\"available\":\"250\",\"frozen\":\"10\",\"lock\":\"2\"," +
			"\"uTime\":1785208000000}]}";

		var order = CoinCatchRestClient.Deserialize<CoinCatchOrder[]>(
			orderJson)[0];
		var balance = CoinCatchRestClient.Deserialize<CoinCatchBalance[]>(
			balanceJson)[0];

		AreEqual(OrderStates.Active, order.Status.ToOrderState());
		AreEqual(0.03m, order.RemainingQuantity);
		AreEqual(42L, CoinCatchExtensions.ParseTransactionId(
			order.ClientOrderId));
		AreEqual(250m, balance.Available);
		AreEqual(12m, balance.Blocked);
	}

	[TestMethod]
	public void FuturesTradingUsesPublishedShapes()
	{
		const string orderJson =
			"{\"code\":\"00000\",\"data\":{\"nextFlag\":false," +
			"\"orderList\":[{\"symbol\":\"BTCUSDT_UMCBL\",\"size\":\"1\"," +
			"\"orderId\":\"future-order-1\",\"clientOid\":\"ss-84\"," +
			"\"filledQty\":\"0.4\",\"price\":\"67500\"," +
			"\"priceAvg\":\"67490\",\"state\":\"partially_filled\"," +
			"\"side\":\"open_long\",\"timeInForce\":\"normal\"," +
			"\"marginCoin\":\"USDT\",\"orderType\":\"limit\"," +
			"\"cTime\":1785208000000,\"uTime\":1785208001000}]}}";
		const string positionJson =
			"{\"code\":\"00000\",\"data\":[{" +
			"\"symbol\":\"BTCUSDT_UMCBL\",\"marginCoin\":\"USDT\"," +
			"\"holdSide\":\"long\",\"available\":\"0.4\",\"locked\":\"0.1\"," +
			"\"total\":\"0.5\",\"averageOpenPrice\":\"67000\"," +
			"\"unrealizedPL\":\"250\",\"liquidationPrice\":\"52000\"," +
			"\"leverage\":\"10\",\"uTime\":1785208001000}]}";

		var order = CoinCatchRestClient.DeserializeOrderPage(orderJson)
			.Orders[0];
		var position = CoinCatchRestClient.Deserialize<
			CoinCatchPosition[]>(positionJson)[0];

		AreEqual(OrderStates.Active, order.Status.ToOrderState());
		AreEqual(0.6m, order.RemainingQuantity);
		AreEqual(Sides.Buy, order.Side.ToSide());
		AreEqual(0.5m, position.Total);
		AreEqual(250m, position.UnrealizedProfit);
	}

	[TestMethod]
	public void WebSocketProtocolUsesProductSpecificArguments()
	{
		AreEqual(
			"{\"op\":\"subscribe\",\"args\":[{\"instType\":\"SP\"," +
			"\"channel\":\"ticker\",\"instId\":\"BTCUSDT\"}]}",
			CoinCatchWsClient.CreateSubscriptionJson(
				true,
				CoinCatchProductTypes.Spot,
				"ticker",
				"BTCUSDT_SPBL"));
		AreEqual(
			"{\"op\":\"unsubscribe\",\"args\":[{\"instType\":\"MC\"," +
			"\"channel\":\"books15\",\"instId\":\"BTCUSDT\"}]}",
			CoinCatchWsClient.CreateSubscriptionJson(
				false,
				CoinCatchProductTypes.UsdtFutures,
				"books15",
				"BTCUSDT_UMCBL"));
	}

	[TestMethod]
	public void SymbolsIntervalsAndBoardsUseProductFormats()
	{
		AreEqual(
			"BTCUSDT",
			"BTCUSDT_SPBL".ToCoinCatchWebSocketSymbol());
		AreEqual(
			"BTC/USDT",
			CoinCatchExtensions.CreateSecurityCode("BTC", "USDT"));
		AreEqual(
			"1h",
			TimeSpan.FromHours(1).ToCoinCatchGranularity());
		AreEqual(
			"candle1H",
			TimeSpan.FromHours(1).ToCoinCatchWebSocketChannel());
		AreEqual(
			BoardCodes.CoinCatchFutUsdt,
			CoinCatchProductTypes.UsdtFutures.ToBoardCode());
	}
}
