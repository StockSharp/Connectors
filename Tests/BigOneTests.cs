namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

using Ecng.Common;
using Ecng.ComponentModel;
using Ecng.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.BigOne;
using StockSharp.BigOne.Native;
using StockSharp.BigOne.Native.Model;
using StockSharp.Messages;

/// <summary>
/// Tests for the BigOne connector.
/// </summary>
[TestClass]
public class BigOneTests : LiveMarketDataTestBase
{
	[TestMethod]
	public void DefaultsUseOfficialServiceAddresses()
	{
		var adapter = new BigOneMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://api.big.one/api/v3",
			adapter.SpotRestEndpoint);
		AreEqual("wss://api.big.one/ws/v2",
			adapter.SpotWebSocketEndpoint);
		AreEqual("https://api.big.one/api/contract/v2",
			adapter.ContractRestEndpoint);
		AreEqual("wss://api.big.one/ws/contract/v2",
			adapter.ContractWebSocketEndpoint);
		AreEqual("wss://api.big.one/ws/contract/v2/stream",
			adapter.ContractPrivateWebSocketEndpoint);
	}

	[TestMethod]
	public async Task SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new BigOneMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "public-key".Secure(),
			Secret = "private-secret".Secure(),
			SpotRestEndpoint = "https://spot.example.test/api/v3/",
			SpotWebSocketEndpoint = "wss://spot.example.test/ws/",
			ContractRestEndpoint =
				"https://contract.example.test/api/v2/",
			ContractWebSocketEndpoint =
				"wss://contract.example.test/public/",
			ContractPrivateWebSocketEndpoint =
				"wss://contract.example.test/private/",
		};
		var storage = new SettingsStorage();
		await source.SaveAsync(storage, CancellationToken);

		var target = new BigOneMessageAdapter(
			new IncrementalIdGenerator());
		await target.LoadAsync(storage, CancellationToken);

		AreEqual("public-key", target.Key.UnSecure());
		AreEqual("private-secret", target.Secret.UnSecure());
		AreEqual("https://spot.example.test/api/v3",
			target.SpotRestEndpoint);
		AreEqual("wss://spot.example.test/ws",
			target.SpotWebSocketEndpoint);
		AreEqual("https://contract.example.test/api/v2",
			target.ContractRestEndpoint);
		AreEqual("wss://contract.example.test/public",
			target.ContractWebSocketEndpoint);
		AreEqual("wss://contract.example.test/private",
			target.ContractPrivateWebSocketEndpoint);
	}

	[TestMethod]
	public void SpotJwtUsesOpenApiV2PayloadAndValidSignature()
	{
		var token = BigOneAuthenticator.CreateSpotToken(
			"api-key", "secret", "123456789");
		var parts = token.Split('.');

		AreEqual(3, parts.Length);
		var header = JObject.Parse(Decode(parts[0]));
		var payload = JObject.Parse(Decode(parts[1]));
		AreEqual("HS256", header.Value<string>("alg"));
		AreEqual("JWT", header.Value<string>("typ"));
		AreEqual("OpenAPIV2", payload.Value<string>("type"));
		AreEqual("api-key", payload.Value<string>("sub"));
		AreEqual("123456789", payload.Value<string>("nonce"));
		AreEqual(parts[2], BigOneAuthenticator.Sign(
			$"{parts[0]}.{parts[1]}", "secret"));
	}

	[TestMethod]
	public void ContractJwtUsesCurrentPayloadAndValidSignature()
	{
		var token = BigOneAuthenticator.CreateContractToken(
			"api-key", "secret", 123456789, 123456);
		var parts = token.Split('.');
		var payload = JObject.Parse(Decode(parts[1]));

		AreEqual("api-key", payload.Value<string>("sub"));
		AreEqual(123456789L, payload.Value<long>("nonce"));
		AreEqual(123456L, payload.Value<long>("iat"));
		AreEqual(123516L, payload.Value<long>("exp"));
		AreEqual(parts[2], BigOneAuthenticator.Sign(
			$"{parts[0]}.{parts[1]}", "secret"));
	}

	[TestMethod]
	public void SpotModelsUseCurrentEnvelope()
	{
		const string json =
			"{\"code\":0,\"data\":[{\"id\":\"pair-id\"," +
			"\"name\":\"BTC-USDT\",\"base_scale\":8," +
			"\"quote_scale\":2,\"base_asset\":{\"symbol\":\"BTC\"," +
			"\"name\":\"Bitcoin\"},\"quote_asset\":{\"symbol\":\"USDT\"," +
			"\"name\":\"Tether\"},\"min_quote_value\":\"5\"}]}";
		var pairs = BigOneRestClient.DeserializeSpot<
			BigOneSpotPair[]>(json);

		AreEqual(1, pairs.Length);
		AreEqual("BTC/USDT", pairs[0].SecurityCode);
		AreEqual(0.01m, pairs[0].PriceStep);
		AreEqual(0.00000001m, pairs[0].VolumeStep);
		AreEqual(5m, pairs[0].MinimumQuoteValue);
	}

	[TestMethod]
	public void SpotMarketModelsUseDocumentedFields()
	{
		const string tickerJson =
			"{\"code\":0,\"data\":{\"asset_pair_name\":\"BTC-USDT\"," +
			"\"bid\":{\"price\":\"63698.81\",\"quantity\":\"0.001429\"," +
			"\"order_count\":1},\"ask\":{\"price\":\"63700.00\"," +
			"\"quantity\":\"0.002\"},\"open\":\"65181.04\"," +
			"\"close\":\"63705.67\",\"high\":\"65717.99\"," +
			"\"low\":\"63550.02\",\"volume\":\"4952.855003\"," +
			"\"daily_change\":\"-1475.37\"}}";
		const string tradeJson =
			"{\"code\":0,\"data\":[{\"id\":38199941," +
			"\"price\":\"63705.67\",\"amount\":\"0.019812\"," +
			"\"taker_side\":\"ASK\"," +
			"\"inserted_at\":\"2026-07-28T01:02:03.123Z\"}]}";

		var ticker = BigOneRestClient.DeserializeSpot<
			BigOneSpotTicker>(tickerJson);
		var trades = BigOneRestClient.DeserializeSpot<
			BigOneSpotTrade[]>(tradeJson);

		AreEqual(63698.81m, ticker.Bid.Price);
		AreEqual(0.001429m, ticker.Bid.Amount);
		AreEqual(63705.67m, ticker.Close);
		AreEqual("38199941", trades[0].Id);
		AreEqual("ASK", trades[0].TakerSide);
	}

	[TestMethod]
	public void ContractModelsHandleCurrentResponseShapes()
	{
		const string instrumentsJson =
			"{\"value\":[{\"symbol\":\"BTCUSD\"," +
			"\"latestPrice\":63614.6,\"markPrice\":63639.67," +
			"\"indexPrice\":63641.15,\"fundingRate\":-0.0001365," +
			"\"volume24h\":16075560,\"openInterest\":1065687}]}";
		const string depthJson =
			"{\"bids\":{\"63606.9\":126028},\"asks\":{\"63607.1\":174063}," +
			"\"to\":4069219408,\"from\":0,\"lastPrice\":63607," +
			"\"bestPrices\":{\"ask\":63607.1,\"bid\":63606.9}}";

		var instruments = BigOneRestClient.DeserializeContract<
			BigOneContractInstrument[]>(instrumentsJson);
		var depth = BigOneRestClient.DeserializeContract<
			BigOneContractDepth>(depthJson);

		AreEqual(1, instruments.Length);
		AreEqual("BTCUSD", instruments[0].Symbol);
		AreEqual(63639.67m, instruments[0].MarkPrice);
		AreEqual(126028m, depth.Bids["63606.9"]);
		AreEqual(63607.1m, depth.BestPrices.Ask);
	}

	[TestMethod]
	public void WebSocketModelsHandleSpotAndContractPayloads()
	{
		const string spotJson =
			"{\"requestId\":\"ticker\",\"tickersSnapshot\":{\"tickers\":[{" +
			"\"market\":\"BTC-USDT\",\"bid\":{\"price\":\"63699.19\"," +
			"\"amount\":\"0.001429\"},\"ask\":{\"price\":\"63711.96\"," +
			"\"amount\":\"0.001844\"},\"close\":\"63702.01\"}]}}";
		const string candleJson =
			"[{\"open\":63617.3,\"nTrades\":15,\"turnover\":0.121," +
			"\"symbol\":\"BTCUSD\",\"time\":1785199200000," +
			"\"type\":\"1MIN\",\"close\":63581.5,\"volume\":7705," +
			"\"high\":63617.3,\"low\":63580.6,\"version\":4069224680," +
			"\"nextTs\":1785199260000}]";

		var spot = BigOneSpotWsClient.DeserializeMessage(spotJson);
		var candles = BigOneContractWsClient.DeserializeCandles(
			candleJson);

		AreEqual("ticker", spot.RequestId);
		AreEqual(63699.19m,
			spot.TickersSnapshot.Tickers[0].Bid.Price);
		AreEqual(1, candles.Length);
		AreEqual("1MIN", candles[0].Type);
		AreEqual(7705m, candles[0].Volume);
	}

	[TestMethod]
	public void SymbolsAndIntervalsUseBigOneFormats()
	{
		var spot = new BigOneSpotPair
		{
			Name = "BTC-USDT",
			BaseScale = 8,
			QuoteScale = 2,
			BaseAsset = new() { Symbol = "BTC" },
			QuoteAsset = new() { Symbol = "USDT" },
		};
		var contract = new BigOneContractInstrument
		{
			Symbol = "BTCUSD",
		};

		AreEqual(new SecurityId
		{
			SecurityCode = "BTC/USDT",
			BoardCode = BoardCodes.BigOne,
		}, spot.ToStockSharp());
		AreEqual(new SecurityId
		{
			SecurityCode = "BTCUSD",
			BoardCode = BoardCodes.BigOne,
		}, contract.ToStockSharp());
		AreEqual("min1",
			TimeSpan.FromMinutes(1).ToBigOneSpotPeriod());
		AreEqual("MIN1",
			TimeSpan.FromMinutes(1).ToBigOneSpotStreamPeriod());
		AreEqual("1MIN",
			TimeSpan.FromMinutes(1).ToBigOneContractPeriod());
		IsTrue(BigOneExtensions.TimeFrames.Contains(
			TimeSpan.FromDays(1)));
	}

	[TestMethod]
	public void OrderConditionKeepsSpotAndContractParameters()
	{
		var condition = new BigOneOrderCondition
		{
			StopPrice = 62000m,
			TriggerAbove = true,
			ReduceOnly = true,
			PostOnly = true,
		};

		AreEqual(62000m, condition.StopPrice);
		IsTrue(condition.TriggerAbove);
		IsTrue(condition.ReduceOnly);
		IsTrue(condition.PostOnly);
	}

	private static string Decode(string value)
	{
		value = value.Replace('-', '+').Replace('_', '/');
		value += new string('=', (4 - value.Length % 4) % 4);
		return Encoding.UTF8.GetString(Convert.FromBase64String(value));
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task CandleHistoryReadsBackwardsFromItsUpperBound()
	{
		var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
		var to = from.AddMinutes(2);
		using var client = new BigOneRestClient("https://spot.example.test", "https://contract.example.test", null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(request =>
		{
			var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
			AreEqual(to, DateTime.Parse(query["time"]).ToUniversalTime());
			var body = query["direction"] == "DESC"
				? """{"code":0,"data":[{"time":"2026-01-01T00:02:00Z","open":"100","high":"110","low":"90","close":"105","volume":"1"},{"time":"2026-01-01T00:01:00Z","open":"100","high":"110","low":"90","close":"105","volume":"1"}]}"""
				: """{"code":0,"data":[]}""";
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
		})), "_http");

		var candles = await client.GetCandlesAsync("BTC-USDT", "min1", from, to, CancellationToken);

		AreEqual(2, candles.Length);
		AreEqual((long)(from.AddMinutes(1) - DateTime.UnixEpoch).TotalMilliseconds, candles[0].Timestamp);
		AreEqual((long)(to - DateTime.UnixEpoch).TotalMilliseconds, candles[1].Timestamp);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new BigOneMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.BigOne,
	};

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000)]
	public async Task PublicSpotWebSocketStreams()
	{
		var ticker = NewSignal();
		var book = NewSignal();
		var trade = NewSignal();
		var candle = NewSignal();
		var error = new TaskCompletionSource<Exception>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var client = new BigOneSpotWsClient(
			"wss://api.big.one/ws/v2",
			new WorkingTime(), 0);

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
		client.CandleReceived += (_, _) =>
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
				"BTC-USDT", CancellationToken);
			await client.SubscribeOrderBookAsync(
				"BTC-USDT", CancellationToken);
			await client.SubscribeTradesAsync(
				"BTC-USDT", CancellationToken);
			await client.SubscribeCandlesAsync(
				"BTC-USDT", "MIN1", CancellationToken);

			await AwaitStreamsAsync(
				[ticker.Task, book.Task, trade.Task, candle.Task],
				error.Task, "BigONE spot WebSocket streams");
		}
		finally
		{
			await client.DisconnectAsync(CancellationToken);
			client.Dispose();
		}
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000)]
	public async Task PublicContractWebSocketStreams()
	{
		var ticker = NewSignal();
		var book = NewSignal();
		var trade = NewSignal();
		var candle = NewSignal();
		var error = new TaskCompletionSource<Exception>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var client = new BigOneContractWsClient(
			"wss://api.big.one/ws/contract/v2",
			"wss://api.big.one/ws/contract/v2/stream",
			null, new WorkingTime(), 0);

		client.InstrumentReceived += (_, _) =>
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
		client.CandlesReceived += (_, _) =>
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
			await client.SubscribeInstrumentAsync(
				"BTCUSD", CancellationToken);
			await client.SubscribeOrderBookAsync(
				"BTCUSD", CancellationToken);
			await client.SubscribeTradesAsync(
				"BTCUSD", CancellationToken);
			await client.SubscribeCandlesAsync(
				"BTCUSD", "1MIN", CancellationToken);

			await AwaitStreamsAsync(
				[ticker.Task, book.Task, trade.Task, candle.Task],
				error.Task, "BigONE contract WebSocket streams");
		}
		finally
		{
			await client.DisconnectAsync(CancellationToken);
			client.Dispose();
		}
	}

	private async Task AwaitStreamsAsync(Task[] tasks,
		Task<Exception> error, string description)
	{
		var streams = Task.WhenAll(tasks);
		var timeout = Task.Delay(
			TimeSpan.FromSeconds(30), CancellationToken);
		var completed = await Task.WhenAny(streams, error, timeout);
		if (completed == error)
			throw await error;
		if (completed != streams)
			Fail($"{description} timed out.");
		await streams;
	}

	private static TaskCompletionSource NewSignal()
		=> new(TaskCreationOptions.RunContinuationsAsynchronously);
}
