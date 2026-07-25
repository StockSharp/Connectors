namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Weex;

/// <summary>
/// Weex publishes market data without credentials.
/// </summary>
[TestClass]
public class WeexMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new WeexMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [WeexSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.Weex,
	};
}
