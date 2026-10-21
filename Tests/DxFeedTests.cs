namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.DxFeed;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for DxFeed.</summary>
[TestClass]
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
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2 == DataType.Ticks)
		{
			// The anonymous demo publishes delayed trades.
			message.From = DateTime.UtcNow.AddHours(-1);
			message.To = DateTime.UtcNow;
			message.Count = 1;
		}
	}
}
