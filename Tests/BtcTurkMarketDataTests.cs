namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BtcTurk;
using StockSharp.Messages;

/// <summary>
/// BtcTurk publishes spot market data without credentials.
/// </summary>
[TestClass]
public class BtcTurkMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter()
		=> new BtcTurkMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/TRY",
		BoardCode = BoardCodes.BtcTurk,
	};
}
