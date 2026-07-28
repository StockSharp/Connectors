namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Pendle;

/// <summary>Pendle publishes PT/YT market data without credentials.</summary>
[TestClass]
public class PendleMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new PendleMessageAdapter(new IncrementalIdGenerator())
		{
			MarketAddresses =
				"0x34280882267ffa6383b363e278b027be083bbe3b",
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "PT-STETH-30DEC2027",
		BoardCode = BoardCodes.Pendle,
	};

	/// <inheritdoc />
	protected override DataType[] SkippedDataTypes =>
		[DataType.Ticks, DataType.MarketDepth];

	/// <inheritdoc />
	protected override int CandlesDepth => 24;
}
