namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Reya;
using StockSharp.Reya.Native;
using StockSharp.Reya.Native.Model;

[TestClass]
[CLSCompliant(false)]
public class ReyaRegressionTests : BaseTestClass
{
	[TestMethod]
	[DataRow(false, "BTCRUSDPERP")]
	[DataRow(true, "WBTCRUSD")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task DepthSubscriptionsReturnBothPerpetualAndSpotBooks(bool isSpot, string symbol)
	{
		using var adapter = new ReyaMessageAdapter(new IncrementalIdGenerator());
		var client = new ReyaRestClient("https://api.example.test/v2");
		HistoryHttpHandler.ReplaceClient(client, new(new HistoryHttpHandler(request =>
		{
			AreEqual($"/v2/market/{symbol}/depth", request.RequestUri.AbsolutePath);
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent("{\"symbol\":\"" + symbol + "\",\"type\":\"SNAPSHOT\",\"updatedAt\":1792443724510,\"bids\":[{\"px\":\"61000\",\"qty\":\"2\"}],\"asks\":[{\"px\":\"61001\",\"qty\":\"3\"}]}"),
			};
		})), "_http");
		const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
		typeof(ReyaMessageAdapter).GetField("_restClient", fields).SetValue(adapter, client);
		typeof(ReyaMessageAdapter).GetField("_socket", fields).SetValue(adapter,
			new ReyaSocketClient("wss://socket.example.test", new(), 0));
		var markets = (Dictionary<string, ReyaMarket>)typeof(ReyaMessageAdapter).GetField("_markets", fields).GetValue(adapter);
		markets.Add(symbol, new() { Symbol = symbol, IsSpot = isSpot });
		var received = new List<Message>();
		adapter.NewOutMessageAsync += (message, _) =>
		{
			received.Add(message);
			return default;
		};

		await adapter.SendInMessageAsync(new MarketDataMessage
		{
			TransactionId = 1,
			IsSubscribe = true,
			DataType2 = DataType.MarketDepth,
			SecurityId = new() { SecurityCode = symbol, BoardCode = BoardCodes.Reya },
			Count = 1,
		}, CancellationToken);

		IsFalse(received.OfType<SubscriptionResponseMessage>().Any(message => message.Error is not null));
		var book = received.OfType<QuoteChangeMessage>().Single();
		AreEqual(61000m, book.Bids.Single().Price);
		AreEqual(2m, book.Bids.Single().Volume);
		AreEqual(61001m, book.Asks.Single().Price);
		AreEqual(3m, book.Asks.Single().Volume);
	}

	[TestMethod]
	[DataRow(nameof(ReyaRestClient.GetPerpetualMarketsAsync), "/v2/perpMarketDefinitions")]
	[DataRow(nameof(ReyaRestClient.GetPerpetualSummariesAsync), "/v2/perpMarkets/summary")]
	[DataRow(nameof(ReyaRestClient.GetPerpetualSummaryAsync), "/v2/perpMarket/BTCRUSDPERP/summary")]
	[DataRow(nameof(ReyaRestClient.GetSpotSummaryAsync), "/v2/spotMarket/WBTCRUSD/summary")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task PublicRequestsUseCurrentRoutes(string operation, string expectedPath)
	{
		string path = null;
		using var client = new ReyaRestClient("https://api.example.test/v2");
		HistoryHttpHandler.ReplaceClient(client, new(new HistoryHttpHandler(request =>
		{
			path = request.RequestUri.AbsolutePath;
			return new(path == expectedPath ? HttpStatusCode.OK : HttpStatusCode.NotFound)
			{
				Content = new StringContent(operation is nameof(ReyaRestClient.GetPerpetualSummaryAsync) or
					nameof(ReyaRestClient.GetSpotSummaryAsync) ? "{}" : "[]"),
			};
		})), "_http");

		Task action = operation switch
		{
			nameof(ReyaRestClient.GetPerpetualMarketsAsync) => client.GetPerpetualMarketsAsync(CancellationToken).AsTask(),
			nameof(ReyaRestClient.GetPerpetualSummariesAsync) => client.GetPerpetualSummariesAsync(CancellationToken).AsTask(),
			nameof(ReyaRestClient.GetPerpetualSummaryAsync) => client.GetPerpetualSummaryAsync("BTCRUSDPERP", CancellationToken).AsTask(),
			_ => client.GetSpotSummaryAsync("WBTCRUSD", CancellationToken).AsTask(),
		};
		await action;

		AreEqual(expectedPath, path);
	}

	[TestMethod]
	public void SummaryKeepsCurrentOracleAndMarkPrices()
	{
		var summary = JsonConvert.DeserializeObject<ReyaMarketSummary>(
			"""{"symbol":"BTCRUSDPERP","updatedAt":1792443724510,"oraclePrice":"61001.5","markPrice":"61002.5"}""");
		var fields = JObject.FromObject(summary);

		AreEqual("61001.5", fields["oraclePrice"]?.Value<string>());
		AreEqual("61002.5", fields["markPrice"]?.Value<string>());
	}

	[TestMethod]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task PerpetualSummaryIsDispatchedFromItsCurrentChannel()
	{
		const string channel = "/v2/perpMarket/BTCRUSDPERP/summary";
		const string payload = """{"type":"channel_data","channel":"/v2/perpMarket/BTCRUSDPERP/summary","data":{"symbol":"BTCRUSDPERP","oraclePrice":"61001.5","markPrice":"61002.5"}}""";
		using var client = new ReyaSocketClient("wss://socket.example.test", new(), 0);
		ReyaSocketEnvelope<ReyaMarketSummary> received = null;
		client.PerpetualSummaryReceived += (data, _) =>
		{
			received = data;
			return default;
		};
		var process = typeof(ReyaSocketClient).GetMethod("ProcessChannelDataAsync", BindingFlags.Instance | BindingFlags.NonPublic);

		await (ValueTask)process.Invoke(client, [payload, channel, CancellationToken]);

		AreEqual("BTCRUSDPERP", received?.Data?.Symbol);
	}
}
