namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Binance;
using StockSharp.Messages;

/// <summary>
/// Binance publishes spot market data without credentials.
/// </summary>
[TestClass]
public class BinanceMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BinanceMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [BinanceSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.Binance,
	};
}
