namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Net.Http;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using StockSharp.FinamTrade;
using StockSharp.FinamTrade.Native;
using StockSharp.FinamTrade.Native.Model;
using StockSharp.Messages;

[TestClass]
public class FinamTradeTests : BaseTestClass
{
	[TestMethod]
	public void SettingsRoundTripKeepsConnectionOptions()
	{
		var source = new FinamTradeMessageAdapter(new IncrementalIdGenerator())
		{
			Token = "secret-token".Secure(),
			AccountId = "ACC-42",
			AppId = "StockSharp-Tests",
			PollingInterval = TimeSpan.FromSeconds(17),
			LookupLimit = 1234,
			RestAddress = "https://api.example.test/",
			WebSocketAddress = "wss://stream.example.test/ws",
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new FinamTradeMessageAdapter(new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("secret-token", target.Token.UnSecure());
		AreEqual(source.AccountId, target.AccountId);
		AreEqual(source.AppId, target.AppId);
		AreEqual(source.PollingInterval, target.PollingInterval);
		AreEqual(source.LookupLimit, target.LookupLimit);
		AreEqual(source.RestAddress, target.RestAddress);
		AreEqual(source.WebSocketAddress, target.WebSocketAddress);
	}

	[TestMethod]
	public void OrderRequestUsesOfficialSnakeCaseAndDecimalShape()
	{
		var json = FinamRestClient.SerializeBody(new FinamOrderRequest
		{
			Symbol = "SBER@MISX",
			Quantity = 10m.ToNativeDecimal(),
			Side = "SIDE_BUY",
			Type = "ORDER_TYPE_LIMIT",
			TimeInForce = "TIME_IN_FORCE_DAY",
			LimitPrice = 305.25m.ToNativeDecimal(),
			ClientOrderId = "42",
		});

		IsTrue(json.Contains("\"client_order_id\":\"42\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"time_in_force\":\"TIME_IN_FORCE_DAY\"",
			StringComparison.Ordinal));
		IsTrue(json.Contains("\"limit_price\":{\"value\":\"305.25\"}",
			StringComparison.Ordinal));
		IsFalse(json.Contains("\"ClientOrderId\":",
			StringComparison.Ordinal));
	}

	[TestMethod]
	public void RestAuthorizationUsesBearerSessionToken()
	{
		using var request = new HttpRequestMessage();
		FinamRestClient.SetAuthorization(request, "jwt-token");

		AreEqual("Bearer jwt-token",
			request.Headers.Authorization.ToString());
	}

	[TestMethod]
	public void DecimalWireFormatSupportsRestAndWebSocketShapes()
	{
		var rest = JsonConvert.DeserializeObject<FinamDecimal>(
			"""{"value":"305.25"}""");
		var stream = JsonConvert.DeserializeObject<FinamDecimal>(
			""""305.25"""");

		AreEqual(305.25m, rest.ToDecimal());
		AreEqual(305.25m, stream.ToDecimal());
		AreEqual("""{"value":"305.25"}""",
			JsonConvert.SerializeObject(stream));
	}

	[TestMethod]
	public void WebSocketQuoteParsesStringDecimals()
	{
		var quote = FinamSocketClient.DeserializeQuotes(
			"""
			{
			  "type":"DATA",
			  "subscription_type":"QUOTES",
			  "timestamp":1770000000,
			  "payload":{
			    "quote":[{
			      "symbol":"SBER@MISX",
			      "timestamp":"2026-02-02T10:00:00Z",
			      "ask":"305.30",
			      "ask_size":"120",
			      "bid":"305.25",
			      "bid_size":"80",
			      "last":"305.28"
			    }]
			  }
			}
			""").Single();

		AreEqual("SBER@MISX", quote.Symbol);
		AreEqual(305.25m, quote.Bid.ToDecimal());
		AreEqual(80m, quote.BidSize.ToDecimal());
		AreEqual(305.30m, quote.Ask.ToDecimal());
		AreEqual(120m, quote.AskSize.ToDecimal());
	}

	[TestMethod]
	public void SymbolsUseTickerAtMicFormat()
	{
		var securityId = new SecurityId
		{
			SecurityCode = "SBER",
			BoardCode = "MISX",
		};

		AreEqual("SBER@MISX", securityId.ToNativeSymbol());
		AreEqual(securityId, "SBER@MISX".ToSecurityId());
	}

	[TestMethod]
	public void CandleTimeFramesUseDocumentedCodes()
	{
		AreEqual("TIME_FRAME_M1", TimeSpan.FromMinutes(1).ToNative());
		AreEqual("TIME_FRAME_H4", TimeSpan.FromHours(4).ToNative());
		AreEqual("TIME_FRAME_D", TimeSpan.FromDays(1).ToNative());
		AreEqual("TIME_FRAME_QR", TimeSpan.FromDays(90).ToNative());
	}

	[TestMethod]
	public void NativeOrderStatesMapToStockSharpStates()
	{
		AreEqual(OrderStates.Active,
			"ORDER_STATUS_PARTIALLY_FILLED".ToOrderState());
		AreEqual(OrderStates.Pending,
			"ORDER_STATUS_PENDING_NEW".ToOrderState());
		AreEqual(OrderStates.Done,
			"ORDER_STATUS_FILLED".ToOrderState());
		AreEqual(OrderStates.Failed,
			"ORDER_STATUS_REJECTED_BY_EXCHANGE".ToOrderState());
	}

	[TestMethod]
	public void NativeAssetTypesMapToStockSharpTypes()
	{
		AreEqual(SecurityTypes.Stock, "stock".ToSecurityType());
		AreEqual(SecurityTypes.Bond, "bond".ToSecurityType());
		AreEqual(SecurityTypes.Etf, "ETF".ToSecurityType());
		AreEqual(SecurityTypes.Future, "futures".ToSecurityType());
		AreEqual(SecurityTypes.Option, "option".ToSecurityType());
		AreEqual(SecurityTypes.Currency, "currency".ToSecurityType());
	}
}
