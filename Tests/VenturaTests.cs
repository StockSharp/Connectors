namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.Ventura;
using StockSharp.Ventura.Native;

[TestClass]
public class VenturaTests : BaseTestClass
{
	[TestMethod]
	public void SettingsRoundTripKeepsCredentialsAndEndpoints()
	{
		var source = new VenturaMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "APP KEY".Secure(),
			Secret = "SECRET".Secure(),
			RequestToken = "REQUEST".Secure(),
			Token = "ACCESS".Secure(),
			RefreshToken = "REFRESH".Secure(),
			ClientId = "CLIENT-1",
			Pin = "1234".Secure(),
			TotpSecret = "JBSWY3DPEHPK3PXP".Secure(),
			MacAddress = "00:11:22:33:44:55",
			PortfolioName = "PORTFOLIO",
			DefaultProduct = VenturaProducts.Intraday,
			PollingInterval = TimeSpan.FromSeconds(27),
			ReconnectAttempts = 6,
			RestAddress = new("https://rest.example.test/api/"),
			MarketDataAddress = new("wss://stream.example.test/market"),
			OrderStatusAddress = new("wss://stream.example.test/orders"),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new VenturaMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual(source.Key.UnSecure(), target.Key.UnSecure());
		AreEqual(source.Secret.UnSecure(), target.Secret.UnSecure());
		AreEqual(
			source.RequestToken.UnSecure(),
			target.RequestToken.UnSecure());
		AreEqual(source.Token.UnSecure(), target.Token.UnSecure());
		AreEqual(
			source.RefreshToken.UnSecure(),
			target.RefreshToken.UnSecure());
		AreEqual(source.ClientId, target.ClientId);
		AreEqual(source.Pin.UnSecure(), target.Pin.UnSecure());
		AreEqual(
			source.TotpSecret.UnSecure(),
			target.TotpSecret.UnSecure());
		AreEqual(source.MacAddress, target.MacAddress);
		AreEqual(source.PortfolioName, target.PortfolioName);
		AreEqual(source.DefaultProduct, target.DefaultProduct);
		AreEqual(source.PollingInterval, target.PollingInterval);
		AreEqual(source.ReconnectAttempts, target.ReconnectAttempts);
		AreEqual(source.RestAddress, target.RestAddress);
		AreEqual(source.MarketDataAddress, target.MarketDataAddress);
		AreEqual(source.OrderStatusAddress, target.OrderStatusAddress);
		AreEqual(
			"https://rest.example.test/api/auth/v1/login?app_key=APP%20KEY&state=STATE%20VALUE",
			target.CreateAuthorizationUri("STATE VALUE").AbsoluteUri);
		IsTrue(target is IKeySecretAdapter);
		IsTrue(target is ITokenAdapter);
	}

