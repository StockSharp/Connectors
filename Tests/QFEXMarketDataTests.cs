namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.QFEX;

/// <summary>
/// QFEX publishes market data without credentials.
/// </summary>
[TestClass]
public class QFEXMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new QFEXMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "NVDA-USD",
		BoardCode = BoardCodes.QFEX,
	};

	/// <inheritdoc />
	/// <remarks>
	/// The trade stream works, but the venue is far too thin to produce a print in
	/// time: a session subscribed to every active QFEX symbol at once saw three
	/// trades in five minutes, and none of them on this instrument.
	/// </remarks>
	protected override DataType[] SkippedDataTypes => [DataType.Ticks];
}
