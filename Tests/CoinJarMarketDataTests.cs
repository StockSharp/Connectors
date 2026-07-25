namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.CoinJar;
using StockSharp.Messages;

/// <summary>
/// CoinJar publishes market data without credentials.
/// </summary>
[TestClass]
public class CoinJarMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new CoinJarMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCAUD",
		BoardCode = BoardCodes.CoinJar,
	};
}
