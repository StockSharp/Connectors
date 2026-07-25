namespace StockSharp.Connectors.Tests;

using System;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Cetus;
using StockSharp.Messages;

/// <summary>
/// Cetus publishes market data without credentials.
/// </summary>
[TestClass]
public class CetusMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new CetusMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SUI-USDC",
		BoardCode = BoardCodes.Cetus,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Ticks are single swaps of one concentrated liquidity pool taken off the Sui
	/// checkpoint stream, and the configured SUI-USDC pool executes roughly one swap a
	/// minute, so the default wait is far too short for it.
	/// </remarks>
	protected override TimeSpan DataTimeout => TimeSpan.FromSeconds(90);
}
