namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.GMTrade;
using StockSharp.Messages;

/// <summary>
/// GMTrade publishes market data without credentials.
/// </summary>
[TestClass]
public class GMTradeMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GMTradeMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SOL-USD-WSOL-USDC",
		BoardCode = BoardCodes.GMTrade,
	};
}
