namespace StockSharp.Connectors.Tests;

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
}
