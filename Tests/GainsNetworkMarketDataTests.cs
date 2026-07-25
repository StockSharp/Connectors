namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.GainsNetwork;
using StockSharp.Messages;

/// <summary>
/// GainsNetwork publishes market data without credentials.
/// </summary>
[TestClass]
public class GainsNetworkMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GainsNetworkMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USD",
		BoardCode = BoardCodes.GainsNetwork,
	};
}
