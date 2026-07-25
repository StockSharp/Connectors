namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bitkub;
using StockSharp.Messages;

/// <summary>
/// Bitkub publishes market data without credentials.
/// </summary>
[TestClass]
public class BitkubMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitkubMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_THB",
		BoardCode = BoardCodes.Bitkub,
	};
}
