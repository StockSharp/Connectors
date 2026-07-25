namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.OrderlyNetwork;

/// <summary>
/// OrderlyNetwork publishes market data without credentials.
/// </summary>
[TestClass]
public class OrderlyNetworkMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new OrderlyNetworkMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "PERP_BTC_USDC",
		BoardCode = BoardCodes.OrderlyNetwork,
	};
}
