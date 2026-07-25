namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Curve;
using StockSharp.Messages;

/// <summary>
/// Curve publishes market data without credentials.
/// </summary>
[TestClass]
public class CurveMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new CurveMessageAdapter(new IncrementalIdGenerator())
	{
		Pools = "0xbEbc44782C7dB0a1A60Cb6fe97d0b483032FF1C7|0x6B175474E89094C44Da98b954EedeAC495271d0F|0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48|DAI-USDC",
		MaximumDiscoveredPools = 0,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "DAI-USDC",
		BoardCode = BoardCodes.Curve,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Candles are folded from on chain swaps, and this stablecoin pool can sit idle for hours,
	/// so the history has to reach far enough back to contain any at all.
	/// </remarks>
	protected override int CandlesDepth => 1440;
}
