namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BitStamp;
using StockSharp.Messages;

/// <summary>
/// Bitstamp publishes market data without credentials.
/// </summary>
[TestClass]
public class BitStampMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitStampMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USD",
		BoardCode = BoardCodes.BitStamp,
	};
}
