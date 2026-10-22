namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.DxFeed;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for DxFeed.</summary>
[TestClass]
[RegionalLiveTests(nameof(OrderBook))]
[RegionalLiveTests(nameof(Ticks))]
[RegionalLiveTests(nameof(Candles))]
public class DxFeedTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new DxFeedMessageAdapter(new IncrementalIdGenerator())
		{
			Address = "wss://demo.dxfeed.com/market-data/dxlink-ws",
			MarketDepthSources = "ntv",
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "AAPL",
		BoardCode = "DXFEED",
	};

	/// <inheritdoc />
	protected override SecurityLookupMessage SecuritiesLookup => new() { SecurityId = TestSecurityId };

	/// <inheritdoc />
	protected override TimeSpan CandleTimeFrame => TimeSpan.FromDays(1);

	/// <inheritdoc />
	protected override int CandlesDepth => 7;

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2 == DataType.Ticks)
		{
			// Include the preceding session when the delayed demo is checked outside trading hours.
			message.From = DateTime.UtcNow.AddDays(-7);
			message.To = DateTime.UtcNow;
			message.Count = 1;
		}
	}
}
