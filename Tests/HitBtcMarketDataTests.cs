namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.HitBtc;
using StockSharp.Messages;

/// <summary>
/// HitBtc publishes market data without credentials.
/// </summary>
[TestClass]
public class HitBtcMarketDataTests : LiveMarketDataTestBase
{
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
