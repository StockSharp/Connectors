namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.FluidDex;
using StockSharp.Messages;

/// <summary>
/// Fluid DEX publishes market data without credentials.
/// </summary>
[TestClass]
public class FluidDexMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new FluidDexMessageAdapter(new IncrementalIdGenerator())
	{
		Pools = "0x667701e51b4d1ca244f17c78f7ab8744b4c99f9b",

		// The default public RPC serves recent logs without an archive subscription.
		HistoryBlockCount = 50,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "USDT-USDC-F7",
		BoardCode = BoardCodes.FluidDex,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Candles are rebuilt from swap logs: the start block is found by bisecting the chain and
	/// every matched log costs another block read.
	/// </remarks>
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(60);

	/// <inheritdoc />
	protected override int CandlesDepth => 2;

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2 == DataType.Ticks || message.DataType2.IsTFCandles)
			message.Count = 1;
	}
}
