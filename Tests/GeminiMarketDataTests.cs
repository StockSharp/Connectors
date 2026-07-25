namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Gemini;
using StockSharp.Messages;

/// <summary>
/// Gemini publishes market data without credentials.
/// </summary>
[TestClass]
public class GeminiMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new GeminiMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSD",
		BoardCode = BoardCodes.Gemini,
	};
}
