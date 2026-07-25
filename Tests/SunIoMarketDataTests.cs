namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.SunIo;

/// <summary>
/// SunIo publishes market data without credentials.
/// </summary>
[TestClass]
public class SunIoMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new SunIoMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "TRX-USDT",
		BoardCode = BoardCodes.SunIo,
	};
}
