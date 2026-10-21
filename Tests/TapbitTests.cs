namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Tapbit;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Tapbit.</summary>
[TestClass]
[RegionalLiveTests]
public class TapbitTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new TapbitMessageAdapter(new IncrementalIdGenerator())
		{
			Sections = TapbitSections.Spot,
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.Tapbit,
	};
}
