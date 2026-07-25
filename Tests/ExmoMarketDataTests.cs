namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Exmo;
using StockSharp.Messages;

/// <summary>
/// Exmo publishes market data without credentials.
/// </summary>
[TestClass]
public class ExmoMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new ExmoMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDC",
		BoardCode = BoardCodes.Exmo,
	};
}
