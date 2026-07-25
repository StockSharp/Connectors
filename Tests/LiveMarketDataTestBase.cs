namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;

/// <summary>
/// Live market data checks for a connector whose public feed needs no credentials.
/// </summary>
/// <remarks>
/// Every check opens its own session against the real venue, so the whole suite is opt in:
/// set <c>STOCKSHARP_LIVE_TESTS=1</c> to run it. A derived class only has to build the
/// adapter and name an instrument that is liquid enough to produce data quickly.
/// </remarks>
[TestCategory("Integration")]
public abstract class LiveMarketDataTestBase : BaseTestClass
{
	private const string _liveVar = "STOCKSHARP_LIVE_TESTS";

	/// <inheritdoc />
	protected override bool SkipInCI => true;

	/// <inheritdoc />
	protected override string SkipInCIReason => "Live market data tests are not run in CI.";

	/// <summary>
	/// Create the adapter under test. Credentials must not be required.
	/// </summary>
	/// <returns><see cref="MessageAdapter"/></returns>
	protected abstract MessageAdapter CreateAdapter();

	/// <summary>
	/// Instrument used by the subscription checks.
	/// </summary>
	protected abstract SecurityId TestSecurityId { get; }

	/// <summary>
	/// Maximum time to establish the session.
	/// </summary>
	protected virtual TimeSpan ConnectTimeout => TimeSpan.FromSeconds(30);

	/// <summary>
	/// Maximum time to wait for the first portion of market data.
	/// </summary>
	protected virtual TimeSpan DataTimeout => TimeSpan.FromSeconds(30);

	/// <summary>
	/// How many candles back the candle check asks for.
	/// </summary>
	/// <remarks>
	/// Expressed in candles rather than in time, because venues cap a history request by the
	/// number of records it would return, not by its span.
	/// </remarks>
	protected virtual int CandlesDepth => 100;

	/// <summary>
	/// Securities the lookup check downloads before it stops.
	/// </summary>
	protected virtual int SecuritiesLimit => 5000;

	/// <summary>
	/// Data types to leave out even though the adapter reports them as supported, because the
	/// public feed of that venue does not actually deliver them.
	/// </summary>
	protected virtual DataType[] SkippedDataTypes => [];

	private MarketDataTestHarness CreateHarness()
	{
		var adapter = CreateAdapter();

		// the public feed is the whole point of these checks, transactions would demand credentials
		adapter.RemoveTransactionalSupport();

		return new(adapter);
	}

	private async Task RunAsync(Func<MarketDataTestHarness, Task> body)
	{
		if (!Env(_liveVar).EqualsIgnoreCase("1"))
			Inconclusive($"Set {_liveVar}=1 to run live market data tests.");

		var harness = CreateHarness();

		await using (harness)
		{
			await harness.ConnectAsync(ConnectTimeout, CancellationToken);

			try
			{
				await body(harness);
			}
			finally
			{
				await harness.DisconnectAsync(ConnectTimeout, CancellationToken);
			}
		}
	}

	private async Task<DataType[]> GetSupportedAsync(MarketDataTestHarness harness)
	{
		var supported = new List<DataType>();

		await foreach (var dataType in harness.Adapter.GetSupportedMarketDataTypesAsync(TestSecurityId, null, null).WithCancellation(CancellationToken))
		{
			if (!SkippedDataTypes.Contains(dataType))
				supported.Add(dataType);
		}

		return [.. supported];
	}

	private async Task<bool> IsSupportedAsync(MarketDataTestHarness harness, DataType dataType)
		=> (await GetSupportedAsync(harness)).Contains(dataType);

	private async Task<TimeSpan?> GetTimeFrameAsync(MarketDataTestHarness harness)
	{
		// listing every single frame would be noise, so the frameless marker skips them all
		if (SkippedDataTypes.Contains(DataType.CandleTimeFrame))
			return null;

		var frames = (await GetSupportedAsync(harness))
			.Where(dt => dt.IsTFCandles && dt.Arg is TimeSpan tf && tf > TimeSpan.Zero)
			.Select(dt => (TimeSpan)dt.Arg)
			.ToArray();

		if (frames.Length == 0)
			return null;

		// the shortest frame keeps the history request small, one minute is the usual sweet spot
		return frames.Contains(TimeSpan.FromMinutes(1))
			? TimeSpan.FromMinutes(1)
			: frames.Min();
	}

