namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.WhiteBit;

/// <summary>
/// WhiteBit publishes market data without credentials.
/// </summary>
[TestClass]
public class WhiteBitMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new WhiteBitMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [WhiteBitSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_USDT",
		BoardCode = BoardCodes.WhiteBit,
	};
}
