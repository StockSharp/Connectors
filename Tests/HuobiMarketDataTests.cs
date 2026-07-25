namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Huobi;
using StockSharp.Messages;

/// <summary>
/// Huobi publishes market data without credentials.
/// </summary>
[TestClass]
public class HuobiMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new HuobiMessageAdapter(new IncrementalIdGenerator())
	{
		Section = HuobiSections.Spot,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.Huobi,
	};
}
