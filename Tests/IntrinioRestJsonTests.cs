namespace StockSharp.Connectors.Tests;

using System;
using System.Text.Json;

using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Intrinio.Native;
using StockSharp.Intrinio.Native.Model;

[TestClass]
public class IntrinioRestJsonTests : BaseTestClass
{
	[TestMethod]
	public void DeserializesSnakeCaseAndNormalizesTimestampsToUtc()
	{
		const string json =
			"""{"last_price":12.5,"last_time":"2026-07-29T10:11:12-04:00","ignored":true}""";

		var value = IntrinioRestClient.Deserialize<IntrinioRealtimeStockPrice>(json);

		AreEqual(12.5m, value.LastPrice);
		AreEqual(new DateTime(2026, 7, 29, 14, 11, 12, DateTimeKind.Utc),
			value.LastTime);
	}

	[TestMethod]
	public void BuildsEscapedQueryFromProtocolNames()
	{
		using var client = new IntrinioRestClient(
			new("https://api-v2.intrinio.com/"), "a+ /?");
		var uri = client.BuildUri("securities/AAPL", new IntrinioQuoteRequest
		{
			IsActiveOnly = true,
			Source = "delayed sip",
		});

		AreEqual(
			"https://api-v2.intrinio.com/securities/AAPL?api_key=a%2B%20%2F%3F&active_only=true&source=delayed%20sip",
			uri.AbsoluteUri);
	}

	[TestMethod]
	public void RejectsMalformedJson()
		=> ThrowsExactly<JsonException>(() =>
			IntrinioRestClient.Deserialize<IntrinioRealtimeStockPrice>("{"));
}
