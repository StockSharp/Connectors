namespace StockSharp.Connectors.Tests;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.IIFL;
using StockSharp.IIFL.Native;
using StockSharp.Messages;

[TestClass]
public class IIFLTests : BaseTestClass
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
		var adapter = new IIFLMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://api.iiflcapital.com/v1",
			adapter.RestEndpoint);
		AreEqual("bridge.iiflcapital.com", adapter.BridgeHost);
		AreEqual(8883, adapter.BridgePort);
		AreEqual(
			"https://idaas.iiflsecurities.com/v1/access/check/token",
			adapter.TokenValidationEndpoint);
		IsTrue(adapter.StreamingEnabled);
		AreEqual(TimeSpan.FromSeconds(5),
			adapter.PollingInterval);
		AreEqual(9, IIFLMessageAdapter.AllTimeFrames.Count());
		CollectionAssert.Contains(adapter.AssociatedBoards, "NSE");
		CollectionAssert.Contains(adapter.AssociatedBoards, "MCX");
	}

	[TestMethod]
	public void SettingsRoundTripKeepsCredentialsAndAddresses()
	{
		var source = new IIFLMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "app-key".Secure(),
			Secret = "app-secret".Secure(),
			ClientId = "client-id",
			AuthorizationCode = "auth-code",
			SessionToken = "session".Secure(),
			PortfolioName = "account",
			RestEndpoint = "https://rest.example/v1",
			BridgeHost = "stream.example",
			BridgePort = 9443,
			TokenValidationEndpoint =
				"https://auth.example/check",
			StreamingEnabled = false,
			PollingInterval = TimeSpan.FromSeconds(12),
		};
		var storage = new SettingsStorage();
		source.Save(storage);
		var target = new IIFLMessageAdapter(
			new IncrementalIdGenerator());

		target.Load(storage);

		AreEqual("app-key", target.Key.UnSecure());
		AreEqual("app-secret", target.Secret.UnSecure());
		AreEqual("client-id", target.ClientId);
		AreEqual("auth-code", target.AuthorizationCode);
		AreEqual("session", target.SessionToken.UnSecure());
		AreEqual("account", target.PortfolioName);
		AreEqual("https://rest.example/v1",
			target.RestEndpoint);
		AreEqual("stream.example", target.BridgeHost);
		AreEqual(9443, target.BridgePort);
		AreEqual("https://auth.example/check",
			target.TokenValidationEndpoint);
		IsFalse(target.StreamingEnabled);
		AreEqual(TimeSpan.FromSeconds(12),
			target.PollingInterval);
	}

	[TestMethod]
	public async Task RestAuthenticationUsesChecksumAndBearer()
	{
		var paths = new List<string>();
		var jwt = CreateJwt("IIFL-USER");
		var expected = Convert.ToHexStringLower(
			SHA256.HashData(Encoding.UTF8.GetBytes(
				"client-id" + "auth-code" + "app-secret")));
		var handler = new Handler(async (request, cancellationToken) =>
		{
			paths.Add(request.RequestUri.AbsolutePath);
			if (request.RequestUri.AbsolutePath.EndsWith(
				"/getusersession", StringComparison.Ordinal))
			{
				IsTrue(request.Headers.Authorization is null);
				var body = JObject.Parse(await request.Content
					.ReadAsStringAsync(cancellationToken));
				AreEqual(expected,
					body.Value<string>("checkSum"));
				return Json($$"""{"userSession":"{{jwt}}"}""");
			}
			AreEqual("Bearer",
				request.Headers.Authorization.Scheme);
			AreEqual(jwt,
				request.Headers.Authorization.Parameter);
			return Json(
				"""{"result":{"clientId":"IIFL-USER"}}""");
		});
		using var client = new IIFLRestClient(
			"https://api.example/v1", "client-id", "auth-code",
			"app-secret".Secure(), null, handler);

		await client.AuthenticateAsync(CancellationToken);
		var profile = await client.GetProfileAsync(CancellationToken);

		AreEqual(jwt, client.AccessToken);
		AreEqual("IIFL-USER", client.UserId);
		AreEqual("IIFL-USER",
			profile["result"].Value<string>("clientId"));
		CollectionAssert.AreEqual(
			new[] { "/v1/getusersession", "/v1/profile" },
			paths);
	}

	[TestMethod]
	public void ContractAndOrderPayloadsMapNativeIdentity()
	{
		var instrument = JObject.Parse(
			"""
			{
			  "formattedInstrumentName": "NIFTY 30 JUL 25000 CE",
			  "instrumentType": "OPTIDX",
			  "underlyingInstrumentSymbol": "NIFTY",
			  "lotSize": "75",
			  "instrumentId": "68428",
			  "tickSize": "0.05",
			  "optionType": "CE",
			  "exchange": "NSEFO",
			  "tradingSymbol": "NIFTY30JUL25000CE",
			  "strikePrice": "25000"
			}
			""").ToObject<IIFLInstrument>();
		var security = instrument.ToSecurityId();
		var order = JObject.Parse(
			"""
			{
			  "brokerOrderId": "ORD-1",
			  "exchangeOrderId": "EX-1",
			  "exchange": "NSEFO",
			  "instrumentId": "68428",
			  "tradingSymbol": "NIFTY30JUL25000CE",
			  "transactionType": "SELL",
			  "orderType": "SL",
			  "orderComplexity": "REGULAR",
			  "product": "NORMAL",
			  "price": 101.25,
			  "slTriggerPrice": 100.5,
			  "quantity": 75,
			  "filledQuantity": 25,
			  "orderStatus": "PARTIALLY FILLED",
			  "exchangeUpdateTime": "28-Jul-2026 11:15:00"
			}
			""").ToIIFLOrder();

		AreEqual("NIFTY30JUL25000CE", security.SecurityCode);
		AreEqual("NFO", security.BoardCode);
		AreEqual("NSEFO/68428", security.Native);
		AreEqual(SecurityTypes.Option,
			instrument.ToSecurityType());
		AreEqual(OptionTypes.Call, instrument.OptionType.ToOptionType());
		AreEqual(0.05m, instrument.Tick);
		AreEqual(75m, instrument.Lot);
		AreEqual(Sides.Sell, order.Side);
		AreEqual(50m, order.Balance);
		AreEqual(OrderTypes.Conditional,
			order.Type.ToIIFLOrderType());
		AreEqual(OrderStates.Active,
			order.Status.ToIIFLOrderState());
		AreEqual(new DateTimeOffset(2026, 7, 28, 11, 15, 0,
			TimeSpan.FromMinutes(330)), order.Time);
	}

	[TestMethod]
	public void BinaryMarketFeedPreservesDividedPricesAndDepth()
	{
		var payload = new byte[188];
		WriteInt32(payload, 0, 10_125);
		WriteUInt32(payload, 4, 75);
		WriteUInt32(payload, 8, 12_000);
		WriteInt32(payload, 12, 10_300);
		WriteInt32(payload, 16, 9_900);
		WriteInt32(payload, 20, 10_000);
		WriteInt32(payload, 24, 10_050);
		WriteInt32(payload, 28, 10_110);
		WriteUInt32(payload, 34, 300);
		WriteInt32(payload, 38, 10_120);
		WriteUInt32(payload, 42, 400);
		WriteInt32(payload, 46, 10_130);
		WriteUInt32(payload, 50, 2_000);
		WriteUInt32(payload, 54, 2_500);
		WriteInt32(payload, 58, 100);
		WriteInt32(payload, 62, 1_722_000_000);
		WriteUInt32(payload, 66, 500);
		WriteInt32(payload, 70, 10_120);
		WriteInt16(payload, 74, 3);
		WriteUInt32(payload, 126, 600);
		WriteInt32(payload, 130, 10_130);
		WriteInt16(payload, 134, 4);

		var feed = IIFLExtensions.ParseMarketFeed(payload);
		var interestPayload = new byte[16];
		WriteInt32(interestPayload, 0, 1234);
		WriteInt32(interestPayload, 4, 1500);
		WriteInt32(interestPayload, 8, 1000);
		WriteInt32(interestPayload, 12, 1200);
		var interest = IIFLExtensions.ParseOpenInterest(
			interestPayload);

		AreEqual(101.25m, feed.LastPrice);
		AreEqual(75m, feed.LastVolume);
		AreEqual(101.2m, feed.BestBidPrice);
		AreEqual(101.3m, feed.BestAskPrice);
		AreEqual(500m, feed.Bids.Single().Volume);
		AreEqual(3, feed.Bids.Single().Orders);
		AreEqual(600m, feed.Asks.Single().Volume);
		AreEqual(4, feed.Asks.Single().Orders);
		AreEqual(DateTimeOffset.FromUnixTimeSeconds(1_722_000_000),
			feed.Time);
		AreEqual(1234m, interest.Current);
		AreEqual(1500m, interest.High);
		AreEqual(1000m, interest.Low);
		AreEqual(1200m, interest.Previous);
	}

	[TestMethod]
	public void MqttConnectAndTopicsMatchOfficialBridgeProtocol()
	{
		var jwt = CreateJwt("Trader-42");
		var payload = IIFLMqttClient.CreateConnectPayload(
			"Trader-42", jwt, "client-1");
		var strings = ReadMqttStrings(payload).ToArray();

		AreEqual("MQTT", strings[0]);
		AreEqual("client-1", strings[1]);
		AreEqual("Trader-42", strings[2]);
		AreEqual($"OPENID~~{jwt}~", strings[3]);
		AreEqual(
			"prod/marketfeed/mw/v1/nsefo/68428",
			IIFLMqttClient.BuildTopic(
				IIFLMqttClient.MarketFeedPrefix,
				"NSEFO/68428"));
		AreEqual(
			"prod/updates/order/v1/trader42",
			IIFLMqttClient.BuildTopic(
				IIFLMqttClient.OrderPrefix, "Trader42"));
	}

	private static IEnumerable<string> ReadMqttStrings(byte[] payload)
	{
		var offset = 0;
		yield return ReadString(payload, ref offset);
		offset += 4;
		yield return ReadString(payload, ref offset);
		yield return ReadString(payload, ref offset);
		yield return ReadString(payload, ref offset);
	}

	private static string ReadString(byte[] payload, ref int offset)
	{
		var length = (payload[offset] << 8) | payload[offset + 1];
		offset += 2;
		var value = Encoding.UTF8.GetString(payload, offset, length);
		offset += length;
		return value;
	}

	private static string CreateJwt(string user)
	{
		var header = Base64Url("""{"alg":"none"}""");
		var body = Base64Url(
			$$"""{"preferred_username":"{{user}}"}""");
		return $"{header}.{body}.";
	}

	private static string Base64Url(string value)
		=> Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
			.TrimEnd('=')
			.Replace('+', '-')
			.Replace('/', '_');

	private static void WriteInt16(byte[] target, int offset,
		short value)
		=> BinaryPrimitives.WriteInt16LittleEndian(
			target.AsSpan(offset, 2), value);

	private static void WriteInt32(byte[] target, int offset,
		int value)
		=> BinaryPrimitives.WriteInt32LittleEndian(
			target.AsSpan(offset, 4), value);

	private static void WriteUInt32(byte[] target, int offset,
		uint value)
		=> BinaryPrimitives.WriteUInt32LittleEndian(
			target.AsSpan(offset, 4), value);

	private static HttpResponseMessage Json(string content)
		=> new(HttpStatusCode.OK)
		{
			Content = new StringContent(content, Encoding.UTF8,
				"application/json"),
		};
}
