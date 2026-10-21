namespace StockSharp.Connectors.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Ecng.Common;
using Ecng.Logging;
using Ecng.UnitTesting;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using StockSharp.Algo;
using StockSharp.Algo.Storages;
using StockSharp.Messages;

/// <summary>
/// Downloads history through a connector and checks what comes back.
/// </summary>
/// <remarks>
/// A check that talks to the venue is marked Integration and starts with <see cref="SkipUnlessLive"/>,
/// so it reports itself inconclusive unless STOCKSHARP_LIVE_TESTS=1 is set.
/// </remarks>
public abstract class HistoryTestBase : BaseTestClass
{
	/// <summary>
	/// Requires venue access before a region-restricted history check starts.
	/// </summary>
	[TestInitialize]
	public void InitializeHistoryAccess()
	{
		if (LiveTestAccess.GetRegionalSkipReason(GetType(), TestContext?.TestName,
			Env(LiveTestAccess.RegionalOptInVariable)) is string reason)
			Inconclusive(reason);
	}

	private static readonly LogManager _logManager = new(false)
	{
		Listeners =
		{
			new DebugLogListener { Filters = { LoggingHelper.OnlyWarning, LoggingHelper.OnlyError } },
		},
	};

	/// <summary>
	/// The previous calendar year.
	/// </summary>
	protected static readonly int PrevYear = DateTime.UtcNow.Year - 1;

	/// <summary>
	/// Lookup criteria that ask for every security.
	/// </summary>
	protected static SecurityLookupMessage LookupAll => Extensions.LookupAllCriteriaMessage;

	/// <summary>
	/// Reports the check inconclusive unless live checks are enabled with STOCKSHARP_LIVE_TESTS=1.
	/// </summary>
	protected static void SkipUnlessLive()
	{
		if (LiveTestAccess.GetLiveSkipReason(Env("STOCKSHARP_LIVE_TESTS")) is string reason)
			Inconclusive(reason);
	}

	/// <summary>
	/// Creates the adapter and wraps it the way a connector hosts it.
	/// </summary>
	/// <typeparam name="T">Adapter type.</typeparam>
	/// <param name="init">Sets the adapter up before it is wrapped.</param>
	/// <returns>The wrapped adapter.</returns>
	protected static IMessageAdapter CreateAdapter<T>(Action<T> init = null)
		where T : IMessageAdapter
	{
		var adapter = typeof(T).CreateAdapter(new IncrementalIdGenerator());

		init?.Invoke((T)adapter);

		if (adapter.IsNativeIdentifiers)
			adapter = new SecurityNativeIdMessageAdapter(adapter, new InMemoryNativeIdStorageProvider());

		if (adapter.IsSupportSubscriptions)
			adapter = new SubscriptionMessageAdapter(adapter);

		if (adapter.IsSupportOrderBookIncrements)
			adapter = new OrderBookIncrementMessageAdapter(adapter);

		_logManager.Sources.Add(adapter);

		return adapter;
	}

	protected static DateTime ResetTimeOrder() => DateTime.MinValue;

	/// <summary>
	/// Checks a downloaded message: its security, its time within the range, its values and that the
	/// messages keep coming in time order.
	/// </summary>
	protected static void Validate<T>(T msg, SecurityId expected, DateTime from, DateTime to, ref DateTime lastTime)
		where T : ISecurityIdMessage, IServerTimeMessage
	{
		AreEqual(expected, msg.SecurityId);

		IsTrue(msg.ServerTime >= from, $"ServerTime {msg.ServerTime} < from {from}");
		IsTrue(msg.ServerTime <= to, $"ServerTime {msg.ServerTime} > to {to}");

		switch (msg)
		{
			case ExecutionMessage exec when exec.DataTypeEx == DataType.Ticks:
				ValidateTick(exec, ref lastTime);
				break;
			case TimeFrameCandleMessage candle:
				ValidateCandle(candle, ref lastTime);
				break;
			case QuoteChangeMessage quote:
				ValidateOrderBook(quote, ref lastTime);
				break;
			case Level1ChangeMessage level1:
				ValidateLevel1(level1, ref lastTime);
				break;
		}
	}

	private static void ValidateTick(ExecutionMessage msg, ref DateTime lastTime)
	{
		// No negative prices
		IsTrue(msg.TradePrice == null || msg.TradePrice > 0, $"Invalid TradePrice: {msg.TradePrice}");

		// No negative volumes
		IsTrue(msg.TradeVolume == null || msg.TradeVolume >= 0, $"Invalid TradeVolume: {msg.TradeVolume}");

		// Time ordering
		IsTrue(msg.ServerTime >= lastTime, $"Time out of order: {msg.ServerTime} < {lastTime}");
		lastTime = msg.ServerTime;
	}

