namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Cex;
using StockSharp.Messages;

/// <summary>
/// Cex publishes market data without credentials.
/// </summary>
[TestClass]
public class CexMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new CexMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USD",
		BoardCode = BoardCodes.Cex,
	};

	/// <inheritdoc />
	/// <remarks>
	/// The venue keeps two market data streams behind a login: <c>order-book-subscribe</c>
	/// answers <c>Please Login</c> to an anonymous session, and its candle stream
	/// (<c>init-ohlcv</c>) answers <c>Invalid pair</c> for every pair. Candle history is
	/// public, but only for days that are already closed - the daily aggregate of the
	/// current day is served as an empty array - so the recent minutes this suite asks for
	/// are never covered.
	/// </remarks>
	protected override DataType[] SkippedDataTypes =>
	[
		DataType.MarketDepth,
		DataType.CandleTimeFrame,
	];
}
