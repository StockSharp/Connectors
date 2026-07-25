namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BitFlyer;
using StockSharp.Messages;

/// <summary>
/// BitFlyer publishes market data without credentials.
/// </summary>
[TestClass]
public class BitFlyerMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitFlyerMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_JPY",
		BoardCode = BoardCodes.BitFlyer,
	};
}
