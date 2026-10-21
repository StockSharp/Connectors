namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bullish;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Bullish.</summary>
[TestClass]
[RegionalLiveTests]
public class BullishTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BullishMessageAdapter(new IncrementalIdGenerator())
		{
			Sections = [BullishSections.Spot],
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDC",
		BoardCode = BoardCodes.Bullish,
	};
}
