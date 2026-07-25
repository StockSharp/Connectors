namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Coinone;
using StockSharp.Messages;

/// <summary>
/// Coinone publishes market data without credentials.
/// </summary>
[TestClass]
public class CoinoneMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new CoinoneMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_KRW",
		BoardCode = BoardCodes.Coinone,
	};
}
