namespace StockSharp.Connectors.Tests;

using System;
using System.Linq;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Luno;
using StockSharp.Messages;

/// <summary>
/// Luno publishes market data without credentials.
/// </summary>
[TestClass]
public class LunoMarketDataTests : LiveMarketDataTestBase
{
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
