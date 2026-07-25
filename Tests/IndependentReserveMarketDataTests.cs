namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.IndependentReserve;
using StockSharp.Messages;

/// <summary>
/// IndependentReserve publishes market data without credentials.
/// </summary>
[TestClass]
public class IndependentReserveMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new IndependentReserveMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "XBT/AUD",
		BoardCode = BoardCodes.IndependentReserve,
	};
}
