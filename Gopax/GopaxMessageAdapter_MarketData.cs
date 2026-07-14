namespace StockSharp.Gopax;

partial class GopaxMessageAdapter
{
	private readonly HashSet<SecurityId> _orderBookSubscriptions = [];
	private readonly Dictionary<SecurityId, long?> _tradesSubscriptions = [];
	private readonly HashSet<SecurityId> _level1Subscriptions = [];
	private readonly Dictionary<long, RefTriple<SecurityId, int, long>> _candlesSubscriptions = [];

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var symbol in await _httpClient.GetSymbolsAsync(cancellationToken))
		{
			var secMsg = new SecurityMessage
			{
				SecurityId = symbol.Name.ToStockSharp(),
				OriginalTransactionId = lookupMsg.TransactionId,
			}
			.TryFillUnderlyingId(symbol.BaseAsset)
			.FillDefaultCryptoFields();

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var symbol = secId.ToSymbol();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			var ticker = await _httpClient.GetTickerAsync(symbol, cancellationToken);

			_level1Subscriptions.Add(secId);

			ticker.Name = symbol;
			await ProcessTickerAsync(secId, ticker, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			_level1Subscriptions.Remove(secId);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var symbol = secId.ToSymbol();
		var currTime = CurrentTime;

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			var book = await _httpClient.GetOrderBookAsync(symbol, cancellationToken);

			_orderBookSubscriptions.Add(secId);

			await ProcessOrderBookAsync(secId, book, currTime, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			_orderBookSubscriptions.Remove(secId);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var symbol = secId.ToSymbol();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			var lastTime = _tradesSubscriptions.TryGetValue(secId);

			if (mdMsg.To != null)
			{
				var from = mdMsg.From ?? mdMsg.To.Value.AddHours(-1);

				var trades = await _httpClient.GetTradesAsync(symbol, 100, (long)from.ToUnix(), cancellationToken);

				foreach (var trade in trades.OrderBy(t => t.Time))
				{
					var time = (long)trade.Time.ToUnix(false);

					if (lastTime != null && time < lastTime)
						continue;

					lastTime = time;

					await ProcessTickAsync(mdMsg.SecurityId, trade, cancellationToken);
				}

				_tradesSubscriptions[secId] = lastTime ?? 0;
			}
			else
				_tradesSubscriptions[secId] = lastTime;

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_tradesSubscriptions.Remove(secId);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var secId = mdMsg.SecurityId;
		var symbol = secId.ToSymbol();
		var transId = mdMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		var tf = mdMsg.GetTimeFrame();

		if (mdMsg.IsSubscribe)
		{
			var interval = tf.ToNative();

			if (mdMsg.To != null)
			{
				var from = mdMsg.From.Value;
				var to = mdMsg.To.Value;

				var candles = await _httpClient.GetCandlesAsync(symbol, interval, (long)from.ToUnix(false), (long)to.ToUnix(false), cancellationToken);

				foreach (var candle in candles.OrderBy(t => t.Time))
					await ProcessOhlcAsync(transId, candle, cancellationToken);
			}
			else
				_candlesSubscriptions.Add(transId, RefTuple.Create(secId, interval, (long)DateTime.UtcNow.Truncate(tf).ToUnix(false)));

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_candlesSubscriptions.Remove(mdMsg.OriginalTransactionId);
		}
	}

	private async ValueTask ProcessSubscriptionsAsync(CancellationToken cancellationToken)
	{
		var currTime = CurrentTime;

		if (_orderBookSubscriptions.Count > 0)
		{
			foreach (var secId in _orderBookSubscriptions)
			{
				await ProcessOrderBookAsync(secId, await _httpClient.GetOrderBookAsync(secId.ToSymbol(), cancellationToken), currTime, cancellationToken);
			}
		}

		if (_tradesSubscriptions.Count > 0)
		{
			foreach (var pair in _tradesSubscriptions.ToArray())
			{
				var secId = pair.Key;
				var lastTime = pair.Value ?? 0;
				var trades = await _httpClient.GetTradesAsync(pair.Key.ToSymbol(), 100, lastTime, cancellationToken);

				foreach (var trade in trades.OrderBy(t => t.Time))
				{
					var time = (long)trade.Time.ToUnix();

					if (time < lastTime)
						continue;

					lastTime = time;

					await ProcessTickAsync(secId, trade, cancellationToken);
				}

				_tradesSubscriptions[pair.Key] = lastTime;
			}
		}

		if (_level1Subscriptions.Count > 0)
		{
			if (_level1Subscriptions.Count > 3)
			{
				foreach (var ticker in await _httpClient.GetTickersAsync(cancellationToken))
				{
					await ProcessTickerAsync(ticker.Name.ToStockSharp(), ticker, cancellationToken);
				}
			}
			else
			{
				foreach (var secId in _level1Subscriptions)
				{
					await ProcessTickerAsync(secId, await _httpClient.GetTickerAsync(secId.ToSymbol(), cancellationToken), cancellationToken);
				}
			}
		}

		if (_candlesSubscriptions.Count > 0)
		{
			foreach (var pair in _candlesSubscriptions.ToArray())
			{
				var info = pair.Value;
				var mlsStep = (int)TimeSpan.FromMinutes(info.Second).TotalMilliseconds;
				var candles = await _httpClient.GetCandlesAsync(info.First.ToSymbol(), info.Second, info.Third, info.Third + mlsStep, cancellationToken);

				foreach (var ohlc in candles.OrderBy(c => c.Time))
				{
					if (ohlc.Time < info.Third)
						continue;

					info.Third = ohlc.Time;

					await ProcessOhlcAsync(pair.Key, ohlc, cancellationToken);

					info.Third += mlsStep;
				}
			}
		}
	}

	private ValueTask ProcessOrderBookAsync(SecurityId secId, OrderBook book, DateTime currTime, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = secId,
			Bids = book.Bids?.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Size)).ToArray() ?? [],
			Asks = book.Asks?.Select(e => new QuoteChange((decimal)e.Price, (decimal)e.Size)).ToArray() ?? [],
			ServerTime = currTime,
		}, cancellationToken);
	}

	private ValueTask ProcessTickerAsync(SecurityId secId, Ticker ticker, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = secId,
			ServerTime = ticker.Time,
		}
		.TryAdd(Level1Fields.OpenPrice, (decimal)ticker.Open)
		.TryAdd(Level1Fields.HighPrice, (decimal)ticker.High)
		.TryAdd(Level1Fields.LowPrice, (decimal)ticker.Low)
		.TryAdd(Level1Fields.ClosePrice, (decimal)ticker.Close)
		.TryAdd(Level1Fields.Volume, (decimal)ticker.Volume), cancellationToken);
	}

	private ValueTask ProcessTickAsync(SecurityId secId, Trade trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = secId,
			OriginSide = trade.Side.ToSide(),
			TradeId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			ServerTime = trade.Time,
		}, cancellationToken);
	}

	private ValueTask ProcessOhlcAsync(long originId, Ohlc ohlc, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeFrameCandleMessage
		{
			OpenTime = ohlc.Time.FromUnix(false),
			OpenPrice = (decimal)ohlc.Open,
			HighPrice = (decimal)ohlc.High,
			LowPrice = (decimal)ohlc.Low,
			ClosePrice = (decimal)ohlc.Close,
			TotalVolume = (decimal)ohlc.Volume,
			State = CandleStates.Active,
			OriginalTransactionId = originId,
		}, cancellationToken);
	}
}
