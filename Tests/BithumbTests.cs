namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bithumb;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Bithumb.</summary>
[TestClass]
[RegionalLiveTests]
public class BithumbTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BithumbMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/KRW",
		BoardCode = BoardCodes.Bithumb,
	};
}
