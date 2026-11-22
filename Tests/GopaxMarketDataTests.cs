namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Gopax;
using StockSharp.Messages;

/// <summary>
/// Gopax publishes market data without credentials.
/// </summary>
[TestClass]
public class GopaxMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GopaxMessageAdapter(new IncrementalIdGenerator())
	{
		Address = "gopax.co.kr",
	};

	/// <summary>
	/// Gopax trades thinly, BTC/KRW goes hours without a single minute candle,
	/// while the stablecoin pair prints one every few minutes.
	/// </summary>
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "USDT/KRW",
		BoardCode = BoardCodes.Gopax,
	};
}
