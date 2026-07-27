namespace StockSharp.Connectors.Tests;

using System;
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

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using StockSharp.Jainam;
using StockSharp.Jainam.Native;
using StockSharp.Messages;

[TestClass]
public class JainamTests : BaseTestClass
{
	[TestMethod]
	public void SettingsRoundTripKeepsVendorCredentialsAndEndpoints()
	{
		var source = new JainamMessageAdapter(new IncrementalIdGenerator())
		{
			UserId = "DK2200295",
			AppCode = "APP-CODE",
			ApiSecret = "api-secret".Secure(),
			AuthCode = "auth-code".Secure(),
			Token = "user-session".Secure(),
			PortfolioName = "PORTFOLIO",
			DefaultProduct = JainamProducts.Intraday,
			ReconnectAttempts = 7,
			PollingInterval = TimeSpan.FromSeconds(17),
			RestAddress = new("https://api.example.test/"),
			InstrumentAddress = "https://static.example.test/contracts/",
			WebSocketAddress = "wss://socket.example.test/feed/",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new JainamMessageAdapter(new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual(source.UserId, target.UserId);
		AreEqual(source.AppCode, target.AppCode);
		AreEqual(source.ApiSecret.UnSecure(), target.ApiSecret.UnSecure());
		AreEqual(source.AuthCode.UnSecure(), target.AuthCode.UnSecure());
		AreEqual(source.Token.UnSecure(), target.Token.UnSecure());
		AreEqual(source.PortfolioName, target.PortfolioName);
		AreEqual(source.DefaultProduct, target.DefaultProduct);
		AreEqual(source.ReconnectAttempts, target.ReconnectAttempts);
		AreEqual(source.PollingInterval, target.PollingInterval);
		AreEqual(source.RestAddress, target.RestAddress);
		AreEqual(source.InstrumentAddress, target.InstrumentAddress);
		AreEqual(source.WebSocketAddress, target.WebSocketAddress);
	}

	[TestMethod]
	public void CashMasterPreservesNativeIdentifier()
	{
		var instruments = JainamRestClient.ParseInstrumentMaster(
			"nse",
			"""
			{"NSE":[{
			  "trading_symbol":"RELIANCE-EQ",
			  "symbol":"RELIANCE",
			  "group_name":"EQ",
			  "exch":"NSE",
			  "lot_size":"1",
			  "instrument_type":"0",
			  "formatted_ins_name":"RELIANCE-EQ",
			  "exchange_segment":"nse_cm",
			  "tick_size":"0.05",
			  "token":"2885"
			}]}
			""");

		AreEqual(1, instruments.Length);
		var instrument = instruments[0];
		AreEqual("RELIANCE", instrument.Symbol);
		AreEqual(SecurityTypes.Stock, instrument.ToSecurityType());
		AreEqual("NSE", instrument.ToSecurityId().BoardCode);
		AreEqual("RELIANCE-EQ", instrument.ToSecurityId().SecurityCode);
		AreEqual("NSE|2885", instrument.ToSecurityId().Native);
	}

	[TestMethod]
	public void DerivativeMasterMapsOptionAndFutureFields()
	{
		var instruments = JainamRestClient.ParseInstrumentMaster(
			"nfo",
			"""
			{"NFO":[
			  {
			    "symbol":"BANKNIFTY",
			    "option_type":"CE",
			    "expiry_date":"1790640000000",
			    "instrument_type":"OPTIDX",
			    "formatted_ins_name":"BANKNIFTY 29th SEP 72600 CE",
			    "exchange_segment":"nse_fo",
			    "tick_size":"0.05",
			    "token":"35000",
			    "trading_symbol":"BANKNIFTY29SEP26C72600",
			    "exch":"NFO",
			    "lot_size":"30",
			    "strike_price":"72600"
			  },
			  {
			    "symbol":"NIFTY",
			    "expiry_date":"1790640000000",
			    "instrument_type":"FUTIDX",
			    "token":"35001",
			    "trading_symbol":"NIFTY29SEP26F",
			    "exch":"NFO",
			    "lot_size":"65",
			    "tick_size":"0.05"
			  }
			]}
			""");

		AreEqual(SecurityTypes.Option, instruments[0].ToSecurityType());
		AreEqual(OptionTypes.Call, instruments[0].OptionType.ToOptionType());
		AreEqual(72600m, instruments[0].StrikePrice.ToDecimal());
		AreEqual(
			new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc),
			instruments[0].ExpiryTime.ToExpiry());
		AreEqual(SecurityTypes.Future, instruments[1].ToSecurityType());
	}

	[TestMethod]
	public void CurrencyAndCommodityMastersMapAssetClasses()
	{
		var currency = JainamRestClient.ParseInstrumentMaster(
			"cds",
			"""
			{"CDS":[{
			  "symbol":"EURUSD",
			  "instrument_type":"FUTCUR",
			  "token":"1001",
			  "trading_symbol":"EURUSD29JUL26F",
			  "exch":"CDS",
			  "lot_size":"1"
			}]}
			""")[0];
		var commodity = JainamRestClient.ParseInstrumentMaster(
			"mcx",
			"""
			{"MCX":[{
			  "symbol":"SILVERM",
			  "instrument_type":"OPTFUT",
			  "option_type":"PE",
			  "token":"509611",
			  "trading_symbol":"SILVERM28JUL26P279000",
			  "exch":"MCX",
			  "lot_size":"1"
			}]}
			""")[0];

		AreEqual(SecurityTypes.Future, currency.ToSecurityType());
		AreEqual(SecurityTypes.Option, commodity.ToSecurityType());
		AreEqual("CDS", currency.ToSecurityId().BoardCode);
		AreEqual("MCX", commodity.ToSecurityId().BoardCode);
	}

	[TestMethod]
	public void IndexEnvelopeExpandsAllExchangeGroups()
	{
		var instruments = JainamRestClient.ParseInstrumentMaster(
			"indices",
			"""
			{"INDICES":[
			  {"NSE":[{"symbol":"NIFTY 50","exch":"NSE","token":"26000"}]},
			  {"BSE":[{"symbol":"SENSEX","exch":"BSE","token":"1"}]},
			  {"MCX":[{"symbol":"MCX METAL","exch":"MCX","token":"100"}]}
			]}
			""");

		AreEqual(3, instruments.Length);
		IsTrue(instruments.All(instrument => instrument.IsIndex));
		IsTrue(instruments.All(
			instrument => instrument.ToSecurityType() == SecurityTypes.Index));
		AreEqual("NIFTY 50", instruments[0].TradingSymbol);
		AreEqual("BSE|1", instruments[1].ToSecurityId().Native);
	}

	[TestMethod]
	public void ResponseEnvelopeHandlesObjectArrayNoDataAndErrors()
	{
		var profile = JainamRestClient.ParseResponse(
			"profile",
			"""
			{"status":"Ok","message":"Success","result":{"clientId":"DK2200295"}}
			""");
		AreEqual("DK2200295", profile.Result["clientId"].Value<string>());

		var orders = JainamRestClient.ParseResponse(
			"orders",
			"""
			{"status":"Ok","message":"Success","result":[{"brokerOrderId":"O1"}]}
			""");
		AreEqual("O1", orders.Result[0]["brokerOrderId"].Value<string>());

		IsNull(JainamRestClient.ParseResponse(
			"positions",
			"""{"status":"Not ok","message":"No data found","result":[]}""",
			true));
		ThrowsExactly<InvalidOperationException>(() =>
			JainamRestClient.ParseResponse(
				"orders",
				"""{"status":"Not ok","message":"Unauthorized","result":[]}"""));
		ThrowsExactly<InvalidDataException>(() =>
			JainamRestClient.ParseResponse("orders", "<html>"));
	}

	[TestMethod]
	public void NativeCodesMatchPublishedAppendix()
	{
		AreEqual("LONGTERM", JainamProducts.LongTerm.ToNative());
		AreEqual("INTRADAY", JainamProducts.Intraday.ToNative());
		AreEqual("MTF", JainamProducts.Mtf.ToNative());
		AreEqual(JainamProducts.Intraday, "MIS".ToProduct());
		AreEqual("AMO", JainamOrderComplexities.AfterMarket.ToNative());
		AreEqual("BUY", Sides.Buy.ToNative());
		AreEqual(Sides.Sell, "SELL".ToSide());
		AreEqual("SL", OrderTypes.Conditional.ToNative(100m));
		AreEqual("SLM", OrderTypes.Conditional.ToNative(0m));
		AreEqual(OrderTypes.Conditional, "SLM".ToOrderType());
		AreEqual("IOC", ((TimeInForce?)TimeInForce.CancelBalance).ToValidity());
		AreEqual(TimeInForce.PutInQueue, "DAY".ToTimeInForce());
		AreEqual(OrderStates.Active, "OPEN".ToOrderState());
		AreEqual(OrderStates.Done, "COMPLETE".ToOrderState());
		AreEqual(OrderStates.Pending, "validation pending".ToOrderState());
		AreEqual(OrderStates.Failed, "REJECTED".ToOrderState());
	}

	[TestMethod]
	public void HashesFollowDocumentedSsoAndSocketAlgorithms()
	{
		AreEqual(
			"46e1856dfbe1985d60189378534f4022e12249ba2547a2788682df3d6cde1551",
			"USERAUTHSECRET".Sha256());
		AreEqual(
			"24e0bcd5bc6a381c7151f7f3b23b467b96cf9790accd21006e48bf3185b6c24e",
			"session-token".DoubleSha256());
	}

	[TestMethod]
	public void SparseDepthUpdatesMergeWithAcknowledgedSnapshot()
	{
		var state = new JainamMarketUpdate();
		state.Apply(JsonConvert.DeserializeObject<JainamMarketUpdate>(
			"""
			{"t":"dk","e":"NFO","tk":"54957","lp":"76.40",
			 "bp1":"76.30","bq1":"50","bo1":"1",
			 "sp1":"76.45","sq1":"650","so1":"2",
			 "bp2":"76.25","bq2":"2000","bo2":"9"}
			"""));
		state.Apply(JsonConvert.DeserializeObject<JainamMarketUpdate>(
			"""
			{"t":"df","e":"NFO","tk":"54957","lp":"76.55",
			 "sq1":"3","so1":"1"}
			"""));

		AreEqual(76.55m, state.LastPrice.ToDecimal());
		AreEqual(2, state.GetBids().Length);
		AreEqual(76.30m, state.GetBids()[0].Price);
		AreEqual(76.45m, state.GetAsks()[0].Price);
		AreEqual(3m, state.GetAsks()[0].Volume);
		AreEqual(1, state.GetAsks()[0].OrdersCount);
	}

	[TestMethod]
	public async Task SsoExchangeSendsDocumentedChecksum()
	{
		var handler = new CaptureHandler(
			"""
			{"status":"Ok","message":"Success","result":[{
			  "userSession":"SESSION",
			  "clientId":"DK2200295"
			}]}
			""");
		using var client = new JainamRestClient(
			new("https://protrade.jainam.in/"),
			"https://protrade.jainam.in/contract/json/",
			handler);

		var login = await client.Authenticate(
			"USER",
			(SecureString)null,
			"AUTH".Secure(),
			"SECRET".Secure(),
			CancellationToken.None);

		AreEqual("SESSION", login.token);
		AreEqual("DK2200295", login.userId);
		AreEqual(
			"https://protrade.jainam.in/omt/auth/sso/vendor/getUserDetails",
			handler.RequestUri.AbsoluteUri);
		IsNull(handler.Authorization);
		IsTrue(handler.Body.Contains(
			"46e1856dfbe1985d60189378534f4022e12249ba2547a2788682df3d6cde1551"));
	}

	[TestMethod]
	public async Task AuthenticatedRequestUsesRawUserSessionHeader()
	{
		var handler = new CaptureHandler(
			"""
			{"status":"Ok","message":"Success","result":{
			  "clientId":"DK2200295",
			  "clientName":"Demo",
			  "exchanges":["NSE"],
			  "products":["LONGTERM"],
			  "orderComplexity":["REGULAR"]
			}}
			""");
		using var client = new JainamRestClient(
			new("https://protrade.jainam.in/"),
			"https://protrade.jainam.in/contract/json/",
			handler);
		await client.Authenticate(
			"DK2200295",
			"SESSION.JWT".Secure(),
			(SecureString)null,
			(SecureString)null,
			CancellationToken.None);

		var profile = await client.GetProfile(CancellationToken.None);

		AreEqual("DK2200295", profile.ClientId);
		AreEqual(
			"https://protrade.jainam.in/omt/api-order-rest/v1/profile/",
			handler.RequestUri.AbsoluteUri);
		AreEqual("SESSION.JWT", handler.Authorization);
	}

	[TestMethod]
	public void PublishedAccountModelsDeserialize()
	{
		var order = JsonConvert.DeserializeObject<JainamOrder>(
			"""
			{
			  "clientId":"DK2200295",
			  "brokerOrderId":"250526000002881",
			  "exchange":"NSE",
			  "tradingSymbol":"IDEA-EQ",
			  "instrumentId":"14366",
			  "transactionType":"BUY",
			  "quantity":10,
			  "product":"LONGTERM",
			  "orderComplexity":"REGULAR",
			  "orderType":"LIMIT",
			  "price":6.30,
			  "averageTradedPrice":0,
			  "pendingQuantity":10,
			  "filledQuantity":0,
			  "orderStatus":"OPEN"
			}
			""");
		var position = JsonConvert.DeserializeObject<JainamPosition>(
			"""
			{
			  "instrumentId":"20776",
			  "tradingSymbol":"BHAGYANGR-EQ",
			  "exchange":"NSE",
			  "product":"LONGTERM",
			  "netQuantity":1,
			  "netAveragePrice":78.14,
			  "previousDayClose":78.33,
			  "realizedPnl":0
			}
			""");
		var limits = JsonConvert.DeserializeObject<JainamLimits>(
			"""
			{
			  "tradingLimit":0,
			  "openingCashLimit":52926.40,
			  "collateralMargin":47735.39,
			  "utilizedMargin":69.95,
			  "blockedForPayout":0
			}
			""");

		AreEqual("250526000002881", order.OrderId);
		AreEqual(10m, order.PendingQuantity);
		AreEqual(1m, position.NetQuantity);
		AreEqual(78.14m, position.NetAveragePrice);
		AreEqual(52926.40m, limits.OpeningCashLimit);
		AreEqual(69.95m, limits.UtilizedMargin);
	}

	[TestMethod]
	public void IndiaTimesAndInstrumentKeysAreNormalized()
	{
		AreEqual(
			new DateTime(2026, 7, 26, 3, 45, 0, DateTimeKind.Utc),
			"2026-07-26 09:15:00".ToJainamTime());
		AreEqual(
			new DateTime(2026, 7, 26, 4, 2, 15, DateTimeKind.Utc).TimeOfDay,
			"09:32:15".ToJainamTime().Value.TimeOfDay);
		AreEqual("NFO|54957", "nfo".ToInstrumentKey("54957"));
		AreEqual(("NFO", "54957"), "NFO|54957".ParseInstrumentKey());
		ThrowsExactly<FormatException>(() =>
			"NFO-54957".ParseInstrumentKey());
	}

	private sealed class CaptureHandler : HttpMessageHandler
	{
		private readonly string _response;

		public CaptureHandler(string response)
		{
			_response = response;
		}

		public Uri RequestUri { get; private set; }
		public string Authorization { get; private set; }
		public string Body { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			RequestUri = request.RequestUri;
			Authorization = request.Headers.TryGetValues(
				"Authorization",
				out var values)
				? values.Single()
				: null;
			Body = request.Content == null
				? null
				: await request.Content.ReadAsStringAsync(cancellationToken);
			return new(HttpStatusCode.OK)
			{
				Content = new StringContent(
					_response,
					Encoding.UTF8,
					"application/json"),
			};
		}
	}
}
