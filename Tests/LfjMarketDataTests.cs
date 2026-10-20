namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Lfj;
using StockSharp.Messages;

/// <summary>
/// Lfj publishes market data without credentials.
/// </summary>
[TestClass]
public class LfjMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new LfjMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "WAVAX-USDC-LB10",
		BoardCode = BoardCodes.Lfj,
	};

	/// <inheritdoc />
	protected override void ConfigureSubscription(MarketDataMessage message)
	{
		if (message.DataType2 == DataType.Ticks || message.DataType2.IsTFCandles)
		{
			// This on-chain minute contains swaps; recent minutes can have no trades in this pool.
			message.From = new DateTime(2026, 10, 19, 22, 15, 0, DateTimeKind.Utc);
			message.To = message.From.Value.AddMinutes(1);
			message.Count = 1;
		}
	}
}