	private static void ValidateCandle(TimeFrameCandleMessage msg, ref DateTime lastTime)
	{
		// No negative prices
		IsTrue(msg.OpenPrice > 0, $"Invalid OpenPrice: {msg.OpenPrice}");
		IsTrue(msg.HighPrice > 0, $"Invalid HighPrice: {msg.HighPrice}");
		IsTrue(msg.LowPrice > 0, $"Invalid LowPrice: {msg.LowPrice}");
		IsTrue(msg.ClosePrice > 0, $"Invalid ClosePrice: {msg.ClosePrice}");

		// No negative volumes
		IsTrue(msg.TotalVolume >= 0, $"Invalid TotalVolume: {msg.TotalVolume}");

		// OHLC consistency: High >= Low
		IsTrue(msg.HighPrice >= msg.LowPrice, $"High {msg.HighPrice} < Low {msg.LowPrice}");

		// High must be >= Open and Close
		IsTrue(msg.HighPrice >= msg.OpenPrice, $"High {msg.HighPrice} < Open {msg.OpenPrice}");
		IsTrue(msg.HighPrice >= msg.ClosePrice, $"High {msg.HighPrice} < Close {msg.ClosePrice}");

		// Low must be <= Open and Close
		IsTrue(msg.LowPrice <= msg.OpenPrice, $"Low {msg.LowPrice} > Open {msg.OpenPrice}");
		IsTrue(msg.LowPrice <= msg.ClosePrice, $"Low {msg.LowPrice} > Close {msg.ClosePrice}");

		// Time ordering
		IsTrue(msg.OpenTime >= lastTime, $"Time out of order: {msg.OpenTime} < {lastTime}");
		lastTime = msg.OpenTime;
	}

	private static void ValidateOrderBook(QuoteChangeMessage msg, ref DateTime lastTime)
	{
		// Validate bids
		decimal? lastBidPrice = null;
		foreach (var bid in msg.Bids)
		{
			IsTrue(bid.Price > 0, $"Invalid bid price: {bid.Price}");
			IsTrue(bid.Volume >= 0, $"Invalid bid volume: {bid.Volume}");

			// Bids should be sorted descending
			if (lastBidPrice != null)
				IsTrue(bid.Price <= lastBidPrice, $"Bids not sorted: {bid.Price} > {lastBidPrice}");
			lastBidPrice = bid.Price;
		}

		// Validate asks
		decimal? lastAskPrice = null;
		foreach (var ask in msg.Asks)
		{
			IsTrue(ask.Price > 0, $"Invalid ask price: {ask.Price}");
			IsTrue(ask.Volume >= 0, $"Invalid ask volume: {ask.Volume}");

			// Asks should be sorted ascending
			if (lastAskPrice != null)
				IsTrue(ask.Price >= lastAskPrice, $"Asks not sorted: {ask.Price} < {lastAskPrice}");
			lastAskPrice = ask.Price;
		}

		// No bid/ask crossing (best bid < best ask)
		var bestBid = msg.Bids.FirstOrDefault().Price;
		var bestAsk = msg.Asks.FirstOrDefault().Price;
		if (bestBid > 0 && bestAsk > 0)
			IsTrue(bestBid < bestAsk, $"Bid/Ask crossed: bid {bestBid} >= ask {bestAsk}");

		// Time ordering
		IsTrue(msg.ServerTime >= lastTime, $"Time out of order: {msg.ServerTime} < {lastTime}");
		lastTime = msg.ServerTime;
	}

	private static void ValidateLevel1(Level1ChangeMessage msg, ref DateTime lastTime)
	{
		// Validate prices are positive
		if (msg.Changes.TryGetValue(Level1Fields.BestBidPrice, out var bidObj) && bidObj is decimal bidPrice)
			IsTrue(bidPrice > 0, $"Invalid BestBidPrice: {bidPrice}");

		if (msg.Changes.TryGetValue(Level1Fields.BestAskPrice, out var askObj) && askObj is decimal askPrice)
			IsTrue(askPrice > 0, $"Invalid BestAskPrice: {askPrice}");

		if (msg.Changes.TryGetValue(Level1Fields.LastTradePrice, out var lastObj) && lastObj is decimal lastPrice)
			IsTrue(lastPrice > 0, $"Invalid LastTradePrice: {lastPrice}");

		// No bid/ask crossing
		if (msg.Changes.TryGetValue(Level1Fields.BestBidPrice, out var b) && b is decimal bid &&
			msg.Changes.TryGetValue(Level1Fields.BestAskPrice, out var a) && a is decimal ask)
			IsTrue(bid < ask, $"Bid/Ask crossed: bid {bid} >= ask {ask}");

		// Time ordering
		IsTrue(msg.ServerTime >= lastTime, $"Time out of order: {msg.ServerTime} < {lastTime}");
		lastTime = msg.ServerTime;
	}

	protected static IAsyncEnumerable<T> Download<T>(IMessageAdapter adapter, ISubscriptionMessage subscription)
		where T : Message
	{
		return adapter.ConnectAndDownloadAsync<T>(subscription);
	}

