namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Phemex;

/// <summary>
/// Phemex publishes market data without credentials.
/// </summary>
[TestClass]
public class PhemexMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new PhemexMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [PhemexSections.Futures],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.PhemexFutures,
	};
}
