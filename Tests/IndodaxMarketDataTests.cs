namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Indodax;
using StockSharp.Messages;

/// <summary>
/// Indodax publishes market data without credentials.
/// </summary>
[TestClass]
public class IndodaxMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new IndodaxMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCIDR",
		BoardCode = BoardCodes.Indodax,
	};
}
