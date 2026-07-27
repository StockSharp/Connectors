namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.IO;
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

using Google.Protobuf;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.HdfcSecurities;
using StockSharp.HdfcSecurities.Native;
using StockSharp.HdfcSecurities.Native.Protocol;
using StockSharp.Messages;

[TestClass]
public class HdfcSecuritiesTests : BaseTestClass
{
	[TestMethod]
	public void SettingsRoundTripKeepsAuthenticationAndEndpoints()
	{
		var source = new HdfcMessageAdapter(new IncrementalIdGenerator())
		{
			Key = "API KEY".Secure(),
			Secret = "SECRET".Secure(),
			RequestToken = "REQUEST".Secure(),
			Token = "ACCESS".Secure(),
			PortfolioName = "CLIENT-1",
			DefaultProduct = HdfcProducts.Intraday,
			PollingInterval = TimeSpan.FromSeconds(27),
			ReconnectAttempts = 6,
			RestAddress = new("https://rest.example.test/oapi/"),
			InstrumentAddress =
				new("https://public.example.test/security-master"),
			WebSocketAddress = new("wss://stream.example.test/session"),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new HdfcMessageAdapter(new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual(source.Key.UnSecure(), target.Key.UnSecure());
		AreEqual(source.Secret.UnSecure(), target.Secret.UnSecure());
		AreEqual(
			source.RequestToken.UnSecure(),
			target.RequestToken.UnSecure());
		AreEqual(source.Token.UnSecure(), target.Token.UnSecure());
		AreEqual(source.PortfolioName, target.PortfolioName);
		AreEqual(source.DefaultProduct, target.DefaultProduct);
		AreEqual(source.PollingInterval, target.PollingInterval);
		AreEqual(source.ReconnectAttempts, target.ReconnectAttempts);
		AreEqual(source.RestAddress, target.RestAddress);
		AreEqual(source.InstrumentAddress, target.InstrumentAddress);
		AreEqual(source.WebSocketAddress, target.WebSocketAddress);
		AreEqual(
			"https://rest.example.test/oapi/v1/login?api_key=API%20KEY",
			target.CreateAuthorizationUri().AbsoluteUri);
		IsTrue(target is IKeySecretAdapter);
		IsTrue(target is ITokenAdapter);
	}

	[TestMethod]
	public void DefaultsMatchPublishedInvestRightEndpoints()
	{
		var adapter = new HdfcMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			"https://developer.hdfcsec.com/oapi/",
			adapter.RestAddress.AbsoluteUri);
		AreEqual(
			"https://developer.hdfcsec.com/oapi/v1/security-master",
			adapter.InstrumentAddress.AbsoluteUri);
		AreEqual(
			"wss://developer.hdfcsec.com/wsapi/v1/session",
			adapter.WebSocketAddress.AbsoluteUri);
		AreEqual(5, adapter.SupportedOrderBookDepths.Single());
		IsFalse(adapter.IsAllDownloadingSupported(
			TimeSpan.FromMinutes(1).TimeFrame()));
	}

	[TestMethod]
	public async Task AccessTokenExchangeUsesPublishedQueryAndSecretBody()
	{
		var handler = new CaptureHandler(
			new ResponseSpec(
				"""{"status":"success","data":{"accessToken":"ACCESS-7"}}"""));
		using var client = CreateClient(handler);

		var token = await client.ExchangeAccessToken(
			"SECRET VALUE".Secure(),
			"REQUEST VALUE".Secure(),
			CancellationToken.None);

		AreEqual("ACCESS-7", token);
		AreEqual("ACCESS-7", client.Token);
		var request = handler.Requests.Single();
		AreEqual(HttpMethod.Post, request.Method);
		AreEqual(
			"https://api.example.test/oapi/v1/access-token?api_key=KEY&request_token=REQUEST%20VALUE",
			request.Uri.AbsoluteUri);
		IsNull(request.Header("Authorization"));
		AreEqual(
			"SECRET VALUE",
			JObject.Parse(request.Body)["apiSecret"].Value<string>());
	}

	[TestMethod]
	public async Task ProfileUsesRawAccessTokenAndApiKey()
	{
		var handler = new CaptureHandler(
			new ResponseSpec(
				"""{"status":"success","data":[{"user_id":"CLIENT-7","user_name":"Alice"}]}"""));
		using var client = CreateClient(handler, "ACCESS".Secure());

		var profile = await client.GetProfile(CancellationToken.None);

		AreEqual("CLIENT-7", profile.UserId);
		AreEqual("Alice", profile.UserName);
		var request = handler.Requests.Single();
		AreEqual(HttpMethod.Post, request.Method);
		AreEqual(
			"https://api.example.test/oapi/v3/user/profile?api_key=KEY",
			request.Uri.AbsoluteUri);
		AreEqual("ACCESS", request.Header("Authorization"));
		IsTrue(request.Header("User-Agent").Contains(
			"StockSharp-HDFC-Securities"));
	}

	[TestMethod]
	public async Task SecurityMasterMapsCashOptionsAndCurrency()
	{
		const string csv =
			"exchange,security_id,instrument_segment,expiry_date," +
			"strike_price,option_type,lot_size,tick_size,close_price," +
			"exch_security_id,symbol_name,underline_symbol,open_price\n" +
			"NSE,WIPLTDEQNR,EQUITY,,,,1,0.05,458,21840,WIPRO,,450\n" +
			"NSE,68180,OPTIDX,2026-07-30,22400,CE,50,0.05,95,68180," +
			"NIFTY26JUL22400CE,NIFTY,90\n" +
			"NSE,USDINR26JULFUT,FUTCUR,2026-07-29,,,1000,0.0025,83," +
			"90001,USDINR26JULFUT,USDINR,82\n";
		await using var stream = new MemoryStream(
			Encoding.UTF8.GetBytes(csv));

		var instruments = await HdfcRestClient.ParseInstrumentCsv(
			stream,
			CancellationToken.None);

		AreEqual(3, instruments.Length);
		AreEqual(
			SecurityTypes.Stock,
			instruments[0].ToSecurityType());
		AreEqual(
			SecurityTypes.Option,
			instruments[1].ToSecurityType());
		AreEqual(
			SecurityTypes.Future,
			instruments[2].ToSecurityType());
		AreEqual(OptionTypes.Call, instruments[1].OptionType.ToOptionType());
		AreEqual(
			new DateTime(2026, 7, 30, 0, 0, 0, DateTimeKind.Utc),
			instruments[1].ExpiryDate.ToExpiry());
		AreEqual("NFO_68180", instruments[1].ToStreamId());
		AreEqual("NCD_90001", instruments[2].ToStreamId());
		AreEqual(
			"NSE|68180",
			instruments[1].ToSecurityId().Native);
	}

	[TestMethod]
	public async Task LtpUsesExchangeTokenAndRawAuthorization()
	{
		var handler = new CaptureHandler(
			new ResponseSpec(
				"""{"data":[{"prev_close":1106.5,"ltp":1089.75,"exchange":"NSE","token":"21840"}],"meta":{"statusCode":"OK","statusMsg":"OK"}}"""));
		using var client = CreateClient(handler, "ACCESS".Secure());
		var instrument = EquityInstrument();

		var ltp = await client.GetLtp(
			[instrument],
			CancellationToken.None);

		AreEqual(1089.75m, ltp.Single().LastPrice);
		var request = handler.Requests.Single();
		AreEqual(HttpMethod.Put, request.Method);
		AreEqual(
			"https://api.example.test/oapi/v1/fetch-ltp?api_key=KEY",
			request.Uri.AbsoluteUri);
		AreEqual("ACCESS", request.Header("Authorization"));
		var item = JObject.Parse(request.Body)["data"].Single();
		AreEqual("NSE", item["exchange"].Value<string>());
		AreEqual("21840", item["token"].Value<string>());
	}

	[TestMethod]
	public async Task EquityOrderUsesPublishedFieldsAndEndpoint()
	{
		var payload = HdfcMessageAdapter.CreateOrderPayload(
			EquityInstrument(),
			3,
			Sides.Buy,
			HdfcProducts.Delivery,
			OrderTypes.Limit,
			458m,
			TimeInForce.PutInQueue,
			null,
			false,
			123456789);
		var handler = new CaptureHandler(
			new ResponseSpec(
				"""{"status":"success","data":{"order_id":"24042500000504"}}"""));
		using var client = CreateClient(handler, "ACCESS".Secure());

		var orderId = await client.PlaceOrder(
			payload,
			CancellationToken.None);

		AreEqual("24042500000504", orderId);
		AreEqual("NSE", payload["exchange"].Value<string>());
		AreEqual("WIPLTDEQNR", payload["security_id"].Value<string>());
		AreEqual("EQUITY", payload["instrument_segment"].Value<string>());
		AreEqual("BUY", payload["transaction_type"].Value<string>());
		AreEqual("DELIVERY", payload["product"].Value<string>());
		AreEqual("LIMIT", payload["order_type"].Value<string>());
		AreEqual(458m, payload["price"].Value<decimal>());
		AreEqual(3L, payload["quantity"].Value<long>());
		AreEqual("DAY", payload["validity"].Value<string>());
		IsFalse(payload["amo"].Value<bool>());
		AreEqual(
			123456789L,
			payload["external_reference_number"].Value<long>());
		var request = handler.Requests.Single();
		AreEqual(
			"https://api.example.test/oapi/v1/orders/regular?api_key=KEY",
			request.Uri.AbsoluteUri);
		AreEqual(HttpMethod.Post, request.Method);
	}

	[TestMethod]
	public void DerivativeStopOrdersAndValidationMatchApiRules()
	{
		var option = new HdfcInstrument
		{
			Exchange = "NSE",
			SecurityId = "68180",
			InstrumentSegment = "OPTIDX",
			ExpiryDate = "20260730",
			StrikePrice = 22400m,
			OptionType = "CE",
			LotSize = 50,
			TickSize = 0.05m,
			ExchangeSecurityId = "68180",
			SymbolName = "NIFTY26JUL22400CE",
			UnderlyingSymbol = "NIFTY",
		};
		var stopMarket = HdfcMessageAdapter.CreateOrderPayload(
			option,
			50,
			Sides.Sell,
			HdfcProducts.Overnight,
			OrderTypes.Conditional,
			0,
			TimeInForce.CancelBalance,
			100.25m,
			true,
			null);

		AreEqual("SL-M", stopMarket["order_type"].Value<string>());
		AreEqual("IOC", stopMarket["validity"].Value<string>());
		AreEqual("SELL", stopMarket["transaction_type"].Value<string>());
		AreEqual("NIFTY", stopMarket["underlying_symbol"].Value<string>());
		AreEqual("20260730", stopMarket["expiry_date"].Value<string>());
		AreEqual("CE", stopMarket["option_type"].Value<string>());
		AreEqual(22400m, stopMarket["strike_price"].Value<decimal>());
		IsTrue(stopMarket["amo"].Value<bool>());
		ThrowsExactly<InvalidOperationException>(() =>
			HdfcMessageAdapter.CreateOrderPayload(
				option,
				50,
				Sides.Buy,
				HdfcProducts.Overnight,
				OrderTypes.Conditional,
				0,
				null,
				null,
				false,
				null));
		ThrowsExactly<ArgumentOutOfRangeException>(() =>
			HdfcMessageAdapter.CreateOrderPayload(
				option,
				1.5m,
				Sides.Buy,
				HdfcProducts.Overnight,
				OrderTypes.Market,
				0,
				null,
				null,
				false,
				null));
	}

	[TestMethod]
	public void ModifyPayloadContainsOnlyPublishedMutableFields()
	{
		var payload = HdfcMessageAdapter.CreateModifyPayload(
			10,
			HdfcProducts.Intraday,
			OrderTypes.Conditional,
			99.5m,
			TimeInForce.PutInQueue,
			99m,
			2,
			false);

		AreEqual("INTRADAY", payload["product"].Value<string>());
		AreEqual(10L, payload["quantity"].Value<long>());
		AreEqual("SL", payload["order_type"].Value<string>());
		AreEqual(99.5m, payload["price"].Value<decimal>());
		AreEqual(99m, payload["trigger_price"].Value<decimal>());
		AreEqual(2L, payload["disclosed_quantity"].Value<long>());
		AreEqual("DAY", payload["validity"].Value<string>());
		AreEqual(8, payload.Properties().Count());
	}

	[TestMethod]
	public void NativeStatesProductsAndIndianTimestampsAreNormalized()
	{
		AreEqual(OrderStates.Active, "Modified".ToOrderState());
		AreEqual(OrderStates.Done, "Traded".ToOrderState());
		AreEqual(OrderStates.Failed, "Rejected".ToOrderState());
		AreEqual(OrderTypes.Conditional, "SL-M".ToOrderType());
		AreEqual(
			TimeInForce.CancelBalance,
			"IOC".ToTimeInForce());
		AreEqual(
			HdfcProducts.CollateralSell,
			"COLL-SELL".ToProduct());
		AreEqual(
			new DateTime(2024, 4, 30, 7, 10, 12, DateTimeKind.Utc),
			"30/04/2024 12:40:12".ToHdfcTime(DateTime.UnixEpoch));
		var epoch = DateTimeOffset
			.FromUnixTimeMilliseconds(1785047100000)
			.UtcDateTime;
		AreEqual(epoch, 1785047100000000L.ToHdfcTime(DateTime.UnixEpoch));
		AreEqual(
			epoch,
			1785047100000000000L.ToHdfcTime(DateTime.UnixEpoch));
	}

	[TestMethod]
	public void PortfolioModelsDeserializePublishedEnvelopes()
	{
		var position = JsonConvert.DeserializeObject<HdfcPosition>(
			"""
			{
			  "client_id":"123456",
			  "security_id":"43382",
			  "instrument_segment":"OPTIDX",
			  "underlying_symbol":"NIFTY",
			  "product":"OVERNIGHT",
			  "exchange":"NSE",
			  "net_qty":75,
			  "average_buy_price":2.3,
			  "average_sell_price":2.5,
			  "realised_pl_overall_position":5.0
			}
			""");
		var holding = JsonConvert.DeserializeObject<HdfcHolding>(
			"""
			{
			  "security_id":"WIPLTDEQNR",
			  "exchange":"NSE",
			  "company_name":"WIPRO",
			  "isin":"INE075A01022",
			  "quantity":2,
			  "average_price":40.67,
			  "close_price":42.28,
			  "ltcg_quantity":1
			}
			""");
		var margins = JsonConvert.DeserializeObject<HdfcMargins>(
			"""
			{
			  "total_available_limit":9.99,
			  "total_utilised_limit":184,
			  "total_limit":193.99
			}
			""");

		AreEqual(75m, position.NetQuantity);
		AreEqual(2.3m, position.AverageBuyPrice);
		AreEqual(2m, holding.Quantity);
		AreEqual("INE075A01022", holding.Isin);
		AreEqual(9.99m, margins.Available);
		AreEqual(184m, margins.Utilized);
	}

	[TestMethod]
	public void ProtobufMarketPacketAndSubscriptionJsonAreDecoded()
	{
		var packet = new GenericDTOList
		{
			GenericDTOList_ =
			{
				new GenericDTO
				{
					InstrumentId = 21840,
					PacketTimestamp = 1785047100000,
					PacketType = PacketType.NseCmAll,
					MbpData = new MBPData
					{
						LastTradedPrice = 2450.75,
						LastTradeTime = 1785047101000,
						OpenPrice = 2400,
						HighPrice = 2460,
						LowPrice = 2390,
						ClosingPrice = 2380,
						LastTradeQuantity = 4,
						VolumeTradedToday = 12345,
						MarketDepthDTOList = new MarketDepthDTOList
						{
							MarketDepthDTO =
							{
								new MarketDepthDTO
								{
									Price = 2450.70,
									Quantity = 10,
									NumberOfOrders = 2,
									BuyFlag = true,
								},
								new MarketDepthDTO
								{
									Price = 2450.80,
									Quantity = 11,
									NumberOfOrders = 3,
									BuyFlag = false,
								},
							},
						},
					},
				},
			},
		};

		var update = HdfcSocketClient.Decode(
			packet.ToByteArray(),
			DateTime.UnixEpoch).Single();

		AreEqual("NSE_21840", update.StreamId);
		AreEqual(2450.75m, update.LastPrice);
		AreEqual(4L, update.LastQuantity);
		AreEqual(2, update.Depth.Length);
		IsTrue(update.Depth[0].IsBid);
		AreEqual(3L, update.Depth[1].Orders);

		var subscribe = JObject.Parse(
			HdfcSocketClient.CreateSubscriptionCommand(
				true,
				["NSE_21840", "NSE_21840", "BSE_1"]));
		AreEqual(2, subscribe["subscribe"].Count());
		AreEqual("ALL", subscribe["subscribe"][0]["type"]);
		AreEqual(0, subscribe["unSubscribe"].Count());
		IsFalse(subscribe["heart_beat"].Value<bool>());
		ThrowsExactly<InvalidDataException>(() =>
			HdfcSocketClient.Decode([0xff, 0x80], DateTime.UnixEpoch));
	}

	[TestMethod]
	public async Task HttpErrorsAndInvalidJsonAreRejected()
	{
		var unauthorized = new CaptureHandler(
			new ResponseSpec(
				"""{"status":"failed","message":"Access token expired"}""",
				HttpStatusCode.Unauthorized));
		using (var client = CreateClient(
			unauthorized,
			"ACCESS".Secure()))
		{
			var error = await ThrowsExactlyAsync<InvalidOperationException>(
				() => client.GetOrders(CancellationToken.None));
			IsTrue(error.Message.Contains("HTTP 401"));
			IsTrue(error.Message.Contains("Access token expired"));
		}

		var invalid = new CaptureHandler(new ResponseSpec("<html>"));
		using (var client = CreateClient(invalid, "ACCESS".Secure()))
		{
			await ThrowsExactlyAsync<InvalidDataException>(
				() => client.GetOrders(CancellationToken.None));
		}
	}

	private static HdfcRestClient CreateClient(
		HttpMessageHandler handler,
		SecureString token = null)
		=> new(
			new Uri("https://api.example.test/oapi/"),
			new Uri("https://public.example.test/security-master"),
			"KEY".Secure(),
			token,
			handler);

	private static HdfcInstrument EquityInstrument()
		=> new()
		{
			Exchange = "NSE",
			SecurityId = "WIPLTDEQNR",
			InstrumentSegment = "EQUITY",
			LotSize = 1,
			TickSize = 0.05m,
			ExchangeSecurityId = "21840",
			SymbolName = "WIPRO",
		};

	private sealed record ResponseSpec(
		string Body,
		HttpStatusCode StatusCode = HttpStatusCode.OK);

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
				{
					headers[header.Key] =
						string.Join(",", header.Value);
				}
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
					"No fake HDFC Securities response was configured.");
			}

			var response = _responses.Dequeue();
			return new(response.StatusCode)
			{
				Content = new StringContent(
					response.Body,
					Encoding.UTF8,
					"application/json"),
			};
		}
	}
}
