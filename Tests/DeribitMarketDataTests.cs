namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Deribit;
using StockSharp.Messages;

/// <summary>
/// Deribit publishes market data without credentials.
/// </summary>
[TestClass]
public class DeribitMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new DeribitMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-PERPETUAL",
		BoardCode = BoardCodes.Deribit,
	};
}
