namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Dexalot;
using StockSharp.Messages;

/// <summary>Dexalot publishes public CLOB market data without credentials.</summary>
[TestClass]
public class DexalotMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new DexalotMessageAdapter(new IncrementalIdGenerator())
		{
			Pairs = "ALOT/USDC",
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "ALOT/USDC",
		BoardCode = BoardCodes.Dexalot,
	};

	/// <inheritdoc />
	protected override int CandlesDepth => 24;
}
