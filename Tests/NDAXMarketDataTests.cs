namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.NDAX;

/// <summary>
/// NDAX publishes market data without credentials.
/// </summary>
[TestClass]
public class NDAXMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new NDAXMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCCAD",
		BoardCode = BoardCodes.NDAX,
	};

	// ticker, order book and candles arrive at once, but the NDAX tape is far too thin for a
	// bounded wait: BTCCAD is the busiest instrument of the venue and still prints only about
	// one trade every three minutes, and the socket trade subscription carries no history
	/// <inheritdoc />
	protected override DataType[] SkippedDataTypes => [DataType.Ticks];
}
