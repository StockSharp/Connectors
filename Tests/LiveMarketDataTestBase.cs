namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

using Ecng.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Messages;

/// <summary>
/// Live market data checks for a connector with the access required by its feed.
/// </summary>
/// <remarks>
/// Every check opens its own session against the real venue. A derived class only has to
/// build the adapter and name an instrument that is liquid enough to produce data quickly.
/// </remarks>
public abstract class LiveMarketDataTestBase : HistoryTestBase
{
	/// <summary>
	/// Create the adapter under test with any credentials required by its market data feed.
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
	/// Maximum time to download the security list.
	/// </summary>
	protected virtual TimeSpan SecuritiesTimeout => TimeSpan.FromSeconds(60);

	/// <summary>
	/// How many candles back the candle check asks for.
	/// </summary>
	/// <remarks>
	/// Expressed in candles rather than in time, because venues cap a history request by the
	/// number of records it would return, not by its span.
	/// </remarks>
	protected virtual int CandlesDepth => 100;

	/// <summary>
	/// Preferred candle interval supported by the public data plan.
	/// </summary>
	protected virtual TimeSpan CandleTimeFrame => TimeSpan.FromMinutes(1);

	/// <summary>
	/// Securities the lookup check downloads before it stops.
	/// </summary>
	protected virtual int SecuritiesLimit => 5000;

	/// <summary>
	/// Criteria for feeds that require a symbol or reference-data filter.
	/// </summary>
	protected virtual SecurityLookupMessage SecuritiesLookup => new();

	/// <summary>
	/// Data types to leave out even though the adapter reports them as supported, because the
	/// public feed of that venue does not actually deliver them.
	/// </summary>
	protected virtual DataType[] SkippedDataTypes => [];

	/// <summary>
	/// Sets venue-specific bounds on a market data request.
	/// </summary>
	/// <param name="message">Subscription to configure.</param>
	protected virtual void ConfigureSubscription(MarketDataMessage message)
	{
	}

	private MarketDataTestHarness CreateHarness()
	{
		var adapter = CreateAdapter();

		// These checks exercise market data without sending account or trading requests.
		adapter.RemoveTransactionalSupport();

		return new(adapter);
	}

	/// <summary>
	/// Runs a check against a session of its own with the venue.
	/// </summary>
	/// <param name="body">The check.</param>
	/// <returns>A task that completes when the check is done.</returns>
	private protected async Task RunAsync(Func<MarketDataTestHarness, Task> body)
	{
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

		// Prefer the interval allowed by this feed's public plan, otherwise use its shortest frame.
		return frames.Contains(CandleTimeFrame)
			? CandleTimeFrame
			: frames.Min();
	}

	/// <summary>
	/// Fails a declared subscription when it is rejected or returns no data.
	/// </summary>
	/// <typeparam name="TMessage">What was asked for.</typeparam>
	/// <param name="result">What came back.</param>
	/// <param name="what">What was asked for, in words.</param>
	internal void CheckData<TMessage>(MarketDataResult<TMessage> result, string what)
		where TMessage : Message
	{
		if (result.Error is not null)
			ExceptionDispatchInfo.Throw(result.Error);

		if (result.Data is null)
			Fail($"{GetType().Name}: No {what} received in {DataTimeout}.");
	}

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public Task Securities() => RunAsync(async harness =>
	{
		var result = await harness.LookupSecuritiesAsync(SecuritiesLookup, SecuritiesLimit,
			SecuritiesTimeout, CancellationToken);

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
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public Task Level1() => RunAsync(async harness =>
	{
		if (!await IsSupportedAsync(harness, DataType.Level1))
			Inconclusive("Level1 is not supported by the adapter.");

		var result = await harness.SubscribeAsync<Level1ChangeMessage>(
			DataType.Level1, TestSecurityId, ConfigureSubscription,
			m => m.Changes.Count > 0,
			DataTimeout, CancellationToken);

		CheckData(result, "level1");
	});

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public Task OrderBook() => RunAsync(async harness =>
	{
		if (!await IsSupportedAsync(harness, DataType.MarketDepth))
			Inconclusive("Order book is not supported by the adapter.");

		var result = await harness.SubscribeAsync<QuoteChangeMessage>(
			DataType.MarketDepth, TestSecurityId, ConfigureSubscription,
			m => m.Bids.Length > 0 || m.Asks.Length > 0,
			DataTimeout, CancellationToken);

		CheckData(result, "order book");

		var book = result.Data;

		if (book.Bids.Length > 0 && book.Asks.Length > 0 && book.State is null)
			IsTrue(book.Bids[0].Price <= book.Asks[0].Price, $"Crossed book: {book.Bids[0].Price} > {book.Asks[0].Price}.");
	});

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public Task Ticks() => RunAsync(async harness =>
	{
		if (!await IsSupportedAsync(harness, DataType.Ticks))
			Inconclusive("Ticks are not supported by the adapter.");

		var result = await harness.SubscribeAsync<ExecutionMessage>(
			DataType.Ticks, TestSecurityId, ConfigureSubscription,
			m => m.DataType == DataType.Ticks && m.TradePrice is not null,
			DataTimeout, CancellationToken);

		CheckData(result, "ticks");

		IsGreater(result.Data.TradePrice.Value, 0m, "Tick price is not positive.");
	});

	[TestMethod]
	[TestCategory("Integration")]
	[Timeout(120000, CooperativeCancellation = true)]
	public Task Candles() => RunAsync(async harness =>
	{
		var tf = await GetTimeFrameAsync(harness);

		if (tf is null)
			Inconclusive("Time frame candles are not supported by the adapter.");

		var to = DateTime.UtcNow;
		var from = to - tf.Value.Multiply(CandlesDepth);

		var result = await harness.SubscribeAsync<TimeFrameCandleMessage>(
			tf.Value.TimeFrame(), TestSecurityId,
			mdMsg =>
			{
				mdMsg.From = from;
				mdMsg.To = to;
				ConfigureSubscription(mdMsg);
			},
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
