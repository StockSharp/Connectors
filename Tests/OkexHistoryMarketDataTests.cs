namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.OkexHistory;

/// <summary>
/// OkexHistory publishes market data without credentials.
/// </summary>
[TestClass]
public class OkexHistoryMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new OkexHistoryMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USDT",
		BoardCode = BoardCodes.Okex,
	};

	/// <inheritdoc />
	/// <remarks>
	/// This source serves daily archive files, so the most recent hours are not published yet
	/// and the request has to reach back past the last complete day.
	/// </remarks>
	protected override int CandlesDepth => 3 * 24 * 60;
}
