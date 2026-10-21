namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Poloniex;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Poloniex.</summary>
[TestClass]
public class PoloniexTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new PoloniexMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.Poloniex,
	};
}
