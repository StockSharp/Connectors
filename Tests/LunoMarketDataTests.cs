namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Luno;
using StockSharp.Luno.Native;
using StockSharp.Messages;

/// <summary>
/// Luno publishes market data without credentials.
/// </summary>
[TestClass]
public class LunoMarketDataTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow("UNAVAILABLE")]
	[DataRow("FUTURE_STATUS")]
	[Timeout(10000, CooperativeCancellation = true)]
	public async Task MarketLookupLoadsNonTradingMarkets(string status)
	{
		var json = $$"""
			{"markets":[
				{"market_id":"ETHZAR","trading_status":"{{status}}","base_currency":"ETH","counter_currency":"ZAR","volume_scale":6,"price_scale":2},
				{"market_id":"XBTZAR","trading_status":"ACTIVE","base_currency":"XBT","counter_currency":"ZAR","volume_scale":6,"price_scale":2}
			]}
			""";
		using var client = new LunoRestClient("https://luno.example.test", null, null);
		HistoryHttpHandler.ReplaceClient(client, new HttpClient(new HistoryHttpHandler(request =>
		{
			AreEqual("/api/exchange/1/markets", request.RequestUri.AbsolutePath);
			return new(HttpStatusCode.OK) { Content = new StringContent(json) };
		})), "_http");
		var markets = await client.GetMarketsAsync(CancellationToken);
		AreEqual(2, markets.Length);
		AreEqual("ETHZAR", markets[0].MarketId);
		AreEqual("XBTZAR", markets[1].MarketId);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new LunoMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "XBTZAR",
		BoardCode = BoardCodes.Luno,
	};

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2 != DataType.Ticks)
			return;

		// Public trade history has a 24-hour lookback and the live stream requires credentials.
		var to = DateTime.UtcNow;
		message.From = to.AddHours(-22);
		message.To = to;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Ticker, order book and trades are public, but candles come from the exchange
	/// API, which answers an anonymous request with 401.
	/// </remarks>
	protected override DataType[] SkippedDataTypes
		=> [.. LunoMessageAdapter.AllTimeFrames.Select(tf => tf.TimeFrame())];
}
