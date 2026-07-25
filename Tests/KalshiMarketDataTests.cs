namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Kalshi;
using StockSharp.Messages;

/// <summary>
/// Kalshi publishes market data without credentials.
/// </summary>
[TestClass]
public class KalshiMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new KalshiMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	/// <remarks>
	/// Kalshi markets expire, so the checks use one of the few long living ones - the
	/// 2030 World Cup winner - which is quoted around the clock and closes in 2031.
	/// </remarks>
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "KXWC-30-ESP",
		BoardCode = BoardCodes.Kalshi,
	};
}
