namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Binance;
using StockSharp.Messages;

/// <summary>
/// Binance publishes spot market data without credentials.
/// </summary>
[TestClass]
[RegionalLiveTests]
public class BinanceMarketDataTests : LiveMarketDataTestBase
{
	/// <inheritdoc />
	protected override MessageAdapter CreateAdapter() => new BinanceMessageAdapter(new IncrementalIdGenerator())
	{
		Sections = [BinanceSections.Spot],
	};

	/// <inheritdoc />
	protected override SecurityId TestSecurityId => new()
	{
		SecurityCode = "BTCUSDT",
		BoardCode = BoardCodes.Binance,
	};

	// Past twenty pairs a kind of stream is no longer opened pair by pair, and the day's figures of a pair (open,
	// high, low, change, volume) come only from its ticker.
	[TestMethod]
	[Timeout(120000, CooperativeCancellation = true)]
	public Task Level1_ManyPairs_CarryTheDayFigures() => RunAsync(async harness =>
	{
		string[] codes =
		[
			"BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT", "DOGEUSDT", "ADAUSDT", "TRXUSDT", "LINKUSDT", "LTCUSDT",
			"AVAXUSDT", "DOTUSDT", "BCHUSDT", "XLMUSDT", "ATOMUSDT", "UNIUSDT", "NEARUSDT", "FILUSDT", "ETCUSDT", "APTUSDT",
			"SUIUSDT",
		];

		var reader = harness.CreateReader();

		foreach (var code in codes)
		{
			harness.Post(new MarketDataMessage
			{
				TransactionId = harness.Adapter.TransactionIdGenerator.GetNextId(),
				IsSubscribe = true,
				SecurityId = new() { SecurityCode = code, BoardCode = BoardCodes.Binance },
				DataType2 = DataType.Level1,
			}, CancellationToken);
		}

		var figures = await reader.TryWaitAsync<Level1ChangeMessage>(
			m => m.SecurityId == TestSecurityId && m.Changes.ContainsKey(Level1Fields.OpenPrice),
			DataTimeout, CancellationToken);

		IsNotNull(figures, $"No day figures of {TestSecurityId} in {DataTimeout} with level1 asked for {codes.Length} pairs.");
	});
}
