namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Gmx;
using StockSharp.Messages;

/// <summary>
/// Gmx publishes market data without credentials.
/// </summary>
[TestClass]
public class GmxMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GmxMessageAdapter(new IncrementalIdGenerator())
	{
		Network = GmxNetworks.Arbitrum,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USD [BTC-USDC]",
		BoardCode = BoardCodes.Gmx,
	};
}
