namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Chainflip;
using StockSharp.Messages;

/// <summary>Chainflip publishes market data without credentials.</summary>
[TestClass]
public class ChainflipMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new ChainflipMessageAdapter(new IncrementalIdGenerator())
		{
			Pools = "USDT@ETHEREUM-USDC@ETHEREUM",
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "USDT@ETHEREUM-USDC@ETHEREUM",
		BoardCode = BoardCodes.Chainflip,
	};

	/// <inheritdoc />
	protected override DataType[] SkippedDataTypes =>
		[DataType.CandleTimeFrame];
}
