namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Velodrome;

/// <summary>Velodrome publishes market data without credentials.</summary>
[TestClass]
public class VelodromeMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new VelodromeMessageAdapter(new IncrementalIdGenerator())
		{
			RpcEndpoint = "https://optimism-rpc.publicnode.com",
			WebSocketEndpoint = "wss://optimism-rpc.publicnode.com",
		};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "WETH-USDC-VOLATILE",
		BoardCode = BoardCodes.Velodrome,
	};

	/// <inheritdoc />
	protected override DataType[] SkippedDataTypes =>
		[DataType.Ticks, DataType.CandleTimeFrame];
}
