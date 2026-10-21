namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.BYDFi;
using StockSharp.Messages;

/// <summary>
/// BYDFi publishes market data without credentials.
/// </summary>
[TestClass]
public class BYDFiMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => CreateAdapter(Env("STOCKSHARP_LIVE_BYDFI_TESTS"));

	internal static BYDFiMessageAdapter CreateAdapter(string setting)
	{
		if (!setting.EqualsIgnoreCase("1"))
			Inconclusive("Set STOCKSHARP_LIVE_BYDFI_TESTS=1 when the published BYDFi futures API is accessible on this runner.");
		return new(new IncrementalIdGenerator());
	}

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTC-USDT",
		BoardCode = BoardCodes.BYDFi,
	};
}
