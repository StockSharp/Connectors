namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.PintuPro;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for PintuPro.</summary>
[TestClass]
[RegionalLiveTests]
public class PintuProTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new PintuProMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-IDR",
		BoardCode = BoardCodes.PintuPro,
	};
}
