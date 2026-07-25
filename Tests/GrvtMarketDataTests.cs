namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Grvt;
using StockSharp.Messages;

/// <summary>
/// Grvt publishes market data without credentials.
/// </summary>
[TestClass]
public class GrvtMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GrvtMessageAdapter(new IncrementalIdGenerator())
	{
		Environment = GrvtEnvironments.Production,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_USDT_PERP",
		BoardCode = BoardCodes.Grvt,
	};
}
