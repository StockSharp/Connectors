namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Zaif;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Zaif.</summary>
[TestClass]
public class ZaifTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new ZaifMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/JPY",
		BoardCode = BoardCodes.Zaif,
	};
}