	protected static IAsyncEnumerable<Level1ChangeMessage> DownloadLevel1(IMessageAdapter adapter, SecurityId securityId, DateTime beginDate, DateTime endDate, int? maxCount = default, IEnumerable<Level1Fields> fields = default, SecurityTypes? secType = default)
	{
		var mdMsg = new MarketDataMessage
		{
			SecurityId = securityId,
			IsSubscribe = true,
			DataType2 = DataType.Level1,
			From = beginDate,
			To = endDate,
			Fields = fields,
			SecurityType = secType,
			Count = maxCount,
		};

		return Download<Level1ChangeMessage>(adapter, mdMsg);
	}

	protected static IAsyncEnumerable<ExecutionMessage> DownloadTicks(IMessageAdapter adapter, SecurityId securityId, DateTime beginDate, DateTime endDate, int? maxCount = default, SecurityTypes? secType = default)
	{
		var mdMsg = new MarketDataMessage
		{
			SecurityId = securityId,
			IsSubscribe = true,
			DataType2 = DataType.Ticks,
			From = beginDate,
			To = endDate,
			SecurityType = secType,
			Count = maxCount,
		};

		return Download<ExecutionMessage>(adapter, mdMsg);
	}

	protected static IAsyncEnumerable<QuoteChangeMessage> DownloadOrderBook(IMessageAdapter adapter, SecurityId securityId, DateTime beginDate, DateTime endDate, int? maxCount = default, SecurityTypes? secType = default)
	{
		var mdMsg = new MarketDataMessage
		{
			SecurityId = securityId,
			IsSubscribe = true,
			DataType2 = DataType.MarketDepth,
			From = beginDate,
			To = endDate,
			SecurityType = secType,
			Count = maxCount,
		};

		return Download<QuoteChangeMessage>(adapter, mdMsg);
	}

	protected static IAsyncEnumerable<SecurityMessage> DownloadSecurities(IMessageAdapter adapter, SecurityLookupMessage lookupMsg)
	{
		return Download<SecurityMessage>(adapter, lookupMsg);
	}

	protected static IAsyncEnumerable<TimeFrameCandleMessage> DownloadCandles(IMessageAdapter adapter, SecurityId securityId, TimeSpan timeFrame, DateTime from, DateTime to, long? count = null, Level1Fields? buildField = null, SecurityTypes? secType = default)
	{
		var mdMsg = new MarketDataMessage
		{
			SecurityId = securityId,
			IsSubscribe = true,
			DataType2 = timeFrame.TimeFrame(),
			From = from,
			To = to,
			Count = count,
			BuildField = buildField,
			SecurityType = secType,
		};

		return Download<TimeFrameCandleMessage>(adapter, mdMsg);
	}

	/// <summary>
	/// Downloads candles of every time frame from <paramref name="counts"/>, each through a fresh
	/// adapter, and checks how many finished candles each one produced.
	/// </summary>
	protected static Task DownloadCandlesMultiTf<TAdapter>(SecurityId secId, SecurityTypes? secType, DateTime from, Dictionary<TimeSpan, int> counts, CancellationToken cancellationToken)
		where TAdapter : IMessageAdapter
	{
		return DownloadCandlesMultiTf(() => CreateAdapter<TAdapter>(), secId, secType, from, counts, cancellationToken);
	}

	/// <summary>
	/// Downloads candles of every time frame from <paramref name="counts"/>, each through a fresh
	/// adapter, and checks how many finished candles each one produced.
	/// </summary>
	protected static async Task DownloadCandlesMultiTf(Func<IMessageAdapter> createAdapter, SecurityId secId, SecurityTypes? secType, DateTime from, Dictionary<TimeSpan, int> counts, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(createAdapter);
		ArgumentNullException.ThrowIfNull(counts);

		static TimeSpan GetTo(TimeSpan tf)
		{
			if (tf.TotalMinutes <= 1)
				return TimeSpan.FromDays(1);
			else if (tf.TotalHours <= 1)
				return TimeSpan.FromDays(3);
			else if (tf.TotalDays <= 1)
				return TimeSpan.FromDays(7);
			else if (tf.TotalDays <= 7)
				return TimeSpan.FromDays(30);
			else
				return TimeSpan.FromDays(60);
		}

		var errors = new List<Exception>();

		foreach (var (timeFrame, expectedCount) in counts)
		{
			var to = (from + GetTo(timeFrame)).AddTicks(-1);
			var lastTime = ResetTimeOrder();

			var adapter = createAdapter();

			var counter = 0;
			await foreach (var msg in DownloadCandles(adapter, secId, timeFrame, from, to, secType: secType).WithCancellation(cancellationToken))
			{
				var dt = timeFrame.TimeFrame();
				AreEqual(dt, msg.DataType);
				Validate(msg, secId, from.AddDays(-1) - timeFrame, to.AddDays(1) + timeFrame, ref lastTime);

				if (msg.State == CandleStates.Finished)
					counter++;
			}

			try
			{
				AreEqual(expectedCount, counter);
			}
			catch (Exception ex)
			{
				errors.Add(new InvalidOperationException($"For tf {timeFrame}", ex));
			}

			await adapter.IterationInterval.Delay(cancellationToken);
		}

		if (errors.Count > 0)
			throw new AggregateException(errors);
	}
}
