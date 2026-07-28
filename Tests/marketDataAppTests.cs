namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
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

using StockSharp.MarketDataApp;
using StockSharp.MarketDataApp.Native;
using StockSharp.Messages;

[TestClass]
public class MarketDataAppTests : BaseTestClass
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
	public void DefaultsAndSettingsArePersisted()
	{
		var source = new MarketDataAppMessageAdapter(
			new IncrementalIdGenerator());

		AreEqual(new Uri("https://api.marketdata.app/v1/"),
			source.RestEndpoint);
		IsTrue(source.AdjustSplits);
		IsFalse(source.ExtendedHours);
		AreEqual(1000, source.MaximumOptionContracts);
		AreEqual(12,
			MarketDataAppMessageAdapter.AllTimeFrames.Count());
		CollectionAssert.AreEqual(
			new[] { BoardCodes.MarketDataApp },
			source.AssociatedBoards);

		source.Token = "token".Secure();
		source.RestEndpoint = new("https://api.example/v1/");
		source.ExtendedHours = true;
		source.AdjustSplits = false;
		source.MaximumOptionContracts = 250;
		var storage = new SettingsStorage();
		source.Save(storage);
		var target = new MarketDataAppMessageAdapter(
			new IncrementalIdGenerator());

		target.Load(storage);

		AreEqual("token", target.Token.UnSecure());
		AreEqual(new Uri("https://api.example/v1/"),
			target.RestEndpoint);
		IsTrue(target.ExtendedHours);
		IsFalse(target.AdjustSplits);
		AreEqual(250, target.MaximumOptionContracts);
	}

	[TestMethod]
	public async Task RestClientUsesBearerAndAcceptsHttp203()
	{
		HttpRequestMessage captured = null;
		var handler = new Handler((request, _) =>
		{
			captured = request;
			return Task.FromResult(Json(
				"""
				{
				  "s":"ok",
				  "symbol":["AAPL"],
				  "bid":[200.1],
				  "ask":[200.2],
				  "last":[200.15],
				  "updated":[1785182463]
				}
				""", HttpStatusCode.NonAuthoritativeInformation));
		});
		using var client = new MarketDataAppRestClient(
			new("https://api.example/v1/"), "token".Secure(),
			handler);

		var quotes = await client.GetQuotesAsync(
			MarketDataAppAssetKinds.Stock, "AAPL",
			CancellationToken);

		AreEqual(1, quotes.Length);
		AreEqual("AAPL", quotes[0].Symbol);
		AreEqual(200.1m, quotes[0].Bid);
		AreEqual("Bearer", captured.Headers.Authorization.Scheme);
		AreEqual("token",
			captured.Headers.Authorization.Parameter);
		AreEqual("/v1/stocks/quotes/AAPL/",
			captured.RequestUri.AbsolutePath);
	}

	[TestMethod]
	public void PackedQuoteArraysMapByIndex()
	{
		var quotes = JObject.Parse(
			"""
			{
			  "s":"ok",
			  "symbol":["AAPL","MSFT"],
			  "ask":[201.2,null],
			  "askSize":[100,null],
			  "bid":[201.1,450.5],
			  "bidSize":[200,50],
			  "last":[201.15,450.6],
			  "change":[1.5,-2.1],
			  "volume":[123456,654321],
			  "updated":[1785182463,1785182464]
			}
			""").ToQuotes(null);

		AreEqual(2, quotes.Length);
		AreEqual("AAPL", quotes[0].Symbol);
		AreEqual(201.2m, quotes[0].Ask);
		AreEqual(123456m, quotes[0].Volume);
		AreEqual("MSFT", quotes[1].Symbol);
		IsNull(quotes[1].Ask);
		AreEqual(-2.1m, quotes[1].Change);
	}

	[TestMethod]
	public void OptionChainMapsContractMetadataAndGreeks()
	{
		var quote = JObject.Parse(
			"""
			{
			  "s":"ok",
			  "optionSymbol":["AAPL271217P00335000"],
			  "underlying":["AAPL"],
			  "expiration":[1829077200],
			  "side":["put"],
			  "strike":[335],
			  "updated":[1785182400],
			  "bid":[36.35],
			  "ask":[37.55],
			  "last":[37.83],
			  "openInterest":[59],
			  "underlyingPrice":[336.99],
			  "iv":[0.2962],
			  "delta":[-0.3688],
			  "gamma":[0.0034],
			  "theta":[-0.041],
			  "vega":[1.25]
			}
			""").ToQuotes(null).Single();
		var instrument = quote.ToInstrument(
			MarketDataAppAssetKinds.Option, SecurityTypes.Option);
		var security = instrument.ToSecurityMessage(42);

		AreEqual("AAPL271217P00335000", instrument.Symbol);
		AreEqual("option:AAPL271217P00335000",
			instrument.NativeId);
		AreEqual(OptionTypes.Put, security.OptionType);
		AreEqual(335m, security.Strike);
		AreEqual("AAPL",
			security.UnderlyingSecurityId.SecurityCode);
		AreEqual(BoardCodes.MarketDataApp,
			security.SecurityId.BoardCode);
		AreEqual(-0.3688m, quote.Delta);
		AreEqual(0.2962m, quote.ImpliedVolatility);
	}

	[TestMethod]
	public async Task CandleRequestUsesOfficialPackedContract()
	{
		HttpRequestMessage captured = null;
		var handler = new Handler((request, _) =>
		{
			captured = request;
			return Task.FromResult(Json(
				"""
				{
				  "s":"ok",
				  "t":[1785162600,1785162900],
				  "o":[200,201],
				  "h":[202,203],
				  "l":[199,200],
				  "c":[201,202],
				  "v":[1000,1200]
				}
				"""));
		});
		using var client = new MarketDataAppRestClient(
			new("https://api.example/v1/"), default, handler);

		var candles = await client.GetCandlesAsync(
			MarketDataAppAssetKinds.Stock, "5", "AAPL",
			new(2026, 7, 27, 13, 30, 0, DateTimeKind.Utc),
			new(2026, 7, 27, 14, 0, 0, DateTimeKind.Utc),
			true, false, CancellationToken);

		AreEqual(2, candles.Length);
		AreEqual(200m, candles[0].Open);
		AreEqual(202m, candles[1].Close);
		AreEqual(1200m, candles[1].Volume);
		AreEqual("/v1/stocks/candles/5/AAPL/",
			captured.RequestUri.AbsolutePath);
		IsTrue(captured.RequestUri.Query.Contains(
			"extended=true", StringComparison.Ordinal));
		IsTrue(captured.RequestUri.Query.Contains(
			"adjustsplits=false", StringComparison.Ordinal));
		IsFalse(captured.Headers.Contains("Authorization"));
	}

	[TestMethod]
	public async Task NoData404IsAnEmptySuccessfulLookup()
	{
		var handler = new Handler((_, _) => Task.FromResult(Json(
			"""{"s":"no_data"}""", HttpStatusCode.NotFound)));
		using var client = new MarketDataAppRestClient(
			new("https://api.example/v1/"), default, handler);

		var quotes = await client.GetQuotesAsync(
			MarketDataAppAssetKinds.Index, "VIX",
			CancellationToken);

		AreEqual(0, quotes.Length);
		AreEqual("5", TimeSpan.FromMinutes(5).ToResolution(
			MarketDataAppAssetKinds.Stock));
		AreEqual("W", TimeSpan.FromDays(7).ToResolution(
			MarketDataAppAssetKinds.Fund));
		Throws<NotSupportedException>(() =>
			TimeSpan.FromMinutes(5).ToResolution(
				MarketDataAppAssetKinds.Fund));
	}

	private static HttpResponseMessage Json(string content,
		HttpStatusCode status = HttpStatusCode.OK)
		=> new(status)
		{
			Content = new StringContent(content, Encoding.UTF8,
				"application/json"),
		};
}
