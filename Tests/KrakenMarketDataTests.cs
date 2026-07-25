namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Kraken;
using StockSharp.Messages;

/// <summary>
/// Kraken publishes spot market data without credentials.
/// </summary>
[TestClass]
public class KrakenMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new KrakenMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "XBT/USD",
		BoardCode = BoardCodes.Kraken,
	};
}
