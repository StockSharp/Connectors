namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.LATOKEN;
using StockSharp.Messages;

/// <summary>
/// Latoken publishes market data without credentials.
/// </summary>
/// <remarks>The public WebSocket endpoint can return HTTP 522 before any subscription starts.</remarks>
[TestClass]
[RegionalLiveTests]
public class LatokenMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new LatokenMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.Latoken,
	};
}
