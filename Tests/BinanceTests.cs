namespace StockSharp.Connectors.Tests;

using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Binance;
using StockSharp.Binance.Native;
using StockSharp.Messages;

/// <summary>
/// Tests for the Binance connector.
/// </summary>
[TestClass]
[RegionalLiveTests]
public class BinanceTests : LiveMarketDataTestBase
{
	// A book is built from a snapshot and the numbered updates that follow it. An update that does not start where
	// the book ends means changes were lost - most of them removals of levels, which then stay in the book for
	// good - so the book is built anew rather than carried on.
	private static DepthSequence Spot(long snapshot)
	{
		var sequence = new DepthSequence(false);
		sequence.Reset(snapshot);
		return sequence;
	}

	private static DepthSequence Futures(long snapshot)
	{
		var sequence = new DepthSequence(true);
		sequence.Reset(snapshot);
		return sequence;
	}

	[TestMethod]
	public void Spot_AnUpdateThatStartsWhereTheBookEnds_IsApplied()
	{
		var sequence = Spot(100);

		AreEqual(true, sequence.TryAdvance(101, 105, null));
		AreEqual(105, sequence.LastUpdateId);
		AreEqual(true, sequence.TryAdvance(106, 106, null));
		AreEqual(106, sequence.LastUpdateId);
	}

	// The snapshot is taken while updates flow: the one in flight starts before it and ends after it, and carries
	// the changes made since.
	[TestMethod]
	public void Spot_TheUpdateTheSnapshotWasTakenInTheMiddleOf_IsApplied()
	{
		var sequence = Spot(100);

		AreEqual(true, sequence.TryAdvance(98, 103, null));
		AreEqual(103, sequence.LastUpdateId);
		AreEqual(true, sequence.TryAdvance(104, 110, null));
	}

	[TestMethod]
	public void Spot_AnUpdateTheBookAlreadyHolds_IsDropped()
	{
		var sequence = Spot(100);

		IsNull(sequence.TryAdvance(90, 100, null));
		IsNull(sequence.TryAdvance(80, 85, null));
		AreEqual(100, sequence.LastUpdateId);
	}

	[TestMethod]
	public void Spot_AnUpdateThatSkipsChanges_IsAGap()
	{
		var sequence = Spot(100);

		AreEqual(false, sequence.TryAdvance(102, 105, null));
		AreEqual(100, sequence.LastUpdateId, "A gap must not move the book on: what follows it is not its continuation either.");
	}

	// What a dropped and restored socket looks like: the stream carries on from where the venue is, not from
	// where the book is.
	[TestMethod]
	public void Spot_UpdatesLostInTheMiddleOfTheStream_AreAGap()
	{
		var sequence = Spot(100);

		AreEqual(true, sequence.TryAdvance(101, 105, null));
		AreEqual(true, sequence.TryAdvance(106, 110, null));
		AreEqual(false, sequence.TryAdvance(250, 260, null));
		AreEqual(false, sequence.TryAdvance(261, 270, null));
	}

	[TestMethod]
	public void Spot_ANewSnapshot_StartsTheBookAgain()
	{
		var sequence = Spot(100);

		AreEqual(false, sequence.TryAdvance(250, 260, null));

		sequence.Reset(255);

		AreEqual(true, sequence.TryAdvance(250, 260, null));
		AreEqual(true, sequence.TryAdvance(261, 270, null));
	}

	// A futures update names the last change of the one before it, and the first one applied to a snapshot is
	// the one the snapshot was taken in the middle of.
	[TestMethod]
	public void Futures_TheUpdateTheSnapshotWasTakenInTheMiddleOf_IsAppliedFirst()
	{
		var sequence = Futures(100);

		AreEqual(true, sequence.TryAdvance(95, 104, 94));
		AreEqual(104, sequence.LastUpdateId);
		AreEqual(true, sequence.TryAdvance(105, 109, 104));
		AreEqual(109, sequence.LastUpdateId);
	}

	[TestMethod]
	public void Futures_AnUpdateThatEndsBeforeTheSnapshot_IsDropped()
	{
		var sequence = Futures(100);

		IsNull(sequence.TryAdvance(90, 99, 89));
		AreEqual(true, sequence.TryAdvance(100, 104, 99));
	}

	[TestMethod]
	public void Futures_AnUpdateThatStartsRightAfterTheSnapshot_IsApplied()
		=> AreEqual(true, Futures(100).TryAdvance(101, 104, 100));

	[TestMethod]
	public void Futures_ASnapshotOlderThanTheFirstUpdate_IsAGap()
		=> AreEqual(false, Futures(100).TryAdvance(105, 109, 104));

	[TestMethod]
	public void Futures_AnUpdateThatDoesNotNameTheOneBeforeIt_IsAGap()
	{
		var sequence = Futures(100);

		AreEqual(true, sequence.TryAdvance(95, 104, 94));
		AreEqual(false, sequence.TryAdvance(120, 125, 119));
		AreEqual(104, sequence.LastUpdateId);
	}

	[TestMethod]
	public void Futures_AnUpdateAlreadyApplied_IsDropped()
	{
		var sequence = Futures(100);

		AreEqual(true, sequence.TryAdvance(95, 104, 94));
		AreEqual(true, sequence.TryAdvance(105, 109, 104));
		IsNull(sequence.TryAdvance(105, 109, 104));
		IsNull(sequence.TryAdvance(95, 104, 94));
	}

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
	[TestCategory("Integration")]
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
