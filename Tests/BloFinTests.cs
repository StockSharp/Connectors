namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BloFin;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for BloFin.</summary>
[TestClass]
[RegionalLiveTests]
public class BloFinTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BloFinMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USDT",
		BoardCode = BoardCodes.BloFin,
	};
}
