namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Korbit;
using StockSharp.Messages;

/// <summary>
/// Korbit publishes market data without credentials.
/// </summary>
[TestClass]
public class KorbitMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new KorbitMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_KRW",
		BoardCode = BoardCodes.Korbit,
	};
}
