namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.LBank;
using StockSharp.Messages;

/// <summary>
/// LBank publishes market data without credentials.
/// </summary>
[TestClass]
public class LBankMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override TimeSpan ConnectTimeout => TimeSpan.FromSeconds(60);

	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new LBankMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC/USDT",
		BoardCode = BoardCodes.LBank,
	};
}
