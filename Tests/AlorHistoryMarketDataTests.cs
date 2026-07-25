namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.AlorHistory;
using StockSharp.Messages;

/// <summary>
/// AlorHistory publishes market data without credentials.
/// </summary>
[TestClass]
public class AlorHistoryMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new AlorHistoryMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SBER",
		BoardCode = BoardCodes.Moex,
	};
}
