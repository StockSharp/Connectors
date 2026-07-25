namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Toobit;

/// <summary>
/// Toobit publishes market data without credentials.
/// </summary>
[TestClass]
public class ToobitMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new ToobitMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [ToobitSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.Toobit,
	};
}
