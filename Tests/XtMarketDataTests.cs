namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Xt;

/// <summary>
/// Xt publishes market data without credentials.
/// </summary>
[TestClass]
public class XtMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new XtMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [XtSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_USDT",
		BoardCode = BoardCodes.Xt,
	};
}
