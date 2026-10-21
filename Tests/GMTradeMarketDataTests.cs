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
	protected override MessageAdapter CreateAdapter() => CreateAdapter(Env("STOCKSHARP_LIVE_GMTRADE_TESTS"));

	internal static GMTradeMessageAdapter CreateAdapter(string setting)
	{
		if (!setting.EqualsIgnoreCase("1"))
			Inconclusive("Set STOCKSHARP_LIVE_GMTRADE_TESTS=1 when GMTrade keeper and WebSocket API quota is available on this runner.");
		return new(new IncrementalIdGenerator());
	}

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SOL-USD-WSOL-USDC",
		BoardCode = BoardCodes.GMTrade,
	};
}
