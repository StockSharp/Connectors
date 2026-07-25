namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;
using StockSharp.MoexISS;

/// <summary>
/// MoexISS publishes market data without credentials.
/// </summary>
[TestClass]
public class MoexISSMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new MoexISSMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "SBER",
		BoardCode = "TQBR",
	};

	// ISS has no level1 stream: the adapter maps that data type to the dividend history
	// (/iss/securities/<code>/dividends.json), which holds a handful of records per year and
	// therefore is empty for any short window the harness asks for
	/// <inheritdoc />
	protected override DataType[] SkippedDataTypes => [DataType.Level1];
}
