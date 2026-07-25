namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bitmart;
using StockSharp.Messages;

/// <summary>
/// Bitmart publishes market data without credentials.
/// </summary>
[TestClass]
public class BitmartMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitmartMessageAdapter(new IncrementalIdGenerator())
	{
		Section = BitmartSections.Spot,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_USDT",
		BoardCode = BoardCodes.Bitmart,
	};
}
