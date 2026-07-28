namespace StockSharp.Connectors.Tests;

using System;
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

using StockSharp.Finage;
using StockSharp.Finage.Native;
using StockSharp.Messages;

[TestClass]
public class FinageTests : BaseTestClass
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
	public void DefaultsAndSettingsRoundTrip()
	{
		var source = new FinageMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(new Uri("https://api.finage.co.uk/"),
			source.RestEndpoint);
		AreEqual(new Uri("wss://socket.finage.ws:8080/"),
			source.StreamingEndpoint);
		AreEqual(TimeSpan.FromMilliseconds(500),
			source.RequestInterval);
		AreEqual(10000, source.MaximumSecurities);
		IsTrue(FinageMessageAdapter.AllTimeFrames
			.Contains(TimeSpan.FromMinutes(15)));

		source.ApiKey = "rest".Secure();
		source.StreamingToken = "stream".Secure();
		source.RestEndpoint = new("https://rest.example/v1/");
		source.StreamingEndpoint =
			new("wss://stream.example/feed");
		source.RequestInterval = TimeSpan.FromSeconds(1);
		source.Symbols = "EURUSD,GBPUSD";
		source.MaximumSecurities = 500;
		var storage = new SettingsStorage();
		source.Save(storage);
		var target = new FinageMessageAdapter(
			new IncrementalIdGenerator());

		target.Load(storage);

		AreEqual("rest", target.ApiKey.UnSecure());
		AreEqual("stream", target.StreamingToken.UnSecure());
		AreEqual(source.RestEndpoint, target.RestEndpoint);
		AreEqual(source.StreamingEndpoint,
			target.StreamingEndpoint);
		AreEqual(source.RequestInterval, target.RequestInterval);
		AreEqual("EURUSD,GBPUSD", target.Symbols);
		AreEqual(500, target.MaximumSecurities);
	}

	[TestMethod]
	public async Task SymbolDirectoryUsesConfiguredHostAndApiKey()
	{
		Uri captured = null;
		var handler = new Handler((request, _) =>
		{
			captured = request.RequestUri;
			return Task.FromResult(Json(
				"""
				{
				  "page":1,
				  "symbols":[
				    {"symbol":"EURUSD","name":"Euro - US dollar"},
				    {"symbol":"GBPUSD","name":"Pound - US dollar"}
				  ]
				}
				"""));
		});
		using var client = new FinageRestClient(
			new("https://rest.example/api/"),
			"secret".Secure(), TimeSpan.Zero, handler);

		var instruments = await client.GetSymbolsAsync(
			"EUR", 10, CancellationToken);

		AreEqual(
			"https://rest.example/api/symbol-list/forex" +
				"?page=1&search=EUR&apikey=secret",
			captured.AbsoluteUri);
		AreEqual(2, instruments.Length);
		AreEqual("EURUSD", instruments[0].Symbol);
		AreEqual("Euro - US dollar", instruments[0].Name);
	}

	[TestMethod]
	public async Task LastQuoteUsesOfficialEndpointAndFields()
	{
		Uri captured = null;
		var handler = new Handler((request, _) =>
		{
			captured = request.RequestUri;
			return Task.FromResult(Json(
				"""
				{"symbol":"GBPUSD","ask":1.36305,
				"bid":1.36292,"timestamp":1609875979000}
				"""));
		});
		using var client = new FinageRestClient(
			new("https://rest.example/"),
			"secret".Secure(), TimeSpan.Zero, handler);

		var quote = await client.GetQuoteAsync("GBP/USD",
			CancellationToken);

		AreEqual(
			"https://rest.example/last/forex/GBPUSD?apikey=secret",
			captured.AbsoluteUri);
		AreEqual("GBPUSD", quote.Symbol);
		AreEqual(1.36292m, quote.Bid);
		AreEqual(1.36305m, quote.Ask);
		AreEqual(
			DateTimeOffset.FromUnixTimeMilliseconds(1609875979000)
				.UtcDateTime,
			quote.Time);
	}

	[TestMethod]
	public async Task AggregatesUseOfficialPathAndParseBars()
	{
		Uri captured = null;
		var handler = new Handler((request, _) =>
		{
			captured = request.RequestUri;
			return Task.FromResult(Json(
				"""
				{
				  "symbol":"GBPUSD",
				  "totalResults":1,
				  "results":[
				    {"v":254,"o":1.3642,"c":1.3667,
				    "h":1.3677,"l":1.3642,"t":1609477200000}
				  ]
				}
				"""));
		});
		using var client = new FinageRestClient(
			new("https://rest.example/"),
			"secret".Secure(), TimeSpan.Zero, handler);

		var bars = await client.GetBarsAsync("GBP/USD",
			new DateTime(2021, 1, 1),
			new DateTime(2021, 1, 5),
			TimeSpan.FromHours(4), CancellationToken);

		IsTrue(captured.AbsolutePath.EndsWith(
			"/agg/forex/GBPUSD/4/hour/2021-01-01/2021-01-05",
			StringComparison.Ordinal));
		IsTrue(captured.Query.Contains("limit=50000",
			StringComparison.Ordinal));
		IsTrue(captured.Query.Contains("date_format=ts",
			StringComparison.Ordinal));
		AreEqual(1, bars.Length);
		AreEqual(1.3667m, bars[0].Close);
		AreEqual(254m, bars[0].Volume);
	}

	[TestMethod]
	public void StreamingPayloadAndQuoteMatchDocumentedFields()
	{
		var subscribe = JObject.Parse(
			FinageExtensions.BuildSubscription(
				["EUR/USD", "GBPUSD"], true));
		var unsubscribe = JObject.Parse(
			FinageExtensions.BuildSubscription(
				["EUR/USD"], false));
		var quote = FinageExtensions.ParseStreamQuote(
			"""
			{"s":"EUR/USD","a":1.1145,"b":1.11439,
			"dc":"-0.4262","dd":"-0.0047",
			"ppms":false,"t":1747410743394}
			""");

		AreEqual("subscribe",
			subscribe.Value<string>("action"));
		AreEqual("EURUSD,GBPUSD",
			subscribe.Value<string>("symbols"));
		AreEqual("unsubscribe",
			unsubscribe.Value<string>("action"));
		AreEqual("EURUSD", quote.Symbol);
		AreEqual(1.11439m, quote.Bid);
		AreEqual(1.1145m, quote.Ask);
		AreEqual(
			DateTimeOffset.FromUnixTimeMilliseconds(1747410743394)
				.UtcDateTime,
			quote.Time);
	}

	[TestMethod]
	public void SymbolsIntervalsAndStreamingUriNormalize()
	{
		var instrument = "EUR/USD".ToInstrument(
			"Euro - US dollar");
		var interval = TimeSpan.FromDays(7)
			.ToFinageInterval();
		var endpoint = new Uri(
			"wss://stream.example/feed?format=json")
			.BuildStreamingUri("socket key");

		AreEqual("EURUSD", instrument.Symbol);
		AreEqual("EUR", instrument.BaseCurrency);
		AreEqual("USD", instrument.QuoteCurrency);
		AreEqual(BoardCodes.Finage,
			instrument.ToSecurityId().BoardCode);
		AreEqual(1, interval.Multiplier);
		AreEqual("week", interval.Unit);
		AreEqual(
			"wss://stream.example/feed?format=json&token=socket%20key",
			endpoint.AbsoluteUri);
	}

	private static HttpResponseMessage Json(string content)
		=> new(HttpStatusCode.OK)
		{
			Content = new StringContent(content, Encoding.UTF8,
				"application/json"),
		};
}
