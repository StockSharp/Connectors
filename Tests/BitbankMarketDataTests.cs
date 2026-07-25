namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Bitbank;
using StockSharp.Messages;

/// <summary>
/// Bitbank publishes market data without credentials.
/// </summary>
[TestClass]
public class BitbankMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BitbankMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC_JPY",
		BoardCode = BoardCodes.Bitbank,
	};

	/// <inheritdoc />
	/// <remarks>
	/// The venue is thin and bursty: even on BTC_JPY the trade room regularly stays silent
	/// for a minute at a time.
	/// </remarks>
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(90);
}
