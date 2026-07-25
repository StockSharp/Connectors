namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BTCMarkets;
using StockSharp.Messages;

/// <summary>
/// BTCMarkets publishes market data without credentials.
/// </summary>
[TestClass]
public class BTCMarketsMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BTCMarketsMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-AUD",
		BoardCode = BoardCodes.BTCMarkets,
	};
}
