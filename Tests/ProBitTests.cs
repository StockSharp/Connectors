namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.ProBit;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for ProBit.</summary>
[TestClass]
[RegionalLiveTests]
public class ProBitTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new ProBitMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USDT",
		BoardCode = BoardCodes.ProBit,
	};
}
