namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.Meteora;

/// <summary>
/// Meteora publishes market data without credentials.
/// </summary>
[TestClass]
public class MeteoraMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new MeteoraMessageAdapter(new IncrementalIdGenerator())
	{
		// pin the pool instead of taking whatever tops the volume ranking today
		Pools = "5rCf1DM8LjKTw4YqhnoLcngyZYeNnQqztScTogYHAS6|SOL|USDC",
		MaximumDiscoveredPools = 0,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SOL-USDC",
		BoardCode = BoardCodes.Meteora,
	};

	/// <inheritdoc />
	protected override int CandlesDepth => 2;

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2 == DataType.Ticks || message.DataType2.IsTFCandles)
			message.Count = 1;
	}
}
