namespace StockSharp.Connectors.Tests;

using System;
using System.Buffers.Binary;
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
using StockSharp.MStock;
using StockSharp.MStock.Native;

[TestClass]
public class MStockTests : BaseTestClass
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
	public void DefaultsUseOfficialTypeBEndpointsAndSegments()
	{
		var adapter = new MStockMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://api.mstock.trade",
			adapter.RestEndpoint);
		AreEqual("wss://ws.mstock.trade",
			adapter.StreamingEndpoint);
		IsTrue(adapter.StreamingEnabled);
		AreEqual(TimeSpan.FromSeconds(5),
			adapter.PollingInterval);
		AreEqual(8, MStockMessageAdapter.AllTimeFrames.Count());
		CollectionAssert.AreEquivalent(
			new[] { "NSE", "BSE", "NFO", "BFO", "CDS" },
			adapter.AssociatedBoards);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsCredentialsAndEndpoints()
	{
		var source = new MStockMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "api-key".Secure(),
			ClientCode = "client-code",
			Password = "password".Secure(),
			Otp = "123456".Secure(),
			UseTotp = true,
			RefreshToken = "refresh".Secure(),
			AccessToken = "access".Secure(),
			RestEndpoint = "https://rest.example",
			StreamingEndpoint = "wss://stream.example",
			StreamingEnabled = false,
			PollingInterval = TimeSpan.FromSeconds(13),
		};
		var storage = new SettingsStorage();
		source.Save(storage);
		var target = new MStockMessageAdapter(
			new IncrementalIdGenerator());

		target.Load(storage);

		AreEqual("api-key", target.Key.UnSecure());
		AreEqual("client-code", target.ClientCode);
		AreEqual("password", target.Password.UnSecure());
		AreEqual("123456", target.Otp.UnSecure());
		IsTrue(target.UseTotp);
		AreEqual("refresh", target.RefreshToken.UnSecure());
		AreEqual("access", target.AccessToken.UnSecure());
		AreEqual("https://rest.example", target.RestEndpoint);
		AreEqual("wss://stream.example",
			target.StreamingEndpoint);
		IsFalse(target.StreamingEnabled);
		AreEqual(TimeSpan.FromSeconds(13),
			target.PollingInterval);
	}

	[TestMethod]
	public async Task RestAuthenticationUsesOfficialTypeBFlow()
	{
		var paths = new List<string>();
		var handler = new Handler(async (request, cancellationToken) =>
		{
			paths.Add(request.RequestUri.AbsolutePath);
			AreEqual("1", request.Headers.GetValues(
				"X-Mirae-Version").Single());
			AreEqual("api-key", request.Headers.GetValues(
				"X-PrivateKey").Single());
			if (request.RequestUri.AbsolutePath.EndsWith(
				"/connect/login", StringComparison.Ordinal))
			{
				IsTrue(request.Headers.Authorization is null);
				var body = JObject.Parse(await request.Content
					.ReadAsStringAsync(cancellationToken));
				AreEqual("client-code",
					body.Value<string>("clientcode"));
				AreEqual("password",
					body.Value<string>("password"));
				return Json(
					"""{"status":true,"data":{"refreshToken":"request-token"}}""");
			}
			if (request.RequestUri.AbsolutePath.EndsWith(
				"/session/verifytotp", StringComparison.Ordinal))
			{
				IsTrue(request.Headers.Authorization is null);
				var body = JObject.Parse(await request.Content
					.ReadAsStringAsync(cancellationToken));
				AreEqual("request-token",
					body.Value<string>("refreshToken"));
				AreEqual("654321", body.Value<string>("totp"));
				return Json(
					"""{"status":"True","data":{"jwtToken":"jwt-token","refreshToken":"refresh-token","feedToken":"feed-token"}}""");
			}

			AreEqual("Bearer",
				request.Headers.Authorization.Scheme);
			AreEqual("jwt-token",
				request.Headers.Authorization.Parameter);
			return Json(
				"""{"status":"True","data":[]}""");
		});
		using var client = new MStockRestClient(
			"https://api.example", "api-key".Secure(),
			"client-code", "password".Secure(), "654321".Secure(),
			true, null, null, handler);

		await client.AuthenticateAsync(CancellationToken);
		await client.GetOrdersAsync(CancellationToken);

		AreEqual("jwt-token", client.AccessToken);
		AreEqual("refresh-token", client.RefreshToken);
		AreEqual("feed-token", client.FeedToken);
		CollectionAssert.AreEqual(new[]
		{
			"/openapi/typeb/connect/login",
			"/openapi/typeb/session/verifytotp",
			"/openapi/typeb/orders",
		}, paths);
	}

	[TestMethod]
	public async Task RestPayloadsUseOfficialOrderAndQuoteContracts()
	{
		var paths = new List<string>();
		var handler = new Handler(async (request, cancellationToken) =>
		{
			paths.Add(request.RequestUri.AbsolutePath);
			var body = JObject.Parse(await request.Content
				.ReadAsStringAsync(cancellationToken));
			if (request.RequestUri.AbsolutePath.EndsWith(
				"/orders/regular", StringComparison.Ordinal))
			{
				AreEqual("NIFTY30JUL25000CE",
					body.Value<string>("tradingsymbol"));
				AreEqual("STOPLOSS_LIMIT",
					body.Value<string>("ordertype"));
				AreEqual("CARRYFORWARD",
					body.Value<string>("producttype"));
				return Json(
					"""[{"status":"success","data":{"order_id":"ORD-1"}}]""");
			}
			if (request.RequestUri.AbsolutePath.EndsWith(
				"/instruments/historical",
				StringComparison.Ordinal))
			{
				AreEqual("01-02-2025",
					body.Value<string>("fromdate"));
				AreEqual("07-02-2025",
					body.Value<string>("todate"));
				AreEqual("ONE_HOUR",
					body.Value<string>("interval"));
				return Json(
					"""{"status":"True","data":{"candles":[]}}""");
			}

			AreEqual("FULL", body.Value<string>("mode"));
			AreEqual("68428",
				body["exchangeTokens"]["NFO"].Single()
					.Value<string>());
			return Json(
				"""{"status":"True","data":{"fetched":[]}}""");
		});
		using var client = new MStockRestClient(
			"https://api.example", "api-key".Secure(), null, null,
			null, true, null, "jwt-token".Secure(), handler);
		var order = new JObject
		{
			["tradingsymbol"] = "NIFTY30JUL25000CE",
			["ordertype"] = "STOPLOSS_LIMIT",
			["producttype"] = "CARRYFORWARD",
		};

		var result = await client.PlaceOrderAsync(order,
			CancellationToken);
		await client.GetQuotesAsync(
			[new("NFO", "68428", "NIFTY30JUL25000CE",
				"NIFTY", 75)],
			CancellationToken);
		await client.GetCandlesAsync(
			new("NFO", "68428", "NIFTY30JUL25000CE",
				"NIFTY", 75),
			TimeSpan.FromHours(1), new(2025, 2, 1),
			new(2025, 2, 7), CancellationToken);

		AreEqual("ORD-1", result.Value<string>("order_id"));
		CollectionAssert.AreEqual(new[]
		{
			"/openapi/typeb/orders/regular",
			"/openapi/typeb/instruments/quote",
			"/openapi/typeb/instruments/historical",
		}, paths);
	}

	[TestMethod]
	public void InstrumentOrderAndTimeMappingsPreserveIdentity()
	{
		var instrument = JObject.Parse(
			"""
			{
			  "token": "68428",
			  "symbol": "NIFTY",
			  "name": "NIFTY30JUL25000CE",
			  "expiry": "30Jul2026",
			  "strike": "25000",
			  "lotsize": "75",
			  "instrumenttype": "OPTIDX",
			  "exch_seg": "NFO",
			  "tick_size": "0.05"
			}
			""").ToObject<MStockInstrument>();
		var security = instrument.ToSecurityId();
		var order = JObject.Parse(
			"""
			{
			  "orderid": "ORD-1",
			  "exchangeorderid": "EX-1",
			  "exchange": "NFO",
			  "symboltoken": "68428",
			  "tradingsymbol": "NIFTY30JUL25000CE",
			  "transactiontype": "SELL",
			  "ordertype": "STOPLOSS_LIMIT",
			  "producttype": "CARRYFORWARD",
			  "variety": "STOPLOSS",
			  "duration": "IOC",
			  "price": "101.25",
			  "triggerprice": "100.5",
			  "quantity": "75",
			  "filledshares": "25",
			  "orderstatus": "PARTIALLY FILLED",
			  "exchorderupdatetime": "2026-Jul-28 11: 15: 00"
			}
			""").ToMStockOrder();

		AreEqual("NIFTY30JUL25000CE", security.SecurityCode);
		AreEqual("NFO", security.BoardCode);
		AreEqual("NFO/68428", security.Native);
		AreEqual(SecurityTypes.Option,
			instrument.ToSecurityType());
		AreEqual(OptionTypes.Call, instrument.ToOptionType());
		AreEqual(0.05m, instrument.Tick);
		AreEqual(75m, instrument.Lot);
		AreEqual(25000m, instrument.StrikePrice);
		AreEqual(new DateTime(2026, 7, 30),
			instrument.ToExpiry());
		AreEqual(Sides.Sell, order.Side);
		AreEqual(50m, order.Balance);
		AreEqual(OrderTypes.Conditional,
			order.OrderType.ToMStockOrderType());
		AreEqual(TimeInForce.CancelBalance,
			order.Duration.ToMStockTimeInForce());
		AreEqual(OrderStates.Active,
			order.Status.ToMStockOrderState());
		AreEqual(new DateTimeOffset(2026, 7, 28, 11, 15, 0,
			TimeSpan.FromMinutes(330)), order.Time);
	}

	[TestMethod]
	public void BinaryFeedSupportsDirectAndEnvelopePackets()
	{
		var packet = new byte[379];
		packet[0] = 3;
		packet[1] = 2;
		Encoding.ASCII.GetBytes("68428").CopyTo(packet, 2);
		WriteUInt64(packet, 27, 42);
		WriteUInt64(packet, 35, 1_469_404_800);
		WriteUInt64(packet, 43, 10_125);
		WriteUInt64(packet, 51, 75);
		WriteUInt64(packet, 59, 10_100);
		WriteUInt64(packet, 67, 10_000);
		WriteDouble(packet, 75, 2_000);
		WriteDouble(packet, 83, 2_500);
		WriteUInt64(packet, 91, 10_000);
		WriteUInt64(packet, 99, 10_300);
		WriteUInt64(packet, 107, 9_900);
		WriteUInt64(packet, 115, 10_050);
		WriteUInt64(packet, 123, 1_469_404_700);
		WriteUInt64(packet, 131, 12_345);
		WriteDouble(packet, 139, 345);
		WriteUInt64(packet, 149, 500);
		WriteUInt64(packet, 157, 10_120);
		WriteUInt16(packet, 165, 3);
		WriteUInt64(packet, 249, 600);
		WriteUInt64(packet, 257, 10_130);
		WriteUInt16(packet, 265, 4);
		WriteUInt64(packet, 347, 11_000);
		WriteUInt64(packet, 355, 9_000);
		WriteUInt64(packet, 363, 15_000);
		WriteUInt64(packet, 371, 8_000);

		var direct = MStockExtensions.ParseMarketData(packet).Single();
		var envelope = new byte[383];
		BinaryPrimitives.WriteUInt16BigEndian(
			envelope.AsSpan(0, 2), 1);
		BinaryPrimitives.WriteUInt16BigEndian(
			envelope.AsSpan(2, 2), 379);
		packet.CopyTo(envelope, 4);
		var wrapped = MStockExtensions.ParseMarketData(
			envelope).Single();

		AreEqual("NFO", direct.Exchange);
		AreEqual("68428", direct.Token);
		AreEqual(101.25m, direct.LastPrice);
		AreEqual(75m, direct.LastVolume);
		AreEqual(101.2m, direct.Bids.Single().Price);
		AreEqual(500m, direct.Bids.Single().Volume);
		AreEqual(3, direct.Bids.Single().Orders);
		AreEqual(101.3m, direct.Asks.Single().Price);
		AreEqual(4, direct.Asks.Single().Orders);
		AreEqual(12345m, direct.OpenInterest);
		AreEqual(110m, direct.UpperLimit);
		AreEqual(90m, direct.LowerLimit);
		AreEqual(direct.LastPrice, wrapped.LastPrice);
		AreEqual(direct.Time, wrapped.Time);
	}

	[TestMethod]
	public void WebSocketUriAndSubscriptionsMatchTypeBProtocol()
	{
		var uri = MStockSocketClient.BuildUri(
			"wss://ws.example/feed?client=stocksharp",
			"api key", "jwt+token");
		var subscribe = JObject.Parse(
			MStockSocketClient.CreateSubscription("NFO",
				["68428", "68428"], 3, true));
		var unsubscribe = JObject.Parse(
			MStockSocketClient.CreateSubscription("NFO",
				["68428"], 3, false));

		AreEqual(
			"wss://ws.example/feed?client=stocksharp&" +
				"ACCESS_TOKEN=jwt%2Btoken&API_KEY=api%20key",
			uri.AbsoluteUri);
		AreEqual(1, subscribe.Value<int>("action"));
		AreEqual(3,
			subscribe["params"].Value<int>("mode"));
		AreEqual(2,
			subscribe["params"]["tokenList"][0]
				.Value<int>("exchangeType"));
		AreEqual(1,
			subscribe["params"]["tokenList"][0]["tokens"].Count());
		AreEqual(0, unsubscribe.Value<int>("action"));
	}

	private static void WriteUInt16(byte[] target, int offset,
		ushort value)
		=> BinaryPrimitives.WriteUInt16LittleEndian(
			target.AsSpan(offset, 2), value);

	private static void WriteUInt64(byte[] target, int offset,
		ulong value)
		=> BinaryPrimitives.WriteUInt64LittleEndian(
			target.AsSpan(offset, 8), value);

	private static void WriteDouble(byte[] target, int offset,
		double value)
		=> BinaryPrimitives.WriteDoubleLittleEndian(
			target.AsSpan(offset, 8), value);

	private static HttpResponseMessage Json(string content)
		=> new(HttpStatusCode.OK)
		{
			Content = new StringContent(content, Encoding.UTF8,
				"application/json"),
		};
}
