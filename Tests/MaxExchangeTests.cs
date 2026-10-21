namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.ComponentModel;
using Ecng.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.MaxExchange;
using StockSharp.MaxExchange.Native;
using StockSharp.MaxExchange.Native.Model;
using StockSharp.Messages;

/// <summary>
/// Tests for the MaxExchange connector.
/// </summary>
[TestClass]
public class MaxExchangeTests : LiveMarketDataTestBase
{
	[TestMethod]
	public void DefaultsUseOfficialServiceAddresses()
	{
		var adapter = new MaxExchangeMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://max-api.maicoin.com", adapter.RestEndpoint);
		AreEqual("wss://max-stream.maicoin.com/ws",
			adapter.WebSocketEndpoint);
	}

	[TestMethod]
	public async Task SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new MaxExchangeMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "public-key".Secure(),
			Secret = "private-secret".Secure(),
			RestEndpoint = "https://rest.example.test/",
			WebSocketEndpoint = "wss://stream.example.test/ws/",
		};
		var storage = new SettingsStorage();
		await source.SaveAsync(storage, CancellationToken);

		var target = new MaxExchangeMessageAdapter(
			new IncrementalIdGenerator());
		await target.LoadAsync(storage, CancellationToken);

		AreEqual("public-key", target.Key.UnSecure());
		AreEqual("private-secret", target.Secret.UnSecure());
		AreEqual("https://rest.example.test", target.RestEndpoint);
		AreEqual("wss://stream.example.test/ws",
			target.WebSocketEndpoint);
	}

	[TestMethod]
	public void SignatureMatchesOfficialSortedPayloadContract()
	{
		var values = new Dictionary<string, object>
		{
			["volume"] = "0.01",
			["side"] = "buy",
			["price"] = "2000000",
			["ord_type"] = "limit",
			["market"] = "btctwd",
		};

		var payload = MaxExchangeAuthenticator.CreatePayload(
			"/api/v3/wallet/spot/order", 1700000000123, values);

		AreEqual(
			"eyJtYXJrZXQiOiJidGN0d2QiLCJub25jZSI6MTcwMDAwMDAwMDEyMywib3JkX3R5cGUiOiJsaW1pdCIsInBhdGgiOiIvYXBpL3YzL3dhbGxldC9zcG90L29yZGVyIiwicHJpY2UiOiIyMDAwMDAwIiwic2lkZSI6ImJ1eSIsInZvbHVtZSI6IjAuMDEifQ==",
			payload);
		AreEqual(
			"ee00b6fa7cb57c2a8fd85cc6035a650b6d2446acb66e513fd6a932b395bb7ec0",
			MaxExchangeAuthenticator.CreateSignature(
				"test-secret", payload));
	}

	[TestMethod]
	public void OrderBodyUsesOfficialV3WireNames()
	{
		var json = MaxExchangeRestClient.SerializeBody(
			new MaxExchangePlaceOrderRequest
			{
				Market = "btctwd",
				Side = "buy",
				Volume = "0.01",
				Price = "2000000",
				ClientOid = "42",
				StopPrice = "1900000",
				OrderType = "stop_limit",
			});

		IsTrue(json.Contains("\"market\":\"btctwd\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"client_oid\":\"42\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"stop_price\":\"1900000\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"ord_type\":\"stop_limit\"",
			StringComparison.Ordinal));
		IsFalse(json.Contains("\"OrderType\":",
			StringComparison.Ordinal));
	}

	[TestMethod]
	public void PublicModelsUseCurrentV3Shape()
	{
		const string marketsJson =
			"[{\"id\":\"btctwd\",\"status\":\"active\"," +
			"\"base_unit\":\"btc\",\"base_unit_precision\":8," +
			"\"min_base_amount\":0.0001,\"quote_unit\":\"twd\"," +
			"\"quote_unit_precision\":1,\"min_quote_amount\":250," +
			"\"m_wallet_supported\":true}]";
		const string tickerJson =
			"{\"market\":\"btctwd\",\"at\":1728011558," +
			"\"buy\":\"1952078.0\",\"buy_vol\":\"0.02049401\"," +
			"\"sell\":\"1965418.4\",\"sell_vol\":\"0.00097219\"," +
			"\"open\":\"1966666.6\",\"low\":\"1944444.4\"," +
			"\"high\":\"1970531.2\",\"last\":\"1944444.4\"," +
			"\"vol\":\"0.00310063\",\"vol_in_btc\":\"0.00310063\"}";
		const string depthJson =
			"{\"timestamp\":1728011558,\"last_update_version\":1727932531262," +
			"\"last_update_id\":208377,\"asks\":[[\"1984599.0\",\"0.96152476\"]]," +
			"\"bids\":[[\"1952078.0\",\"0.02049401\"]]}";

		var markets = JsonConvert.DeserializeObject<
			MaxExchangeMarket[]>(marketsJson);
		var ticker = JsonConvert.DeserializeObject<
			MaxExchangeTicker>(tickerJson);
		var depth = JsonConvert.DeserializeObject<
			MaxExchangeOrderBook>(depthJson);

		AreEqual("BTC/TWD", markets[0].SecurityCode);
		AreEqual(8, markets[0].BasePrecision);
		AreEqual(1944444.4m, ticker.Last);
		AreEqual(0.02049401m, ticker.BidVolume);
		AreEqual(1984599m, depth.Asks[0][0]);
		AreEqual(0.02049401m, depth.Bids[0][1]);
	}

	[TestMethod]
	public void KlinesUseDocumentedArrayShape()
	{
		const string json =
			"[[1728010920,1944444.4,1944555.5,1944000.1," +
			"1944444.4,0.125]]";

		var candles = MaxExchangeRestClient.DeserializeKlines(json);

		AreEqual(1, candles.Length);
		AreEqual(1728010920L, candles[0].Timestamp);
		AreEqual(1944555.5m, candles[0].High);
		AreEqual(0.125m, candles[0].Volume);
	}

	[TestMethod]
	public void WebSocketModelsUseCompactEventEnvelope()
	{
		const string bookJson =
			"{\"c\":\"book\",\"M\":\"btctwd\",\"e\":\"snapshot\"," +
			"\"a\":[[\"2061921.5\",\"0.0005622\"]]," +
			"\"b\":[[\"2059999.5\",\"0.00018669\"]]," +
			"\"T\":1785196067053,\"fi\":2414003,\"li\":2414003," +
			"\"v\":1784968353087}";
		const string tradeJson =
			"{\"c\":\"trade\",\"M\":\"btctwd\",\"e\":\"update\"," +
			"\"t\":[{\"p\":\"2060753.8\",\"v\":\"0.0031235\"," +
			"\"T\":1785196032938,\"tr\":\"up\",\"rpi\":false}]," +
			"\"T\":1785196032964}";

		var book = MaxExchangeWsClient.DeserializeMessage<
			MaxExchangeBookEvent>(bookJson);
		var trades = MaxExchangeWsClient.DeserializeMessage<
			MaxExchangeTradeEvent>(tradeJson);

		AreEqual("snapshot", book.Event);
		AreEqual("btctwd", book.Market);
		AreEqual(2061921.5m, book.Asks[0][0]);
		AreEqual(2414003L, book.LastUpdateId);
		AreEqual("update", trades.Event);
		AreEqual(2060753.8m, trades.Trades[0].Price);
		AreEqual("up", trades.Trades[0].Trend);
	}

	[TestMethod]
	public void SymbolsIntervalsAndNegativePrecisionUseMaxFormat()
	{
		AreEqual("BTC/TWD", "btctwd".ToMaxExchangeSecurityCode(
			"btc", "twd"));
		AreEqual("btctwd", "BTC/TWD".ToMaxExchangeSymbol());
		AreEqual(new SecurityId
		{
			SecurityCode = "BTC/TWD",
			BoardCode = BoardCodes.MaxExchange,
		}, new MaxExchangeMarket
		{
			Id = "btctwd",
			BaseUnit = "btc",
			QuoteUnit = "twd",
		}.ToStockSharp());
		AreEqual("1m",
			TimeSpan.FromMinutes(1).ToMaxExchangeResolution());
		AreEqual("1d",
			TimeSpan.FromDays(1).ToMaxExchangeResolution());
		AreEqual(0.00000001m, MaxExchangeExtensions.GetStep(8));
		AreEqual(1000m, MaxExchangeExtensions.GetStep(-3));
	}

	[TestMethod]
	public void OrderConditionKeepsStopParameters()
	{
		var condition = new MaxExchangeOrderCondition
		{
			StopPrice = 65000.5m,
			PostOnly = true,
		};

		AreEqual(65000.5m, condition.StopPrice);
		IsTrue(condition.PostOnly);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new MaxExchangeMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/TWD",
		BoardCode = BoardCodes.MaxExchange,
	};

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000)]
	public async Task PublicWebSocketStreams()
	{
		var ticker = NewSignal();
		var book = NewSignal();
		var trade = NewSignal();
		var candle = NewSignal();
		var error = new TaskCompletionSource<Exception>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var client = new MaxExchangeWsClient(
			"wss://max-stream.maicoin.com/ws",
			new WorkingTime(),
			0);

		client.TickerReceived += (_, _) =>
		{
			ticker.TrySetResult();
			return default;
		};
		client.OrderBookReceived += (_, _) =>
		{
			book.TrySetResult();
			return default;
		};
		client.TradesReceived += (_, _) =>
		{
			trade.TrySetResult();
			return default;
		};
		client.KlineReceived += (_, _) =>
		{
			candle.TrySetResult();
			return default;
		};
		client.Error += (exception, _) =>
		{
			error.TrySetResult(exception);
			return default;
		};

		try
		{
			await client.ConnectAsync(CancellationToken);
			await client.SubscribeTickerAsync(
				"btctwd", CancellationToken);
			await client.SubscribeOrderBookAsync(
				"btctwd", 5, CancellationToken);
			await client.SubscribeTradesAsync(
				"btctwd", CancellationToken);
			await client.SubscribeKlineAsync(
				"btctwd", "1m", CancellationToken);

			var streams = Task.WhenAll(
				ticker.Task, book.Task, trade.Task, candle.Task);
			var timeout = Task.Delay(
				TimeSpan.FromSeconds(30), CancellationToken);
			var completed = await Task.WhenAny(
				streams, error.Task, timeout);

			if (completed == error.Task)
				throw await error.Task;
			if (completed != streams)
				Fail("MAX Exchange public WebSocket streams timed out.");
			await streams;
		}
		finally
		{
			await client.DisconnectAsync(CancellationToken);
			client.Dispose();
		}
	}

	private static TaskCompletionSource NewSignal()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);
}
