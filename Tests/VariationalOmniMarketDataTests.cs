namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.VariationalOmni;

/// <summary>
/// VariationalOmni publishes market data without credentials.
/// </summary>
[TestClass]
public class VariationalOmniMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new VariationalOmniMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC",
		BoardCode = BoardCodes.VariationalOmni,
	};
}
