namespace StockSharp.Connectors.Tests;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.FXOpen;
using StockSharp.Messages;

/// <summary>
/// FXOpen publishes market data without credentials.
/// </summary>
[TestClass]
public class FXOpenMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new FXOpenMessageAdapter(new IncrementalIdGenerator());

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "EURUSD",
		BoardCode = BoardCodes.FXOpen,
	};

	/// <inheritdoc />
	/// <remarks>
	/// Forex is closed from Friday evening to Sunday evening, so the minute history window must
	/// reach behind the weekend to contain any bar at all.
	/// </remarks>
	protected override int CandlesDepth => 5000;
}
