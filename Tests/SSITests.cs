namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Ecng.Common;
using Ecng.Serialization;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using StockSharp.Messages;
using StockSharp.SSI;
using StockSharp.SSI.Native;

[TestClass]
public class SSITests : BaseTestClass
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
	public void DefaultsUseOfficialFastConnectV3Endpoints()
	{
		var adapter = new SSIMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual("https://api.ssi.com.vn", adapter.RestEndpoint);
		AreEqual("wss://stream.ssi.com.vn/ws/v3",
			adapter.StreamingEndpoint);
		AreEqual(TimeSpan.FromSeconds(5), adapter.PollingInterval);
		AreEqual(7, SSIMessageAdapter.AllTimeFrames.Count());
		CollectionAssert.AreEquivalent(
			new[] { "HOSE", "HNX", "UPCOM" },
			adapter.AssociatedBoards);
	}

	[TestMethod]
	public void SettingsRoundTripKeepsCredentialsAndEndpoints()
	{
		var source = new SSIMessageAdapter(
			new IncrementalIdGenerator())
		{
			Key = "api-key".Secure(),
			Secret = "api-secret".Secure(),
			ClientId = "client-id",
			PrivateKey = "private-key".Secure(),
			Otp = "123456".Secure(),
			Account = "0901351",
			RestEndpoint = "https://rest.example",
			StreamingEndpoint = "wss://stream.example/ws/v3",
			PollingInterval = TimeSpan.FromSeconds(11),
		};
		var storage = new SettingsStorage();
		source.Save(storage);

		var target = new SSIMessageAdapter(
			new IncrementalIdGenerator());
		target.Load(storage);

		AreEqual("api-key", target.Key.UnSecure());
		AreEqual("api-secret", target.Secret.UnSecure());
		AreEqual("client-id", target.ClientId);
		AreEqual("private-key", target.PrivateKey.UnSecure());
		AreEqual("123456", target.Otp.UnSecure());
		AreEqual("0901351", target.Account);
		AreEqual("https://rest.example", target.RestEndpoint);
		AreEqual("wss://stream.example/ws/v3",
			target.StreamingEndpoint);
		AreEqual(TimeSpan.FromSeconds(11), target.PollingInterval);
	}

	[TestMethod]
	public void SignerProducesOfficialHexPkcs1Sha256Signature()
	{
		using var rsa = RSA.Create(2048);
		var privateKey = ToSSIPrivateKey(rsa.ExportParameters(true));
		const string body =
			"""{"accountNo":"0901351","symbol":"SSI"}""";

		var signature = Convert.FromHexString(
			new SSISigner(privateKey.Secure()).Sign(body));

		IsTrue(rsa.VerifyData(Encoding.UTF8.GetBytes(body), signature,
			HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
	}

	[TestMethod]
	public async Task RestClientUsesOfficialPathsBearerAndSignature()
	{
		using var rsa = RSA.Create(2048);
		var privateKey = ToSSIPrivateKey(rsa.ExportParameters(true));
		var paths = new List<string>();
		var handler = new Handler(async (request, cancellationToken) =>
		{
			paths.Add(request.RequestUri.PathAndQuery);
			if (request.RequestUri.AbsolutePath.EndsWith("/auth/token",
				StringComparison.Ordinal))
			{
				var body = JObject.Parse(await request.Content
					.ReadAsStringAsync(cancellationToken));
				AreEqual("api-key", body.Value<string>("apiKey"));
				AreEqual("api-secret",
					body.Value<string>("apiSecret"));
				AreEqual("123456", body.Value<string>("otp"));
				return Json(
					"""{"data":{"tokenType":"Bearer","accessToken":"access","refreshToken":"refresh","expiresAt":4102444800,"refreshExpiresAt":4102444800}}""");
			}

			AreEqual("Bearer",
				request.Headers.Authorization.Scheme);
			AreEqual("access",
				request.Headers.Authorization.Parameter);
			if (request.RequestUri.AbsolutePath.EndsWith(
				"/trading/order", StringComparison.Ordinal))
			{
				var body = await request.Content.ReadAsStringAsync(
					cancellationToken);
				var signature = Convert.FromHexString(
					request.Headers.GetValues("X-Signature").Single());
				IsTrue(rsa.VerifyData(Encoding.UTF8.GetBytes(body),
					signature, HashAlgorithmName.SHA256,
					RSASignaturePadding.Pkcs1));
				return Json(
					"""{"orderId":"ORD-1","clientRequestId":"42","orderStatus":"PD"}""");
			}
			if (request.RequestUri.AbsolutePath.EndsWith(
				"/data/securitiesByBoard", StringComparison.Ordinal))
				return Json(
					"""{"data":[{"symbol":"SSI","board":"HOSE","symbolNameEn":"SSI Securities","lotSize":100}]}""");
			if (request.RequestUri.AbsolutePath.EndsWith(
				"/data/ohlc", StringComparison.Ordinal))
				return Json(
					"""{"data":[{"symbol":"SSI","tradingDate":"2026/07/28 09:00:00","open":25.1,"high":25.5,"low":25.0,"close":25.4,"volume":1000,"value":25400}]}""");
			return new(HttpStatusCode.NotFound);
		});
		using var client = new SSIRestClient(
			"https://api.example", "client-id", "api-key".Secure(),
			"api-secret".Secure(), privateKey.Secure(),
			"123456".Secure(), handler);

		await client.AuthenticateAsync(CancellationToken);
		var securities = await client.GetSecuritiesAsync(
			"SSI", null, CancellationToken);
		var candles = await client.GetCandlesAsync(
			"SSI", TimeSpan.FromMinutes(1),
			new DateTime(2026, 7, 28), new DateTime(2026, 7, 29),
			1, 100, CancellationToken);
		var order = await client.PlaceOrderAsync(new JObject
		{
			["accountNo"] = "0901351",
			["symbol"] = "SSI",
			["side"] = "B",
			["quantity"] = 100,
			["price"] = "25.4",
			["orderType"] = "LO",
			["clientRequestId"] = "42",
			["deviceId"] = "StockSharp",
			["userAgent"] = "StockSharp.SSI/1.0",
		}, CancellationToken);

		AreEqual("SSI", securities.Single()
			.Value<string>("symbol"));
		AreEqual(25.4m, candles.Single().Close);
		AreEqual(new DateTimeOffset(2026, 7, 28, 9, 0, 0,
			TimeSpan.FromHours(7)), candles.Single().Time);
		AreEqual("ORD-1", order.Value<string>("orderId"));
		IsTrue(paths.Contains("/api/v3/auth/token"));
		IsTrue(paths.Any(path => path.StartsWith(
			"/api/v3/data/securitiesByBoard?",
			StringComparison.Ordinal)));
		IsTrue(paths.Any(path => path.Contains(
			"timeFrame=1m", StringComparison.Ordinal)));
		IsTrue(paths.Contains("/api/v3/trading/order"));
	}

	[TestMethod]
	public async Task UnauthorizedRequestRefreshesWithoutReusingOtp()
	{
		var loginCount = 0;
		var refreshCount = 0;
		var dataCount = 0;
		var handler = new Handler(async (request, cancellationToken) =>
		{
			if (request.RequestUri.AbsolutePath.EndsWith("/auth/token",
				StringComparison.Ordinal))
			{
				loginCount++;
				return Json(
					"""{"accessToken":"access-1","refreshToken":"refresh","expiresAt":4102444800}""");
			}
			if (request.RequestUri.AbsolutePath.EndsWith(
				"/auth/refresh", StringComparison.Ordinal))
			{
				refreshCount++;
				IsTrue(request.Headers.Authorization is null);
				var body = JObject.Parse(await request.Content
					.ReadAsStringAsync(cancellationToken));
				AreEqual("refresh",
					body.Value<string>("refreshToken"));
				return Json(
					"""{"accessToken":"access-2","refreshToken":"refresh","expiresAt":4102444800}""");
			}
			dataCount++;
			if (dataCount == 1)
				return new(HttpStatusCode.Unauthorized)
				{
					Content = new StringContent(
						"""{"msg":"expired"}"""),
				};
			AreEqual("access-2",
				request.Headers.Authorization.Parameter);
			return Json(
				"""{"data":[{"symbol":"SSI","board":"HOSE"}]}""");
		});
		using var client = new SSIRestClient(
			"https://api.example", "client-id", "api-key".Secure(),
			"api-secret".Secure(), null, "123456".Secure(), handler);

		var securities = await client.GetSecuritiesAsync(
			"SSI", null, CancellationToken);

		AreEqual(1, loginCount);
		AreEqual(1, refreshCount);
		AreEqual(2, dataCount);
		AreEqual("SSI",
			securities.Single().Value<string>("symbol"));
	}

	[TestMethod]
	public void StreamingRequestsUseOfficialMethodChannelAndTopics()
	{
		var request = SSIWebSocketClient.CreateRequest("subscribe",
			"DATA", ["trade.SSI", "quote.SSI"]);

		AreEqual("subscribe", request.Value<string>("method"));
		AreEqual("DATA", request.Value<string>("channel"));
		CollectionAssert.AreEqual(
			new[] { "trade.SSI", "quote.SSI" },
			request["topics"].Values<string>().ToArray());
	}

	[TestMethod]
	public void StreamingPayloadsMapPricesDepthCandlesAndOrders()
	{
		var trade = JObject.Parse(
			"""{"t":"2026-07-28T09:31:02+07:00","s":"SSI","p":25.4,"q":100,"si":"B","v":12500}""")
			.ToSSITrade();
		var depth = JObject.Parse(
			"""{"t":"2026-07-28T09:31:03+07:00","s":"SSI","bids":[[25.3,500],[25.2,700]],"asks":[[25.4,300]]}""")
			.ToSSIDepth();
		var candle = JObject.Parse(
			"""{"st":"2026-07-28T09:31:00+07:00","t":"2026-07-28T09:31:59+07:00","s":"SSI","o":25.2,"h":25.5,"l":25.1,"c":25.4,"v":1000}""")
			.ToSSICandle();
		var order = JObject.Parse(
			"""{"accountNo":"0901351","orderId":"ORD-1","symbol":"SSI","side":"B","orderType":"LO","price":25.4,"quantity":100,"osQty":40,"filledQty":60,"cancelQty":0,"orderStatus":"PF","inputTime":"2026-07-28T09:30:00+07:00"}""")
			.ToSSIOrder();

		AreEqual("SSI", trade.Symbol);
		AreEqual(25.4m, trade.Price);
		AreEqual(Sides.Buy, trade.Side);
		AreEqual(12_500m, trade.TotalVolume);
		AreEqual(new DateTimeOffset(2026, 7, 28, 9, 31, 2,
			TimeSpan.FromHours(7)), trade.Time);
		AreEqual(2, depth.Bids.Length);
		AreEqual(25.3m, depth.Bids[0].Price);
		AreEqual(500m, depth.Bids[0].Volume);
		AreEqual(25.4m, depth.Asks.Single().Price);
		AreEqual(25.2m, candle.Open);
		AreEqual(25.4m, candle.Close);
		AreEqual(1_000m, candle.Volume);
		AreEqual(60m, order.FilledVolume);
		AreEqual(40m, order.Balance);
		AreEqual(OrderStates.Active, order.Status.ToSSIOrderState());
	}

	[TestMethod]
	public void AllOfficialTerminalStatusesAreMapped()
	{
		foreach (var status in new[] { "FF", "FFPC", "CL", "EX" })
			AreEqual(OrderStates.Done, status.ToSSIOrderState());
		foreach (var status in new[] { "RJ", "REJECTED" })
			AreEqual(OrderStates.Failed, status.ToSSIOrderState());
		foreach (var status in new[]
			{
				"PD", "WA", "RS", "SD", "QU", "PF", "WM", "WC",
				"IAV",
			})
			AreEqual(OrderStates.Active, status.ToSSIOrderState());
	}

	private static string ToSSIPrivateKey(RSAParameters value)
	{
		var xml = new XElement("RSAKeyValue",
			Element("Modulus", value.Modulus),
			Element("Exponent", value.Exponent),
			Element("P", value.P),
			Element("Q", value.Q),
			Element("DP", value.DP),
			Element("DQ", value.DQ),
			Element("InverseQ", value.InverseQ),
			Element("D", value.D));
		return Convert.ToBase64String(Encoding.UTF8.GetBytes(
			xml.ToString(SaveOptions.DisableFormatting)));
	}

	private static XElement Element(string name, byte[] value)
		=> new(name, Convert.ToBase64String(value));

	private static HttpResponseMessage Json(string content)
		=> new(HttpStatusCode.OK)
		{
			Content = new StringContent(content, Encoding.UTF8,
				"application/json"),
		};
}
