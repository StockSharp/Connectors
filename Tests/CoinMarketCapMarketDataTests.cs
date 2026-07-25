namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinMarketCap;
using StockSharp.Messages;

/// <summary>
/// CoinMarketCap publishes market data without credentials.
/// </summary>
[TestClass]
public class CoinMarketCapMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new CoinMarketCapMessageAdapter(new IncrementalIdGenerator())
	{
		AccessMode = CoinMarketCapAccessModes.Keyless,
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USD",
		BoardCode = BoardCodes.CoinMarketCap,
	};
}
