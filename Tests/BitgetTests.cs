namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using RestSharp;

using StockSharp.Bitget;
using StockSharp.Bitget.Native;
using FuturesExtensions = StockSharp.Bitget.Native.Futures.Extensions;
using SpotExtensions = StockSharp.Bitget.Native.Spot.Extensions;
using StockSharp.Messages;

/// <summary>
/// Tests for the Bitget connector.
/// </summary>
[TestClass]
public class BitgetTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow(20000, CandleStates.Active)]
	[DataRow(59999, CandleStates.Active)]
	[DataRow(60000, CandleStates.Finished)]
	[DataRow(120000, CandleStates.Finished)]
	public void SpotAndFuturesRestCandlesKeepTheirFormingState(int elapsedMilliseconds, CandleStates expected)
	{
		var open = new DateTime(2024, 1, 2, 12, 0, 0, DateTimeKind.Utc);
		var payload = JsonConvert.SerializeObject(new object[] { (long)open.ToUnix(false), "100", "110", "90", "105", "12.5", "1300" });
		var spot = JsonConvert.DeserializeObject<StockSharp.Bitget.Native.Spot.Model.RestCandle>(payload);
		var futures = JsonConvert.DeserializeObject<StockSharp.Bitget.Native.Futures.Model.RestCandle>(payload);
		var subscription = new MarketDataMessage { TransactionId = 42, DataType2 = TimeSpan.FromMinutes(1).TimeFrame() };
		var snapshotTime = open.AddMilliseconds(elapsedMilliseconds);

		foreach (var message in new[] { SpotExtensions.ToCandleMessage(spot, subscription, snapshotTime), FuturesExtensions.ToCandleMessage(futures, subscription, snapshotTime) })
		{
			AreEqual(expected, message.State);
			AreEqual(open, message.OpenTime);
			AreEqual(105m, message.ClosePrice);
			AreEqual(12.5m, message.TotalVolume);
		}
	}

	[TestMethod]
	public void RestMonthlyCandleKeepsTheVenueUtcPlusEightBoundary()
	{
		var open = new DateTime(2024, 2, 29, 16, 0, 0, DateTimeKind.Utc);
		var close = new DateTime(2024, 3, 31, 16, 0, 0, DateTimeKind.Utc);
		var subscription = new MarketDataMessage { DataType2 = TimeSpan.FromDays(30).TimeFrame() };
		var spot = new StockSharp.Bitget.Native.Spot.Model.RestCandle { Timestamp = (long)open.ToUnix(false) };
		var futures = new StockSharp.Bitget.Native.Futures.Model.RestCandle { Timestamp = (long)open.ToUnix(false) };

		AreEqual(CandleStates.Active, SpotExtensions.ToCandleMessage(spot, subscription, close.AddMilliseconds(-1)).State);
		AreEqual(CandleStates.Finished, SpotExtensions.ToCandleMessage(spot, subscription, close).State);
		AreEqual(CandleStates.Active, FuturesExtensions.ToCandleMessage(futures, subscription, close.AddMilliseconds(-1)).State);
		AreEqual(CandleStates.Finished, FuturesExtensions.ToCandleMessage(futures, subscription, close).State);
	}

	[TestMethod]
	public void SpotPostSignatureCoversSerializedBodyAndAddsDemoHeader()
	{
		const string key = "test-key";
		const string secret = "test-secret";
		const string passphrase = "test-passphrase";
		var url = new Uri("https://api.bitget.com/api/v2/spot/trade/place-order");
		var body = new { symbol = "BTCUSDT", side = "buy", price = "100" };
		using var authenticator = new Authenticator(true, key.Secure(), secret.Secure(), passphrase.Secure(), true);
		var request = new RestRequest((string)null, Method.Post);

		SpotExtensions.ApplySecret(request, url, authenticator, body);

		var serializedBody = Parameter(request, ParameterType.RequestBody, null);
		var timestamp = Parameter(request, ParameterType.HttpHeader, "ACCESS-TIMESTAMP");
		var expected = HmacSha256Base64(secret, timestamp + "POST" + url.AbsolutePath + serializedBody);
		AreEqual(expected, Parameter(request, ParameterType.HttpHeader, "ACCESS-SIGN"));
		AreEqual("1", Parameter(request, ParameterType.HttpHeader, "paptrading"));
	}

	[TestMethod]
	public void FuturesGetSignatureUsesAmpersandSeparatedEscapedQuery()
	{
		const string secret = "test-secret";
		var url = new Uri("https://api.bitget.com/api/v2/mix/order/orders-pending");
		using var authenticator = new Authenticator(true, "test-key".Secure(), secret.Secure(), "pass".Secure(), true);
		var request = new RestRequest((string)null, Method.Get)
			.AddQueryParameter("productType", "USDT FUTURES")
			.AddQueryParameter("symbol", "BTC/USDT");

		FuturesExtensions.ApplySecret(request, url, authenticator);

		var timestamp = Parameter(request, ParameterType.HttpHeader, "ACCESS-TIMESTAMP");
		const string query = "productType=USDT%20FUTURES&symbol=BTC%2FUSDT";
		var expected = HmacSha256Base64(secret, timestamp + "GET" + url.AbsolutePath + "?" + query);
		AreEqual(expected, Parameter(request, ParameterType.HttpHeader, "ACCESS-SIGN"));
	}

	private static string Parameter(RestRequest request, ParameterType type, string name)
		=> request.Parameters.Single(p => p.Type == type && (name is null || p.Name == name)).Value?.ToString();

	// The signature Bitget expects, worked out here independently of the code under test.
	private static string HmacSha256Base64(string secret, string payload)
		=> Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitgetMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [BitgetSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.Bitget,
	};
}
