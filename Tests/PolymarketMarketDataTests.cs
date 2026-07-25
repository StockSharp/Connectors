namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Polymarket;

/// <summary>
/// Polymarket publishes market data without credentials.
/// </summary>
[TestClass]
public class PolymarketMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new PolymarketMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "will-the-democrats-win-the-2028-us-presidential-election:yes",
		BoardCode = BoardCodes.Polymarket,
	};

	// the book and its price_change stream update every few seconds, but a tick is only
	// produced by a last_trade_price event, and an outcome that settles years from now is
	// actually traded far too rarely for a bounded wait - the market codes of the outcomes
	// that do trade constantly (the five minute crypto markets) live for five minutes and
	// cannot be named here
	/// <inheritdoc />
	protected override DataType[] SkippedDataTypes => [DataType.Ticks];
}
