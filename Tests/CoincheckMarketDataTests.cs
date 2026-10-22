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
[RegionalLiveTests]
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
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(90);
}
