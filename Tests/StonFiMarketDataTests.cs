namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.StonFi;

/// <summary>STON.fi publishes public TON AMM data without credentials.</summary>
[TestClass]
public class StonFiMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new StonFiMessageAdapter(new IncrementalIdGenerator())
		{
			Pools =
				"EQCGScrZe1xbyWqWDvdI6mzP-GAcAWFv6ZXuaJOuSqemxku4",
			PoolLimit = 1,
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "USD₮/GRAM",
		BoardCode = BoardCodes.StonFi,
	};

	/// <inheritdoc />
	protected override int CandlesDepth => 10;
}