	private void CheckData<TMessage>(MarketDataResult<TMessage> result, string what)
		where TMessage : Message
	{
		if (result.IsNotSupported)
			Inconclusive($"{what} is not supported by the adapter.");

		if (result.Error is not null)
			throw result.Error;

		if (result.Data is null)
			Fail($"No {what} received in {DataTimeout}.");
	}

	[TestMethod]
	[Timeout(120000)]
	public Task Securities() => RunAsync(async harness =>
	{
		var result = await harness.LookupSecuritiesAsync(SecuritiesLimit, DataTimeout, CancellationToken);

		IsGreater(result.Securities.Length, 0, "No securities received.");

		foreach (var security in result.Securities)
		{
			IsFalse(security.SecurityId.SecurityCode.IsEmpty(), "Security code is empty.");
			IsFalse(security.SecurityId.BoardCode.IsEmpty(), "Board code is empty.");
		}

		// the instrument the subscription checks use must be one the adapter itself reports,
		// otherwise those checks would silently test a code the venue does not know
		if (result.IsComplete)
		{
			IsTrue(result.Securities.Any(s => s.SecurityId == TestSecurityId),
				$"{TestSecurityId} is not among the {result.Securities.Length} securities reported by the adapter.");
		}
	});

	[TestMethod]
	[Timeout(120000)]
	public Task Level1() => RunAsync(async harness =>
	{
		if (!await IsSupportedAsync(harness, DataType.Level1))
			Inconclusive("Level1 is not supported by the adapter.");

		var result = await harness.SubscribeAsync<Level1ChangeMessage>(
			DataType.Level1, TestSecurityId, null,
			m => m.Changes.Count > 0,
			DataTimeout, CancellationToken);

		CheckData(result, "level1");
	});

	[TestMethod]
	[Timeout(120000)]
	public Task OrderBook() => RunAsync(async harness =>
	{
		if (!await IsSupportedAsync(harness, DataType.MarketDepth))
			Inconclusive("Order book is not supported by the adapter.");

		var result = await harness.SubscribeAsync<QuoteChangeMessage>(
			DataType.MarketDepth, TestSecurityId, null,
			m => m.Bids.Length > 0 || m.Asks.Length > 0,
			DataTimeout, CancellationToken);

		CheckData(result, "order book");

		var book = result.Data;

		if (book.Bids.Length > 0 && book.Asks.Length > 0 && book.State is null)
			IsTrue(book.Bids[0].Price <= book.Asks[0].Price, $"Crossed book: {book.Bids[0].Price} > {book.Asks[0].Price}.");
	});

	[TestMethod]
	[Timeout(120000)]
	public Task Ticks() => RunAsync(async harness =>
	{
		if (!await IsSupportedAsync(harness, DataType.Ticks))
			Inconclusive("Ticks are not supported by the adapter.");

		var result = await harness.SubscribeAsync<ExecutionMessage>(
			DataType.Ticks, TestSecurityId, null,
			m => m.DataType == DataType.Ticks && m.TradePrice is not null,
			DataTimeout, CancellationToken);

		CheckData(result, "ticks");

		IsGreater(result.Data.TradePrice.Value, 0m, "Tick price is not positive.");
	});

	[TestMethod]
	[Timeout(120000)]
	public Task Candles() => RunAsync(async harness =>
	{
		var tf = await GetTimeFrameAsync(harness);

		if (tf is null)
			Inconclusive("Time frame candles are not supported by the adapter.");

		var from = DateTime.UtcNow - tf.Value.Multiply(CandlesDepth);

		var result = await harness.SubscribeAsync<TimeFrameCandleMessage>(
			tf.Value.TimeFrame(), TestSecurityId,
			mdMsg => mdMsg.From = from,
			null,
			DataTimeout, CancellationToken);

		CheckData(result, "candles");

		var candle = result.Data;

		IsGreater(candle.HighPrice, 0m, "Candle high is not positive.");
		IsTrue(candle.HighPrice >= candle.LowPrice, $"Candle high {candle.HighPrice} is below low {candle.LowPrice}.");
		IsTrue(candle.HighPrice >= candle.OpenPrice && candle.OpenPrice >= candle.LowPrice, "Candle open is outside the high/low range.");
		IsTrue(candle.HighPrice >= candle.ClosePrice && candle.ClosePrice >= candle.LowPrice, "Candle close is outside the high/low range.");
	});
}
