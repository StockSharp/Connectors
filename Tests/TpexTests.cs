namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Tpex;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Tpex.</summary>
[TestClass]
public class TpexTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new TpexMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "6488",
		Native = "6488",
		BoardCode = "TPEX",
	};

	/// <inheritdoc />
	protected override int CandlesDepth => 7;
}
