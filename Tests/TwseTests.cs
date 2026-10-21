namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Twse;
using StockSharp.Messages;

/// <summary>Anonymous market data checks for Twse.</summary>
[TestClass]
public class TwseTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new TwseMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "2330",
		Native = "2330",
		BoardCode = BoardCodes.Tsec,
	};

	/// <inheritdoc />
	protected override int CandlesDepth => 7;
}
