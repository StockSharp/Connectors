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
		// ethereum-rpc.publicnode.com serves eth_getLogs only for the last hundred blocks and
		// rejects anything older with HTTP 403 "archive requests require a personal token",
		// which leaves the swap history unreadable
		RpcEndpoint = "https://gateway.tenderly.co/public/mainnet",

		Pools = "0x667701e51b4d1ca244f17c78f7ab8744b4c99f9b",

		// a tick request without a start time walks the whole window and reads the timestamp of
		// every block a swap sits in, so the default quarter of a million blocks never finishes
		HistoryBlockCount = 500,
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
}
