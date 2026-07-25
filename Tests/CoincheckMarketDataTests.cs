namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Coincheck;
using StockSharp.Messages;

/// <summary>
/// Coincheck publishes market data without credentials.
/// </summary>
[TestClass]
public class CoincheckMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new CoincheckMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/JPY",
		BoardCode = BoardCodes.Coincheck,
	};

	/// <inheritdoc />
	/// <remarks>
	/// The venue is thin: BTC/JPY, its busiest pair, leaves gaps of a minute and a half
	/// between trades.
	/// </remarks>
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(90);
}
