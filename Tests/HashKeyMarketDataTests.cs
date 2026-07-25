namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.HashKey;
using StockSharp.Messages;

/// <summary>
/// HashKey publishes market data without credentials.
/// </summary>
[TestClass]
public class HashKeyMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new HashKeyMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [HashKeySections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.HashKey,
	};
}
