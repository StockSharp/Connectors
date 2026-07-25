namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Backpack;
using StockSharp.Messages;

/// <summary>
/// Backpack publishes market data without credentials.
/// </summary>
[TestClass]
public class BackpackMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BackpackMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_USDC",
		BoardCode = BoardCodes.Backpack,
	};
}
