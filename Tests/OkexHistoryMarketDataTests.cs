namespace StockSharp.Connectors.Tests;

using System;

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
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		// Daily archives are published after the trading day closes. Start at midnight so
		// the first record also verifies the request without scanning most of a large file.
		message.From = DateTime.UtcNow.Date.AddDays(-3);
		message.To = message.From.Value.AddDays(1);
		message.Count = 1;
		if (message.DataType2 == DataType.MarketDepth)
			message.MaxDepth = 20;
	}
}
