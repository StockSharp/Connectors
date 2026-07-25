namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Pionex;

/// <summary>
/// Pionex publishes market data without credentials.
/// </summary>
[TestClass]
public class PionexMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new PionexMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [PionexSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_USDT",
		BoardCode = BoardCodes.Pionex,
	};
}
