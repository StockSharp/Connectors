namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.GmoCoin;
using StockSharp.Messages;

/// <summary>
/// GmoCoin publishes market data without credentials.
/// </summary>
[TestClass]
public class GmoCoinMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GmoCoinMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC",
		BoardCode = BoardCodes.GmoCoin,
	};
}
