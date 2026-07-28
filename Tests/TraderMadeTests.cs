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

using StockSharp.Messages;
using StockSharp.TraderMade;
using StockSharp.TraderMade.Native;

[TestClass]
public class TraderMadeTests : BaseTestClass
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
		var source = new TraderMadeMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(
			new Uri("https://marketdata.tradermade.com/api/v1/"),
			source.RestEndpoint);
		AreEqual(
			new Uri("wss://stream.tradermade.com/feedAdv"),
			source.StreamingEndpoint);
		AreEqual(TimeSpan.FromMilliseconds(500),
			source.RequestInterval);
		AreEqual("USD,EUR,GBP,JPY,BTC",
			source.QuoteCurrencies);
		AreEqual(10000, source.MaximumSecurities);
		IsTrue(TraderMadeMessageAdapter.AllTimeFrames
			.Contains(TimeSpan.FromMinutes(15)));

		source.RestKey = "rest".Secure();
		source.StreamingKey = "stream".Secure();
		source.RestEndpoint = new("https://rest.example/v1/");
		source.StreamingEndpoint = new("wss://stream.example/v2");
		source.RequestInterval = TimeSpan.FromSeconds(1);
		source.EnableLadder = true;
		source.Weekend = true;
		source.QuoteCurrencies = "USD,EUR";
		source.Symbols = "EURUSD,GBPUSD";
		source.MaximumSecurities = 500;
		var storage = new SettingsStorage();
		source.Save(storage);
		var target = new TraderMadeMessageAdapter(
			new IncrementalIdGenerator());

		target.Load(storage);

		AreEqual("rest", target.RestKey.UnSecure());
		AreEqual("stream", target.StreamingKey.UnSecure());
		AreEqual(source.RestEndpoint, target.RestEndpoint);
		AreEqual(source.StreamingEndpoint,
			target.StreamingEndpoint);
		AreEqual(source.RequestInterval, target.RequestInterval);
		IsTrue(target.EnableLadder);
		IsTrue(target.Weekend);
		AreEqual("USD,EUR", target.QuoteCurrencies);
		AreEqual("EURUSD,GBPUSD", target.Symbols);
		AreEqual(500, target.MaximumSecurities);
	}

	[TestMethod]
	public async Task RestClientUsesConfiguredHostAndApiKey()
	{
		Uri captured = null;
		var handler = new Handler((request, _) =>
		{
			captured = request.RequestUri;
			return Task.FromResult(Json(
				"""{"available_currencies":{"EUR":"Euro","USD":"US Dollar"}}"""));
		});
		using var client = new TraderMadeRestClient(
			new("https://rest.example/api/v1/"),
			"secret".Secure(), TimeSpan.Zero, handler);

		var result = await client.GetCurrenciesAsync(
			CancellationToken);

		AreEqual(
			"https://rest.example/api/v1/live_currencies_list" +
				"?api_key=secret",
			captured.AbsoluteUri);
		AreEqual("Euro", result["EUR"]);
		AreEqual("US Dollar", result["USD"]);
	}

	[TestMethod]
	public async Task TimeseriesUsesOfficialIntervalAndParsesBars()
	{
		Uri captured = null;
		var handler = new Handler((request, _) =>
		{
			captured = request.RequestUri;
			return Task.FromResult(Json(
				"""
				{
				  "base_currency":"EUR",
				  "quote_currency":"USD",
				  "quotes":[
				    {
				      "date":"2026-05-15-12:30",
				      "open":1.1620,
				      "high":1.1630,
				      "low":1.1610,
				      "close":1.1627
				    }
				  ]
				}
				"""));
		});
		using var client = new TraderMadeRestClient(
			new("https://rest.example/api/v1/"),
			"secret".Secure(), TimeSpan.Zero, handler);

		var bars = await client.GetBarsAsync("EUR/USD",
			new DateTime(2026, 5, 15, 12, 0, 0),
			new DateTime(2026, 5, 15, 13, 0, 0),
			TimeSpan.FromMinutes(15), false,
			CancellationToken);

		IsTrue(captured.Query.Contains("interval=minute",
			StringComparison.Ordinal));
		IsTrue(captured.Query.Contains("period=15",
			StringComparison.Ordinal));
		IsTrue(captured.Query.Contains("currency=EURUSD",
			StringComparison.Ordinal));
		AreEqual(1, bars.Length);
		AreEqual(new DateTime(2026, 5, 15, 12, 30, 0,
			DateTimeKind.Utc), bars[0].Time);
		AreEqual(1.1627m, bars[0].Close);
	}

	[TestMethod]
	public void LiveAndStreamingQuotesUseDocumentedFields()
	{
		var live = JObject.Parse(
			"""
			{
			  "quotes":[
			    {
			      "ask":1.18183,
			      "base_currency":"EUR",
			      "bid":1.18181,
			      "mid":1.18182,
			      "quote_currency":"USD"
			    }
			  ],
			  "timestamp":1605267771
			}
			""").ToLiveQuotes().Single();
		var stream = TraderMadeExtensions.ParseStreamQuote(
			"""
			{"a":"1.162720000","av":"100000","b":"1.162700000",
			"bv":"100000","s":"EURUSD","t":"QUOTE",
			"ts":"20260515-12:36:35.588"}
			""");

		AreEqual("EURUSD", live.Symbol);
		AreEqual(1.18181m, live.Bid);
		AreEqual(1.18183m, live.Ask);
		AreEqual("EURUSD", stream.Symbol);
		AreEqual(100000m, stream.BidVolume);
		AreEqual(100000m, stream.AskVolume);
		AreEqual(new DateTime(2026, 5, 15, 12, 36, 35,
			588, DateTimeKind.Utc), stream.Time);
	}

	[TestMethod]
	public void WebSocketPayloadsAndLadderMatchV2()
	{
		var login = JObject.Parse(
			TraderMadeExtensions.BuildLogin("secret", true));
		var subscribe = JObject.Parse(
			TraderMadeExtensions.BuildSubscription(
				["EUR/USD"], true));
		var quote = TraderMadeExtensions.ParseStreamQuote(
			"""
			{
			  "a":"1.16189","av":"100000",
			  "b":"1.16185","bv":"100000",
			  "ladder":{
			    "a":[["1.1619000","2600000"],["1.1619200","250000"]],
			    "b":[["1.1618400","2681000"],["1.1618200","2250000"]]
			  },
			  "m":"1.16187","s":"EURUSD","t":"QUOTE",
			  "ts":"20260522-17:30:12.842"
			}
			""");

		AreEqual("login", login.Value<string>("action"));
		AreEqual("JSON", login.Value<string>("fmt"));
		IsTrue(login.Value<bool>("send_ladder"));
		AreEqual("subscribe",
			subscribe.Value<string>("action"));
		AreEqual("EURUSD:QUOTE",
			subscribe["symbols"][0].Value<string>());
		IsTrue(subscribe.Value<bool>("send_last"));
		AreEqual(2, quote.Bids.Length);
		AreEqual(2, quote.Asks.Length);
		AreEqual(2681000m, quote.Bids[0].Volume);
	}

	[TestMethod]
	public void InstrumentsNormalizeAndClassify()
	{
		var forex = "EUR/USD:QUOTE".ToInstrument();
		var crypto = "BTC-USD".ToInstrument();
		var cfd = "UKOILUSD".ToInstrument();

		AreEqual("EURUSD", forex.Symbol);
		AreEqual("EUR", forex.BaseCurrency);
		AreEqual("USD", forex.QuoteCurrency);
		AreEqual(SecurityTypes.Currency, forex.SecurityType);
		AreEqual(SecurityTypes.CryptoCurrency,
			crypto.SecurityType);
		AreEqual(SecurityTypes.Cfd, cfd.SecurityType);
		AreEqual(BoardCodes.TraderMade,
			forex.ToSecurityId().BoardCode);
		var interval = TimeSpan.FromHours(4)
			.ToTraderMadeInterval();
		AreEqual("hourly", interval.Interval);
		AreEqual(4, interval.Period);
		AreEqual(TimeSpan.FromDays(31), interval.MaxRange);
	}

	private static HttpResponseMessage Json(string content)
		=> new(HttpStatusCode.OK)
		{
			Content = new StringContent(content, Encoding.UTF8,
				"application/json"),
		};
}