	[TestMethod]
	public void DefaultsMatchPublishedEaseApiEndpoints()
	{
		var adapter = new VenturaMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://easeapi.venturasecurities.com/",
			adapter.RestAddress.AbsoluteUri);
		AreEqual(
			"wss://easeapi-ws.venturasecurities.com/v1/easeapi_mktdata",
			adapter.MarketDataAddress.AbsoluteUri);
		AreEqual(
			"wss://easeapi-ws.venturasecurities.com/v1/easeapi_ob",
			adapter.OrderStatusAddress.AbsoluteUri);
		AreEqual(5, adapter.SupportedOrderBookDepths.Single());
		IsFalse(adapter.IsAllDownloadingSupported(
			TimeSpan.FromMinutes(1).TimeFrame()));
	}

	[TestMethod]
	public void AuthHashAndTotpMatchPublishedAlgorithms()
	{
		AreEqual(
			"0d3e7fd9d5f38d9d23bdee9f2957219930c53ac66fc884f901f4a0934fbb0f00",
			VenturaRestClient.ComputeAuthHash("APP", "SECRET"));
		AreEqual(
			287082,
			VenturaRestClient.GenerateTotp(
				"GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ",
				DateTimeOffset.FromUnixTimeSeconds(59).UtcDateTime));
	}

	[TestMethod]
	public async Task RequestTokenExchangeUsesPublishedHeadersAndBody()
	{
		var handler = new CaptureHandler(
			new ResponseSpec(
				"""
				{
				  "client_id":"C100",
				  "auth_token":"ACCESS",
				  "auth_expiry":"2026-07-27T00:00:00",
				  "refresh_token":"REFRESH",
				  "refresh_expiry":"2026-08-27T00:00:00"
				}
				"""));
		using var client = CreateClient(handler);

		var auth = await client.ExchangeAccessToken(
			"SECRET".Secure(),
			"REQUEST VALUE".Secure(),
			CancellationToken.None);

		AreEqual("C100", auth.ClientId);
		AreEqual("ACCESS", auth.AuthToken);
		AreEqual("C100", client.ClientId);
		AreEqual("ACCESS", client.Token);
		var request = handler.Requests.Single();
		AreEqual(HttpMethod.Post, request.Method);
		AreEqual(
			"https://api.example.test/login/v1/authorization/token",
			request.Uri.AbsoluteUri);
		AreEqual("KEY", request.Header("x-app-key"));
		AreEqual("1", request.Header("X-EaseApi-Version"));
		IsNull(request.Header("Authorization"));
		var payload = JObject.Parse(request.Body);
		AreEqual(
			"REQUEST VALUE",
			payload["request_token"].Value<string>());
		AreEqual(
			VenturaRestClient.ComputeAuthHash("KEY", "SECRET"),
			payload["data"].Value<string>());
	}

	[TestMethod]
	public async Task TotpLoginUsesClientMacAndBearerSession()
	{
		var handler = new CaptureHandler(
			new ResponseSpec(
				"""{"data":{"client_id":"C200","auth_token":"ACCESS2","refresh_token":"R2"}}"""),
			new ResponseSpec(
				"""{"status":"success","error":false,"data":{"name":"Alice"}}"""));
		using var client = CreateClient(handler);

		var auth = await client.LoginWithTotp(
			"C200",
			"SECRET".Secure(),
			"4321".Secure(),
			"GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ".Secure(),
			"00:11:22:33:44:55",
			DateTimeOffset.FromUnixTimeSeconds(59).UtcDateTime,
			CancellationToken.None);
		await client.GetProfile(CancellationToken.None);

		AreEqual("ACCESS2", auth.AuthToken);
		var login = handler.Requests[0];
		AreEqual("C200", login.Header("x-client-id"));
		AreEqual(
			"00:11:22:33:44:55",
			login.Header("x-mac-address"));
		IsNull(login.Header("Authorization"));
		var payload = JObject.Parse(login.Body);
		AreEqual("4321", payload["password"].Value<string>());
		AreEqual("287082", payload["totp"].Value<string>());

		var profile = handler.Requests[1];
		AreEqual("C200", profile.Header("x-client-id"));
		AreEqual("Bearer ACCESS2", profile.Header("Authorization"));
	}

	[TestMethod]
	public async Task InstrumentMasterMapsCashIndexOptionAndGzip()
	{
		const string csv =
			"exchange_token,trading_symbol,name,last_price,expiry,strike," +
			"tick_size,lot_size,instrument,segment,exchange\n" +
			"2885,RELIANCE,RELIANCE INDUSTRIES,1440,,0,0.05,1,EQ,NSE,NSE\n" +
			"26000,Nifty 50,Nifty 50,25200,,0,0,0,EQ,Indices,NSE\n" +
			"38445,HINDPETRO24APR220CE,HINDPETRO,12.6,25/04/2024,220," +
			"5,2700,CE,NFO-OPT,NFO\n";
		await using var compressed = new MemoryStream();
		await using (var gzip = new GZipStream(
			compressed,
			CompressionMode.Compress,
			true))
		{
			await gzip.WriteAsync(Encoding.UTF8.GetBytes(csv));
		}
		compressed.Position = 0;

		var instruments = await VenturaRestClient.ParseInstrumentCsv(
			compressed,
			CancellationToken.None);

		AreEqual(3, instruments.Length);
		AreEqual(SecurityTypes.Stock, instruments[0].ToSecurityType());
		AreEqual(SecurityTypes.Index, instruments[1].ToSecurityType());
		AreEqual(SecurityTypes.Option, instruments[2].ToSecurityType());
		AreEqual(
			new DateTime(2024, 4, 25, 0, 0, 0, DateTimeKind.Utc),
			instruments[2].Expiry.ToExpiry());
		AreEqual(OptionTypes.Call, instruments[2].Instrument.ToOptionType());
		AreEqual("nse:ltp", instruments[0].ToStreamAction(false));
		AreEqual("fno:ltp_depth", instruments[2].ToStreamAction(true));
		AreEqual("index:ltp", instruments[1].ToStreamAction(false));
		ThrowsExactly<NotSupportedException>(() =>
			instruments[1].ToStreamAction(true));
		AreEqual(
			"NFO|38445",
			instruments[2].ToSecurityId().Native);
	}

	[TestMethod]
	public async Task QuoteAndDepthUsePublishedRowsAndEndpoint()
	{
		var handler = new CaptureHandler(
			new ResponseSpec(
				"""
				{"success":true,"message":"","data":[
				  ["2885",1440,1410,1450,1400,1405,123456,
				   "26/07/2026 12:30:00",1500,1300,5000,6000,
				   [[100,120,2,3,1439.95,1440.05]]]
				]}
				"""));
		using var client = CreateClient(
			handler,
			"C1",
			"ACCESS".Secure());

		var update = await client.GetMarketUpdate(
			EquityInstrument(),
			true,
			CancellationToken.None);

		AreEqual(1440m, update.LastPrice);
		AreEqual(5000m, update.TotalBuyQuantity);
		AreEqual(1, update.Depth.Length);
		AreEqual(1439.95m, update.Depth[0].BuyPrice);
		AreEqual(
			new DateTime(2026, 7, 26, 7, 0, 0, DateTimeKind.Utc),
			update.ServerTime);
		var request = handler.Requests.Single();
		AreEqual(
			"https://api.example.test/instrument/v1/ltp_depth",
			request.Uri.AbsoluteUri);
		var payload = JObject.Parse(request.Body);
		AreEqual("NSE", payload["exchange"].Value<string>());
		AreEqual("2885", payload["tokens"].Single().Value<string>());
		AreEqual("Bearer ACCESS", request.Header("Authorization"));
	}

	[TestMethod]
	public async Task DeliveryAndIntradayOrdersUsePublishedRoutes()
	{
		var delivery = VenturaMessageAdapter.CreateOrderPayload(
			EquityInstrument(),
			3,
			Sides.Buy,
			VenturaProducts.CashAndCarry,
			OrderTypes.Limit,
			1440m,
			TimeInForce.PutInQueue,
			null,
			1,
			false);
		var intraday = VenturaMessageAdapter.CreateOrderPayload(
			EquityInstrument(),
			2,
			Sides.Sell,
			VenturaProducts.Intraday,
			OrderTypes.Conditional,
			0,
			TimeInForce.CancelBalance,
			1400m,
			null,
			true);
		var handler = new CaptureHandler(
			new ResponseSpec(
				"""{"client_id":"C1","security_id":"2885","order_no":"O1","status":"success","message":""}"""),
			new ResponseSpec(
				"""{"client_id":"C1","security_id":"2885","order_no":"O2","status":"success","message":""}"""));
		using var client = CreateClient(
			handler,
			"C1",
			"ACCESS".Secure());

		AreEqual(
			"O1",
			await client.PlaceOrder(
				VenturaProducts.CashAndCarry,
				delivery,
				CancellationToken.None));
		AreEqual(
			"O2",
			await client.PlaceOrder(
				VenturaProducts.Intraday,
				intraday,
				CancellationToken.None));

		AreEqual(
			"https://api.example.test/trade/v1/delivery",
			handler.Requests[0].Uri.AbsoluteUri);
		AreEqual(
			"https://api.example.test/trade/v1/intraday/regular",
			handler.Requests[1].Uri.AbsoluteUri);
		AreEqual(2885L, delivery["instrument_id"].Value<long>());
		AreEqual("E", delivery["segment"].Value<string>());
		AreEqual("B", delivery["transaction_type"].Value<string>());
		AreEqual("C", delivery["product"].Value<string>());
		AreEqual("LMT", delivery["order_type"].Value<string>());
		AreEqual(1L, delivery["disclosed_quantity"].Value<long>());
		AreEqual("SLM", intraday["order_type"].Value<string>());
		AreEqual("IOC", intraday["validity"].Value<string>());
		AreEqual(1, intraday["off_market_flag"].Value<int>());
	}

	[TestMethod]
	public async Task ModifyAndCancelUseOnlyPublishedFields()
	{
		var modify = VenturaMessageAdapter.CreateModifyPayload(
			"O1",
			10,
			OrderTypes.Conditional,
			99.5m,
			TimeInForce.PutInQueue,
			99m,
			2,
			"adjust");
		var handler = new CaptureHandler(
			new ResponseSpec(
				"""{"order_no":"O1","status":"success","message":""}"""),
			new ResponseSpec(
				"""{"order_no":"O1","status":"success","message":""}"""));
		using var client = CreateClient(
			handler,
			"C1",
			"ACCESS".Secure());

		await client.ModifyOrder(modify, CancellationToken.None);
		await client.CancelOrder("O1", CancellationToken.None);

		AreEqual(8, modify.Properties().Count());
		AreEqual("SL", modify["order_type"].Value<string>());
		AreEqual(2L, modify["disc_quantity"].Value<long>());
		AreEqual("adjust", modify["remarks"].Value<string>());
		AreEqual("O1", modify["order_no"].Value<string>());
		IsNull(modify["disclosed_quantity"]);
		AreEqual(
			"https://api.example.test/trade/v1/modify",
			handler.Requests[0].Uri.AbsoluteUri);
		AreEqual(
			"https://api.example.test/trade/v1/cancel",
			handler.Requests[1].Uri.AbsoluteUri);
		AreEqual(
			"O1",
			JObject.Parse(handler.Requests[1].Body)["order_no"]
				.Value<string>());
	}

	[TestMethod]
	public void NativeStatesProductsAndIndianTimesAreNormalized()
	{
		AreEqual(OrderStates.Active, "Order confirmed".ToOrderState());
		AreEqual(
			OrderStates.Done,
			"Order Trade confirmed".ToOrderState());
		AreEqual(
			OrderStates.Failed,
			"New order rejected".ToOrderState());
		AreEqual(
			OrderTypes.Conditional,
			JToken.FromObject(4).ToOrderType());
		AreEqual(
			TimeInForce.CancelBalance,
			JToken.FromObject(1).ToTimeInForce());
		AreEqual(VenturaProducts.Mtf, "F".ToProduct());
		AreEqual(
			new DateTime(2025, 2, 19, 5, 59, 31, DateTimeKind.Utc),
			"19-Feb-2025T11:29:31".ToVenturaTime(DateTime.UnixEpoch));
	}

	[TestMethod]
	public void MarketAndOrderWebSocketPayloadsAreDecoded()
	{
		var command = JObject.Parse(
			VenturaMarketDataClient.CreateSubscriptionCommand(
				true,
				"nse:ltp",
				["2885", "2885", "15"]));
		AreEqual("nse:ltp", command["actions"].Single().Value<string>());
		AreEqual(2, command["token"].Count());
		AreEqual("sub", command["mode"].Value<string>());

		var ltp = VenturaMarketDataClient.Decode(
			"""["nse:ltp","2885",1440,1410,1450,1400,1405,123456,"26/07/2026 12:30:00"]""",
			DateTime.UnixEpoch).Single();
		AreEqual("2885", ltp.Token);
		AreEqual(1440m, ltp.LastPrice);
		AreEqual(123456m, ltp.Volume);

		var depth = VenturaMarketDataClient.Decode(
			"""["fno:ltp_depth","38445",12.6,10,13,9,10.5,500,"26/07/2026 12:30:01",1000,1200,[[100,120,2,3,12.55,12.65]]]""",
			DateTime.UnixEpoch).Single();
		AreEqual(1, depth.Depth.Length);
		AreEqual(3L, depth.Depth[0].SellOrders);

		var status = VenturaOrderStatusClient.Decode(
			"""["Order Trade confirmed","2885","O1",3,3,1440,1439.5,"2026-07-26 12:30:02"]""",
			DateTime.UnixEpoch).Single();
		AreEqual("O1", status.OrderId);
		AreEqual(3m, status.TradedQuantity);
		AreEqual(1439.5m, status.TradePrice);
		AreEqual(
			OrderStates.Done,
			status.Message.ToOrderState());
	}

	[TestMethod]
	public void WebSocketUrlContainsRawTokenAsDocumented()
	{
		var uri = VenturaMarketDataClient.AddCredentials(
			new("wss://stream.example.test/v1/easeapi_mktdata?source=test"),
			"APP KEY",
			"CLIENT/1",
			"TOKEN+VALUE");

		AreEqual(
			"wss://stream.example.test/v1/easeapi_mktdata?source=test&app_key=APP%20KEY&client_id=CLIENT%2F1&authorization=TOKEN%2BVALUE",
			uri.AbsoluteUri);
	}

	[TestMethod]
	public async Task OrdersPositionsHoldingsAndFundsUnwrapPublishedEnvelopes()
	{
		var handler = new CaptureHandler(
			new ResponseSpec(
				"""
				{"result":[{"symbol":"RELIANCE","token":"2885","exchange":"NSE",
				  "order_id":"O1","action":"BUY","product_type":"C","order_type":2,
				  "status":"OPEN","total_quantity":3,"pending_quantity":2,
				  "executed_quantity":1,"price":1440,
				  "order_date_time":"19-Feb-2025T11:29:31","validity":0}],
				 "error_message":""}
				"""),
			new ResponseSpec(
				"""
				{"result":{"open_positions":[{"symbol":"RELIANCE","token":"2885",
				  "exchange":"NSE","action":"BUY","product_type":"I",
				  "average_traded_price":1440,"total_quantity":3,
				  "profit_loss":10}],"closed_positions":[]},"error_message":""}
				"""),
			new ResponseSpec(
				"""
				{"result":[{"symbol":"RELIANCE","isin":"INE002A01018",
				  "quantity":5,"average_traded_price":1400,
				  "last_traded_price":1440,"exchange":"NSE","profit_loss":200}],
				 "error_message":""}
				"""),
			new ResponseSpec(
				"""
				{"status":"success","error":false,"data":{"available_to_trade":9000,
				  "withdrawable_balance":8000,"total_margin":{"total":10000,
				  "ledger_balance":7000},"utilised_margin":{"total":1000,
				  "pending_order_margin":200,"position_margin":800}}}
				"""));
		using var client = CreateClient(
			handler,
			"C1",
			"ACCESS".Secure());

		var order = (await client.GetOrders(CancellationToken.None)).Single();
		var position =
			(await client.GetPositions(CancellationToken.None)).Single();
		var holding =
			(await client.GetHoldings(CancellationToken.None)).Single();
		var funds = await client.GetFunds(CancellationToken.None);

		AreEqual("O1", order.OrderId);
		AreEqual(OrderTypes.Limit, order.OrderType.ToOrderType());
		AreEqual(3m, position.TotalQuantity);
		AreEqual("INE002A01018", holding.Isin);
		AreEqual(9000m, funds.AvailableToTrade);
		AreEqual(1000m, funds.UtilizedMargin.Total);
	}

	[TestMethod]
	public async Task HttpErrorsAndInvalidJsonAreRejected()
	{
		var unauthorized = new CaptureHandler(
			new ResponseSpec(
				"""{"status":"error","message":"Auth token expired"}""",
				HttpStatusCode.Unauthorized));
		using (var client = CreateClient(
			unauthorized,
			"C1",
			"ACCESS".Secure()))
		{
			var error = await ThrowsExactlyAsync<InvalidOperationException>(
				() => client.GetOrders(CancellationToken.None));
			IsTrue(error.Message.Contains("HTTP 401"));
			IsTrue(error.Message.Contains("Auth token expired"));
		}

		var invalid = new CaptureHandler(new ResponseSpec("<html>"));
		using (var client = CreateClient(
			invalid,
			"C1",
			"ACCESS".Secure()))
		{
			await ThrowsExactlyAsync<InvalidDataException>(
				() => client.GetOrders(CancellationToken.None));
		}
	}

	private static VenturaRestClient CreateClient(
		HttpMessageHandler handler,
		string clientId = null,
		SecureString token = null)
		=> new(
			new Uri("https://api.example.test/"),
			"KEY".Secure(),
			clientId,
			token,
			handler);

	private static VenturaInstrument EquityInstrument()
		=> new()
		{
			ExchangeToken = "2885",
			TradingSymbol = "RELIANCE",
			Name = "RELIANCE INDUSTRIES",
			LastPrice = 1440m,
			TickSize = 0.05m,
			LotSize = 1,
			Instrument = "EQ",
			Segment = "NSE",
			Exchange = "NSE",
		};

	private sealed record ResponseSpec(
		string Body,
		HttpStatusCode StatusCode = HttpStatusCode.OK,
		string ContentType = "application/json");

	private sealed record CapturedRequest(
		Uri Uri,
		HttpMethod Method,
		IReadOnlyDictionary<string, string> Headers,
		string Body)
	{
		public string Header(string name)
			=> Headers.TryGetValue(name, out var value) ? value : null;
	}

	private sealed class CaptureHandler : HttpMessageHandler
	{
		private readonly Queue<ResponseSpec> _responses;

		public CaptureHandler(params ResponseSpec[] responses)
		{
			_responses = new(responses);
		}

		public List<CapturedRequest> Requests { get; } = [];

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			var headers = request.Headers.ToDictionary(
				header => header.Key,
				header => string.Join(",", header.Value),
				StringComparer.OrdinalIgnoreCase);
			if (request.Content != null)
			{
				foreach (var header in request.Content.Headers)
					headers[header.Key] = string.Join(",", header.Value);
			}
			var body = request.Content == null
				? null
				: await request.Content.ReadAsStringAsync(cancellationToken);
			Requests.Add(new(
				request.RequestUri,
				request.Method,
				headers,
				body));
			if (_responses.Count == 0)
			{
				throw new InvalidOperationException(
					"No fake Ventura EaseAPI response was configured.");
			}

			var response = _responses.Dequeue();
			return new(response.StatusCode)
			{
				Content = new StringContent(
					response.Body,
					Encoding.UTF8,
					response.ContentType),
			};
		}
	}
}
