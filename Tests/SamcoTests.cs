namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Samco;
using StockSharp.Samco.Native;

[TestClass]
public class SamcoTests : BaseTestClass
{
	private sealed class Handler(
		Func<HttpRequestMessage, CancellationToken,
			Task<HttpResponseMessage>> callback) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
			=> callback(request, cancellationToken);
	}

	[TestMethod]
	public void DefaultsUseOfficialEndpointsAndSegments()
	{
		var adapter = new SamcoMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://tradeapi.samco.in",
			adapter.RestEndpoint);
		AreEqual(
			"https://developers.stocknote.com/doc/ScripMaster.csv",
			adapter.InstrumentEndpoint);
		AreEqual("wss://stream.samco.in",
			adapter.StreamingEndpoint);
		IsTrue(adapter.StreamingEnabled);
		AreEqual(TimeSpan.FromSeconds(5),
			adapter.PollingInterval);
		AreEqual(8, SamcoMessageAdapter.AllTimeFrames.Count());
		CollectionAssert.AreEquivalent(new[]
		{
			"NSE", "BSE", "NFO", "BFO", "CDS", "MCX", "MFO",
		}, adapter.AssociatedBoards);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsCredentialsAndEndpoints()
	{
		var source = new SamcoMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "encrypted-key".Secure(),
			Secret = "encrypted-secret".Secure(),
			SessionToken = "session".Secure(),
			RestEndpoint = "https://rest.example",
			InstrumentEndpoint = "https://instruments.example/all.csv",
			StreamingEndpoint = "wss://stream.example",
			StreamingEnabled = false,
			PollingInterval = TimeSpan.FromSeconds(17),
		};
		var storage = new SettingsStorage();
		source.Save(storage);
		var target = new SamcoMessageAdapter(
			new IncrementalIdGenerator());

		target.Load(storage);

		AreEqual("encrypted-key", target.Key.UnSecure());
		AreEqual("encrypted-secret", target.Secret.UnSecure());
		AreEqual("session", target.SessionToken.UnSecure());
		AreEqual("https://rest.example", target.RestEndpoint);
		AreEqual("https://instruments.example/all.csv",
			target.InstrumentEndpoint);
		AreEqual("wss://stream.example",
			target.StreamingEndpoint);
		IsFalse(target.StreamingEnabled);
		AreEqual(TimeSpan.FromSeconds(17),
			target.PollingInterval);
	}

	[TestMethod]
	public async Task AuthenticationUsesV32SessionContract()
	{
		var paths = new List<string>();
		var handler = new Handler(async (request, cancellationToken) =>
		{
			paths.Add(request.RequestUri.AbsolutePath);
			if (request.RequestUri.AbsolutePath == "/session/token")
			{
				IsFalse(request.Headers.Contains("x-session-token"));
				var body = JObject.Parse(await request.Content
					.ReadAsStringAsync(cancellationToken));
				AreEqual("encrypted-key",
					body.Value<string>("apiKey"));
				AreEqual("encrypted-secret",
					body.Value<string>("apiSecret"));
				return Json(
					"""{"status":"Success","sessionToken":"daily-token","accountID":"DA123"}""");
			}
			AreEqual("daily-token", request.Headers.GetValues(
				"x-session-token").Single());
			return Json(
				"""{"status":"Success","orderBookDetails":[]}""");
		});
		using var client = new SamcoRestClient(
			"https://api.example",
			"https://instruments.example/all.csv",
			"encrypted-key".Secure(),
			"encrypted-secret".Secure(), null, handler);

		await client.AuthenticateAsync(CancellationToken);
		await client.GetOrdersAsync(CancellationToken);

		AreEqual("daily-token", client.SessionToken);
		AreEqual("DA123", client.AccountId);
		CollectionAssert.AreEqual(new[]
		{
			"/session/token",
			"/order/orderBook",
		}, paths);
	}

	[TestMethod]
	public async Task InstrumentCsvPreservesNativeIdentityAndMetadata()
	{
		const string csv =
			"exchange,exchangeSegment,symbolCode,tradingSymbol," +
			"name,lastPrice,instrument,lotSize,strikePrice," +
			"expiryDate,tickSize\r\n" +
			"NFO,nse_fo,52310_NFO,NIFTY30JUL25000CE,NIFTY," +
			"101.25,OPTIDX,75,25000,30-Jul-2026,0.05\r\n";
		var handler = new Handler((request, _) =>
		{
			AreEqual("https://instruments.example/ScripMaster.csv",
				request.RequestUri.AbsoluteUri);
			return Task.FromResult(new HttpResponseMessage(
				HttpStatusCode.OK)
			{
				Content = new StringContent(csv, Encoding.UTF8,
					"text/csv"),
			});
		});
		using var client = new SamcoRestClient(
			"https://api.example",
			"https://instruments.example/ScripMaster.csv",
			null, null, "token".Secure(), handler);

		var instrument = (await client.GetInstrumentsAsync(
			CancellationToken)).Single();
		var security = instrument.ToSecurityId();

		AreEqual("52310_NFO", security.Native);
		AreEqual("NIFTY30JUL25000CE", security.SecurityCode);
		AreEqual("NFO", security.BoardCode);
		AreEqual(SecurityTypes.Option,
			instrument.ToSecurityType());
		AreEqual(OptionTypes.Call, instrument.ToOptionType());
		AreEqual(75m, instrument.Lot);
		AreEqual(0.05m, instrument.Tick);
		AreEqual(25000m, instrument.Strike);
		AreEqual(new DateTime(2026, 7, 30),
			instrument.ToExpiry());
	}

	[TestMethod]
	public async Task RestRoutesUseOfficialQuoteCandleAndOrderContracts()
	{
		var requests = new List<(HttpMethod Method, string Uri,
			JObject Body)>();
		var handler = new Handler(async (request, cancellationToken) =>
		{
			var body = request.Content is null
				? null
				: JObject.Parse(await request.Content
					.ReadAsStringAsync(cancellationToken));
			requests.Add((request.Method,
				request.RequestUri.PathAndQuery, body));
			if (request.RequestUri.AbsolutePath ==
				"/order/placeOrder")
				return Json(
					"""{"status":"Success","orderNumber":"ORD-1"}""");
			return Json("""{"status":"Success"}""");
		});
		using var client = new SamcoRestClient(
			"https://api.example",
			"https://instruments.example/all.csv",
			null, null, "token".Secure(), handler);
		var instrument = new SamcoInstrumentRef("NFO",
			"52310_NFO", "NIFTY30JUL25000CE", "NIFTY", 75,
			"OPTIDX");
		var order = new JObject
		{
			["symbolName"] = "NIFTY30JUL25000CE",
			["exchange"] = "NFO",
			["orderType"] = "SL",
		};

		var placed = await client.PlaceOrderAsync(order,
			CancellationToken);
		await client.GetQuoteAsync(instrument, CancellationToken);
		await client.GetDepthAsync(instrument, CancellationToken);
		await client.GetCandlesAsync(instrument,
			TimeSpan.FromMinutes(15), new(2026, 7, 1, 9, 15, 0),
			new(2026, 7, 1, 15, 30, 0), CancellationToken);
		await client.GetCandlesAsync(instrument,
			TimeSpan.FromDays(1), new(2026, 6, 1),
			new(2026, 7, 1), CancellationToken);
		await client.CancelOrderAsync("ORD-1", CancellationToken);

		AreEqual("ORD-1", placed.Value<string>("orderNumber"));
		AreEqual("/order/placeOrder", requests[0].Uri);
		AreEqual("SL",
			requests[0].Body.Value<string>("orderType"));
		IsTrue(requests[1].Uri.Contains(
			"/quote/getQuote?symbolName=NIFTY30JUL25000CE&exchange=NFO",
			StringComparison.Ordinal));
		AreEqual("/marketDepth", requests[2].Uri);
		IsTrue(requests[3].Uri.Contains(
			"interval=15", StringComparison.Ordinal));
		IsTrue(requests[4].Uri.StartsWith(
			"/history/candleData?", StringComparison.Ordinal));
		AreEqual(HttpMethod.Delete, requests[5].Method);
		AreEqual("/order/cancelOrder?orderNumber=ORD-1",
			requests[5].Uri);
	}

	[TestMethod]
	public void WebSocketSubscriptionAndFramesMapDepthAndQuote()
	{
		var subscribe = JObject.Parse(
			SamcoSocketClient.CreateSubscription(
				["52310_NFO", "52310_NFO"], true));
		var unsubscribe = JObject.Parse(
			SamcoSocketClient.CreateSubscription(
				["52310_NFO"], false));
		var feed = SamcoExtensions.ParseFeed(
			"""
			{
			  "response": {
			    "streaming_type": "quote2",
			    "data": {
			      "sym": "52310_NFO",
			      "ltp": "101.25",
			      "ltq": "75",
			      "ltt": "28-Jul-2026 11:15:00",
			      "b1p": "101.20",
			      "b1q": "150",
			      "b1n": "3",
			      "a1p": "101.30",
			      "a1q": "225",
			      "a1n": "4",
			      "o": "100",
			      "h": "103",
			      "l": "99",
			      "c": "100.5",
			      "oI": "12345"
			    }
			  }
			}
			""");

		AreEqual("quote2",
			subscribe["request"].Value<string>("streaming_type"));
		AreEqual("subscribe",
			subscribe["request"].Value<string>("request_type"));
		AreEqual(1,
			subscribe["request"]["data"]["symbols"].Count());
		AreEqual("unsubscribe",
			unsubscribe["request"].Value<string>("request_type"));
		AreEqual("52310_NFO", feed.SymbolCode);
		AreEqual(101.25m, feed.LastPrice);
		AreEqual(75m, feed.LastVolume);
		AreEqual(101.2m, feed.Bids.Single().Price);
		AreEqual(150m, feed.Bids.Single().Volume);
		AreEqual(3, feed.Bids.Single().Orders);
		AreEqual(101.3m, feed.Asks.Single().Price);
		AreEqual(4, feed.Asks.Single().Orders);
		AreEqual(12345m, feed.OpenInterest);
	}

	[TestMethod]
	public void OrderTradeStatusAndTimeMappingsUseSamcoContracts()
	{
		var order = JObject.Parse(
			"""
			{
			  "orderNumber": "ORD-1",
			  "exchange": "NFO",
			  "symbol": "52310_NFO",
			  "tradingSymbol": "NIFTY30JUL25000CE",
			  "transactionType": "SELL",
			  "productCode": "NRML",
			  "orderType": "SL",
			  "orderPrice": "101.25",
			  "triggerPrice": "100.50",
			  "quantity": "75",
			  "filledQuantity": "25",
			  "unfilledQuantity": "50",
			  "orderValidity": "IOC",
			  "orderStatus": "Partially Filled",
			  "orderTime": "28-Jul-2026 11:15:00"
			}
			""").ToSamcoOrder();
		var trade = JObject.Parse(
			"""
			{
			  "orderNumber": "ORD-1",
			  "tradeNumber": "TRD-1",
			  "exchange": "NFO",
			  "symbol": "52310_NFO",
			  "tradingSymbol": "NIFTY30JUL25000CE",
			  "transactionType": "BUY",
			  "tradePrice": "101.30",
			  "filledQuantity": "25",
			  "tradeDate": "28JUL2026",
			  "tradeTime": "11:16:00 AM"
			}
			""").ToSamcoTrade();

		AreEqual(Sides.Sell, order.Side);
		AreEqual(OrderTypes.Conditional,
			order.OrderType.ToSamcoOrderType());
		AreEqual(TimeInForce.CancelBalance,
			order.Validity.ToSamcoTimeInForce());
		AreEqual(OrderStates.Active,
			order.Status.ToSamcoOrderState());
		AreEqual(50m, order.Balance);
		AreEqual(new DateTimeOffset(2026, 7, 28, 11, 15, 0,
			TimeSpan.FromMinutes(330)), order.Time);
		AreEqual("TRD-1", trade.Id);
		AreEqual(Sides.Buy, trade.Side);
		AreEqual(101.3m, trade.Price);
		AreEqual(new DateTimeOffset(2026, 7, 28, 11, 16, 0,
			TimeSpan.FromMinutes(330)), trade.Time);
	}

	private static HttpResponseMessage Json(string content)
		=> new(HttpStatusCode.OK)
		{
			Content = new StringContent(content, Encoding.UTF8,
				"application/json"),
		};
}
