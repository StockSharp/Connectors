namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.HitBtc;
using StockSharp.HitBtc.Native;
using StockSharp.Messages;

/// <summary>
/// Tests for the HitBtc connector.
/// </summary>
[TestClass]
public class HitBtcTests : LiveMarketDataTestBase
{
	[TestMethod]
	[DataRow("trades", true, 100)]
	[DataRow("trades", false, null)]
	[DataRow("ticker/1s", true, null)]
	[DataRow("orderbook/full", true, null)]
	public void TradeSubscriptionsRequestAnInitialSnapshot(string channel, bool subscribe, int? expectedLimit)
	{
		var request = HitBtcSocketClient.CreatePublicSubscription(channel, "UNIUSDT", subscribe);

		AreEqual(1, request.Symbols.Length);
		AreEqual("UNIUSDT", request.Symbols[0]);
		AreEqual(expectedLimit, request.Limit);
	}

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new HitBtcMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "UNI/USDT",
		BoardCode = BoardCodes.HitBtc,
	};

	/// <inheritdoc />
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(90);
}
