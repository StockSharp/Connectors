namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Kucoin;
using StockSharp.Messages;

/// <summary>
/// Kucoin publishes market data without credentials.
/// </summary>
[TestClass]
public class KucoinMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new KucoinMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [KucoinSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USDT",
		BoardCode = BoardCodes.Kucoin,
	};
}
