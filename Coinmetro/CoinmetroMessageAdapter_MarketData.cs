namespace StockSharp.Coinmetro;

public partial class CoinmetroMessageAdapter
{
	private static readonly uint[] _crc32Table = CreateCrc32Table();

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId, cancellationToken);

		EnsureConnected();
		var securityTypes = lookupMsg.GetSecurityTypes();
		var requested = lookupMsg.SecurityId.SecurityCode;
		var skip = Math.Max(0, lookupMsg.Skip ?? 0);
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var market in GetMarkets().OrderBy(
			static value => value.SecurityCode,
			StringComparer.OrdinalIgnoreCase))
		{
			if (!lookupMsg.SecurityId.BoardCode.IsEmpty() &&
				!lookupMsg.SecurityId.BoardCode.EqualsIgnoreCase(
					BoardCodes.Coinmetro))
				continue;
			if (!requested.IsEmpty() &&
				!requested.EqualsIgnoreCase(market.SecurityCode) &&
				!requested.EqualsIgnoreCase(market.Pair))
				continue;
			var security = CreateSecurity(
				market, lookupMsg.TransactionId);
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;
			if (skip-- > 0)
				continue;
			await SendOutMessageAsync(security, cancellationToken);
			await SendOutMessageAsync(
				new Level1ChangeMessage
				{
					SecurityId = security.SecurityId,
					ServerTime = CurrentTime,
					OriginalTransactionId =
						lookupMsg.TransactionId,
				}.TryAdd(
					Level1Fields.State,
					SecurityStates.Trading),
				cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(
			lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			MarketSubscription subscription;
			var unsubscribe = false;
			using (_sync.EnterScope())
			{
				if (!_level1Subscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out subscription))
					return;
				unsubscribe = --_tickReferences == 0;
			}
			if (unsubscribe)
				await WsClient.UnsubscribeTicksAsync(
					cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Coinmetro does not expose historical Level1 " +
					"events.");

		var market = GetMarket(mdMsg.SecurityId);
		var ticker = GetTicker(market.Pair);
		if (ticker is null)
		{
			var tickers = await RestClient.GetTickersAsync(
				cancellationToken);
			UpdateTickers(tickers);
			ticker = tickers.FirstOrDefault(item =>
				item.Pair.EqualsIgnoreCase(market.Pair));
		}
		await SendLevel1Async(
			market,
			ticker,
			mdMsg.TransactionId,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var subscribe = false;
		using (_sync.EnterScope())
		{
			_level1Subscriptions[mdMsg.TransactionId] = new()
			{
				Pair = market.Pair,
				SecurityCode = market.SecurityCode,
			};
			subscribe = _tickReferences++ == 0;
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeTicksAsync(
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_level1Subscriptions.Remove(mdMsg.TransactionId);
				_tickReferences--;
			}
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			DepthSubscription subscription;
			using (_sync.EnterScope())
			{
				if (!_depthSubscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out subscription))
					return;
			}
			if (ReleaseBookReference(subscription.Pair))
				await WsClient.UnsubscribeBookAsync(
					subscription.Pair, cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}
		if (mdMsg.From is not null)
			throw new NotSupportedException(
				"Coinmetro does not expose historical order books.");

		var market = GetMarket(mdMsg.SecurityId);
		var maximumDepth = (mdMsg.MaxDepth ?? 100)
			.Max(1).Min(500).To<int>();
		var book = await RestClient.GetBookAsync(
			market.Pair, cancellationToken);
		UpdateBook(market.Pair, book);
		await SendBookAsync(
			market,
			book,
			mdMsg.TransactionId,
			maximumDepth,
			cancellationToken);
		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		using (_sync.EnterScope())
			_depthSubscriptions[mdMsg.TransactionId] = new()
			{
				Pair = market.Pair,
				SecurityCode = market.SecurityCode,
				Depth = maximumDepth,
			};
		var subscribe = AddBookReference(market.Pair);
		try
		{
			if (subscribe)
				await WsClient.SubscribeBookAsync(
					market.Pair, cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_depthSubscriptions.Remove(mdMsg.TransactionId);
			ReleaseBookReference(market.Pair);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
		{
			MarketSubscription subscription;
			var unsubscribe = false;
			using (_sync.EnterScope())
			{
				if (!_tickSubscriptions.Remove(
					mdMsg.OriginalTransactionId,
					out subscription))
					return;
				unsubscribe = --_tickReferences == 0;
			}
			if (unsubscribe)
				await WsClient.UnsubscribeTicksAsync(
					cancellationToken);
			return;
		}
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var maximum = (mdMsg.Count ?? 1000)
			.Max(1).Min(10000).To<int>();

		foreach (var trade in (await RestClient.GetTradesAsync(
			market.Pair,
			mdMsg.From,
			cancellationToken) ?? [])
			.Where(trade =>
				(mdMsg.From is null ||
					trade.Time >=
						mdMsg.From.Value.ToUniversalTime()) &&
				trade.Time <= to)
			.OrderBy(static trade => trade.Time)
			.TakeLast(maximum))
		{
			if (!AddTrade(market.Pair, trade.Id))
				continue;
			await SendTradeAsync(
				market,
				trade,
				mdMsg.TransactionId,
				cancellationToken);
		}

		if (mdMsg.IsHistoryOnly())
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var subscribe = false;
		using (_sync.EnterScope())
		{
			_tickSubscriptions[mdMsg.TransactionId] = new()
			{
				Pair = market.Pair,
				SecurityCode = market.SecurityCode,
			};
			subscribe = _tickReferences++ == 0;
		}
		try
		{
			if (subscribe)
				await WsClient.SubscribeTicksAsync(
					cancellationToken);
			await SendSubscriptionResultAsync(
				mdMsg, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				_tickSubscriptions.Remove(mdMsg.TransactionId);
				_tickReferences--;
			}
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId, cancellationToken);

		EnsureConnected();
		if (!mdMsg.IsSubscribe)
			return;
		if (mdMsg.Count is <= 0)
		{
			await CompleteMarketSubscriptionAsync(
				mdMsg, cancellationToken);
			return;
		}

		var market = GetMarket(mdMsg.SecurityId);
		var timeFrame = mdMsg.GetTimeFrame();
		if (!AllTimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Coinmetro does not support the {timeFrame} " +
					"candle time frame.");
		var to = (mdMsg.To ?? DateTime.UtcNow).ToUniversalTime();
		var from = (mdMsg.From ??
			to - timeFrame * (mdMsg.Count ?? 500))
			.ToUniversalTime();
		var maximum = (mdMsg.Count ?? 10000)
			.Max(1).Min(10000).To<int>();

		foreach (var candle in (await RestClient.GetCandlesAsync(
			market.Pair,
			timeFrame,
			from,
			to,
			cancellationToken) ?? [])
			.Where(candle =>
				candle.OpenTime >= from &&
				candle.OpenTime <= to)
			.OrderBy(static candle => candle.OpenTime)
			.TakeLast(maximum))
			await SendCandleAsync(
				market,
				candle,
				mdMsg.TransactionId,
				cancellationToken);

		await CompleteMarketSubscriptionAsync(
			mdMsg, cancellationToken);
	}

	private async ValueTask ProcessTickerAsync(
		CoinmetroTicker ticker,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(ticker?.Pair);
		if (market is null || ticker is null)
			return;
		UpdateTickers([ticker]);
		KeyValuePair<long, MarketSubscription>[] level1;
		KeyValuePair<long, MarketSubscription>[] ticks;
		using (_sync.EnterScope())
		{
			level1 = [.. _level1Subscriptions.Where(pair =>
				pair.Value.Pair.EqualsIgnoreCase(market.Pair))];
			ticks = [.. _tickSubscriptions.Where(pair =>
				pair.Value.Pair.EqualsIgnoreCase(market.Pair))];
		}

		foreach (var pair in level1)
			await SendLevel1Async(
				market,
				ticker,
				pair.Key,
				cancellationToken);

		if (ticker.Price <= 0 ||
			ticker.Volume <= 0 ||
			ticks.Length == 0)
			return;
		var trade = new CoinmetroTrade
		{
			Id = ticker.Sequence > 0
				? ticker.Sequence.ToString(
					CultureInfo.InvariantCulture)
				: $"{new DateTimeOffset(ticker.Time)
					.ToUnixTimeMilliseconds()}:" +
					$"{ticker.Price.ToWire()}:" +
					ticker.Volume.ToWire(),
			Pair = ticker.Pair,
			Time = ticker.Time,
			Price = ticker.Price,
			Volume = ticker.Volume,
		};
		if (!AddTrade(market.Pair, trade.Id))
			return;

		foreach (var pair in ticks)
			await SendTradeAsync(
				market,
				trade,
				pair.Key,
				cancellationToken);
	}

	private async ValueTask ProcessBookUpdateAsync(
		CoinmetroBookUpdate update,
		CancellationToken cancellationToken)
	{
		var market = GetMarket(update?.Pair);
		if (market is null || update is null)
			return;
		var stale = false;
		using (_sync.EnterScope())
			stale = _orderBooks.TryGetValue(
				market.Pair, out var state) &&
				update.Sequence > 0 &&
				state.Sequence >= update.Sequence;
		if (stale)
			return;

		var valid = ApplyBookUpdate(market, update);
		if (!valid)
		{
			var snapshot = await RestClient.GetBookAsync(
				market.Pair, cancellationToken);
			UpdateBook(market.Pair, snapshot);
		}
		var book = GetBook(market.Pair);
		KeyValuePair<long, DepthSubscription>[] subscriptions;
		using (_sync.EnterScope())
			subscriptions = [.. _depthSubscriptions.Where(pair =>
				pair.Value.Pair.EqualsIgnoreCase(market.Pair))];

		foreach (var pair in subscriptions)
			await SendBookAsync(
				market,
				book,
				pair.Key,
				pair.Value.Depth,
				cancellationToken);
	}

	private SecurityMessage CreateSecurity(
		CoinmetroMarket market,
		long originalTransactionId)
		=> new()
		{
			SecurityId = market.ToStockSharp(),
			Name = market.SecurityCode,
			ShortName = market.SecurityCode,
			SecurityType = SecurityTypes.CryptoCurrency,
			Currency = market.QuoteCurrency.ToCurrency(),
			PriceStep = market.PricePrecision.ToStep(),
			VolumeStep = market.AmountPrecision.ToStep(),
			MinVolume = market.MinimumAmount > 0
				? market.MinimumAmount
				: null,
			OriginalTransactionId = originalTransactionId,
		};

	private ValueTask SendLevel1Async(
		CoinmetroMarket market,
		CoinmetroTicker ticker,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (ticker is null)
			return default;
		return SendOutMessageAsync(
			new Level1ChangeMessage
			{
				SecurityId = market.ToStockSharp(),
				ServerTime = ticker.Time == default
					? CurrentTime
					: ticker.Time,
				OriginalTransactionId =
					originalTransactionId,
			}
			.TryAdd(
				Level1Fields.LastTradePrice, ticker.Price)
			.TryAdd(
				Level1Fields.LastTradeVolume, ticker.Volume)
			.TryAdd(
				Level1Fields.BestBidPrice, ticker.Bid)
			.TryAdd(
				Level1Fields.BestAskPrice, ticker.Ask)
			.TryAdd(
				Level1Fields.State, SecurityStates.Trading),
			cancellationToken);
	}

	private ValueTask SendBookAsync(
		CoinmetroMarket market,
		CoinmetroBook book,
		long originalTransactionId,
		int maximumDepth,
		CancellationToken cancellationToken)
	{
		if (book is null)
			return default;
		return SendOutMessageAsync(
			new QuoteChangeMessage
			{
				SecurityId = market.ToStockSharp(),
				ServerTime = CurrentTime,
				OriginalTransactionId =
					originalTransactionId,
				State = QuoteChangeStates.SnapshotComplete,
				SeqNum = book.Sequence,
				Bids = [.. book.Bids
					.Take(maximumDepth)
					.Select(static quote => new QuoteChange(
						quote.Price, quote.Volume))],
				Asks = [.. book.Asks
					.Take(maximumDepth)
					.Select(static quote => new QuoteChange(
						quote.Price, quote.Volume))],
			},
			cancellationToken);
	}

	private ValueTask SendTradeAsync(
		CoinmetroMarket market,
		CoinmetroTrade trade,
		long originalTransactionId,
		CancellationToken cancellationToken)
		=> SendOutMessageAsync(
			new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				SecurityId = market.ToStockSharp(),
				ServerTime = trade.Time,
				OriginalTransactionId =
					originalTransactionId,
				TradeStringId = trade.Id,
				TradePrice = trade.Price,
				TradeVolume = trade.Volume.Abs(),
				OriginSide = trade.Side,
			},
			cancellationToken);

	private ValueTask SendCandleAsync(
		CoinmetroMarket market,
		CoinmetroCandle candle,
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		var closeTime = candle.OpenTime + candle.TimeFrame;
		return SendOutMessageAsync(
			new TimeFrameCandleMessage
			{
				SecurityId = market.ToStockSharp(),
				TypedArg = candle.TimeFrame,
				OpenTime = candle.OpenTime,
				CloseTime = closeTime,
				OpenPrice = candle.Open,
				HighPrice = candle.High,
				LowPrice = candle.Low,
				ClosePrice = candle.Close,
				TotalVolume = candle.Volume,
				State = closeTime <= DateTime.UtcNow
					? CandleStates.Finished
					: CandleStates.Active,
				OriginalTransactionId =
					originalTransactionId,
			},
			cancellationToken);
	}

	private CoinmetroTicker GetTicker(string pair)
	{
		using (_sync.EnterScope())
			return _tickers.TryGetValue(pair, out var ticker)
				? ticker
				: null;
	}

	private void UpdateTickers(
		IEnumerable<CoinmetroTicker> tickers)
	{
		using (_sync.EnterScope())
		{
			foreach (var ticker in tickers ?? [])
			{
				if (ticker?.Pair.IsEmpty() == false)
					_tickers[ticker.Pair] = ticker;
			}
		}
	}

	private void UpdateBook(
		string pair,
		CoinmetroBook book)
	{
		using (_sync.EnterScope())
		{
			var state = new BookState
			{
				Sequence = book?.Sequence ?? 0,
			};

			foreach (var quote in book?.Bids ?? [])
				state.Bids[quote.Price] = quote.Volume;

			foreach (var quote in book?.Asks ?? [])
				state.Asks[quote.Price] = quote.Volume;

			_orderBooks[pair] = state;
		}
	}

	private bool ApplyBookUpdate(
		CoinmetroMarket market,
		CoinmetroBookUpdate update)
	{
		using (_sync.EnterScope())
		{
			if (!_orderBooks.TryGetValue(
				market.Pair, out var state))
				return false;

			foreach (var quote in update.Bids)
				ApplyQuoteDelta(
					state.Bids,
					quote,
					market.BookAmountPrecision);

			foreach (var quote in update.Asks)
				ApplyQuoteDelta(
					state.Asks,
					quote,
					market.BookAmountPrecision);

			if (update.Sequence > 0)
				state.Sequence = update.Sequence;
			return update.Checksum == 0 ||
				CalculateBookChecksum(state, market) ==
					update.Checksum;
		}
	}

	private CoinmetroBook GetBook(string pair)
	{
		using (_sync.EnterScope())
		{
			if (!_orderBooks.TryGetValue(pair, out var state))
				return null;
			return new()
			{
				Pair = pair,
				Sequence = state.Sequence,
				Bids = [.. state.Bids.Select(static item =>
					new CoinmetroQuote
					{
						Price = item.Key,
						Volume = item.Value,
					})],
				Asks = [.. state.Asks.Select(static item =>
					new CoinmetroQuote
					{
						Price = item.Key,
						Volume = item.Value,
					})],
			};
		}
	}

	private static void ApplyQuoteDelta(
		IDictionary<decimal, decimal> side,
		CoinmetroQuote quote,
		int precision)
	{
		side.TryGetValue(quote.Price, out var current);
		var volume = Math.Round(
			current + quote.Volume,
			precision.Max(0).Min(28),
			MidpointRounding.AwayFromZero);
		if (volume > 0)
			side[quote.Price] = volume;
		else
			side.Remove(quote.Price);
	}

	private static int CalculateBookChecksum(
		BookState state,
		CoinmetroMarket market)
	{
		var value = new StringBuilder();

		foreach (var item in state.Asks.OrderBy(
			static item => item.Key.ToString(
				CultureInfo.InvariantCulture),
			StringComparer.Ordinal))
			AppendChecksumLevel(value, item, market);

		foreach (var item in state.Bids.OrderBy(
			static item => item.Key.ToString(
				CultureInfo.InvariantCulture),
			StringComparer.Ordinal))
			AppendChecksumLevel(value, item, market);

		var checksum = uint.MaxValue;

		foreach (var item in Encoding.UTF8.GetBytes(
			value.ToString()))
			checksum = _crc32Table[(checksum ^ item) & 0xff] ^
				checksum >> 8;

		return unchecked((int)(checksum ^ uint.MaxValue));
	}

	private static void AppendChecksumLevel(
		StringBuilder target,
		KeyValuePair<decimal, decimal> level,
		CoinmetroMarket market)
	{
		target.Append(level.Key.ToString(
			$"F{market.PricePrecision.Max(0).Min(28)}",
			CultureInfo.InvariantCulture));
		target.Append(level.Value.ToString(
			"0.############################",
			CultureInfo.InvariantCulture));
	}

	private static uint[] CreateCrc32Table()
	{
		var result = new uint[256];

		for (uint index = 0; index < result.Length; index++)
		{
			var value = index;

			for (var bit = 0; bit < 8; bit++)
				value = (value & 1) != 0
					? 0xedb88320U ^ value >> 1
					: value >> 1;

			result[index] = value;
		}

		return result;
	}

	private async ValueTask CompleteMarketSubscriptionAsync(
		MarketDataMessage message,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionResultAsync(
			message, cancellationToken);
		await SendSubscriptionFinishedAsync(
			message.TransactionId, cancellationToken);
	}
}
