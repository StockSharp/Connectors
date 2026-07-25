namespace StockSharp.Connectors.Tests;

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
	/// <remarks>
	/// Ticker, order book and trades are public, but candles come from the exchange
	/// API, which answers an anonymous request with 401.
	/// </remarks>
	protected override DataType[] SkippedDataTypes
		=> [.. LunoMessageAdapter.AllTimeFrames.Select(tf => tf.TimeFrame())];
}
