namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Xrpl;

/// <summary>XRPL publishes public DEX data without credentials.</summary>
[TestClass]
public class XrplMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new XrplMessageAdapter(new IncrementalIdGenerator())
		{
			HistoryLedgerLimit = 1000,
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "XRP/RLUSD",
		BoardCode = BoardCodes.Xrpl,
	};

	/// <inheritdoc />
	protected override int CandlesDepth => 5;
}
