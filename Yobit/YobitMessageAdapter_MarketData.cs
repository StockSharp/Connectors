namespace StockSharp.Yobit;

partial class YobitMessageAdapter
{
	private int? _maxDepth;

	private readonly HashSet<string> _orderBookSubscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, long?> _tradesSubscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly HashSet<string> _level1Subscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly HashSet<string> _wsOrderBookSubscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly HashSet<string> _wsTradesSubscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, long?> _pairIds = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Lock _sync = new();
	private bool _wsTickerSubscribed;
	private volatile bool _wsConnected;

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var pair in await _httpClient.GetSymbolsAsync(cancellationToken))
		{
			var secId = pair.Key.ToStockSharp();
			var symbol = pair.Value;

			var secMsg = new SecurityMessage
			{
				SecurityId = secId,
				SecurityType = SecurityTypes.CryptoCurrency,
				OriginalTransactionId = lookupMsg.TransactionId,
				Decimals = symbol.DecimalPlaces,
				PriceStep = symbol.MinPrice,
				MinVolume = symbol.MinAmount,
			};

			_allSymbols.Add(pair.Key);

			if (!secMsg.IsMatch(lookupMsg, secTypes))
				continue;

			await SendOutMessageAsync(secMsg, cancellationToken);

			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = secId,
				ServerTime = CurrentTime
			}
			.TryAdd(Level1Fields.CommissionTaker, symbol.Fee)
			.Add(Level1Fields.State, symbol.Hidden == 1 ? SecurityStates.Stoped : SecurityStates.Trading), cancellationToken);

			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await ProcessLevel1SubscriptionsAsync([currency], cancellationToken);
			_level1Subscriptions.Add(currency);
			await TrySubscribeTickerAsync(cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_level1Subscriptions.Remove(currency);

			if (_level1Subscriptions.Count == 0)
				await TryUnSubscribeTickerAsync(cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToCurrency();
		var currTime = CurrentTime;

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.MaxDepth != null && (_maxDepth == null || mdMsg.MaxDepth.Value > _maxDepth.Value))
				_maxDepth = mdMsg.MaxDepth.Value;

			await ProcessOrderBookSubscriptionsAsync([currency], _maxDepth, currTime, cancellationToken);
			_orderBookSubscriptions.Add(currency);
			await TrySubscribeOrderBookAsync(currency, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_orderBookSubscriptions.Remove(currency);
			await TryUnSubscribeOrderBookAsync(currency, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			var lastId = _tradesSubscriptions.TryGetValue(currency);

			if (mdMsg.To != null)
			{
				await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

				var trades = await _httpClient.GetTicksAsync([currency], 2000, cancellationToken);

				foreach (var trade in trades.SelectMany(p => p.Value).OrderBy(t => t.Timestamp))
				{
					if (lastId != null && trade.Id <= lastId)
						continue;
					lastId = trade.Id;
					await ProcessTickAsync(mdMsg.TransactionId, mdMsg.SecurityId, trade, cancellationToken);
				}

				_tradesSubscriptions[currency] = lastId;
			}
			else
				_tradesSubscriptions[currency] = await ProcessTicksSubscriptionAsync(currency, lastId, cancellationToken);

			await TrySubscribeTradesAsync(currency, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_tradesSubscriptions.Remove(currency);
			await TryUnSubscribeTradesAsync(currency, cancellationToken);
		}
	}

	private async ValueTask ProcessSubscriptionsAsync(CancellationToken cancellationToken)
	{
		var currTime = CurrentTime;

		const int maxSymbols = 40;

		var restOrderBooks = _wsConnected
			? _orderBookSubscriptions.Except(_wsOrderBookSubscriptions, StringComparer.InvariantCultureIgnoreCase).ToArray()
			: _orderBookSubscriptions.ToArray();

		if (restOrderBooks.Length > 0)
		{
			foreach (var batch in restOrderBooks.Chunk(maxSymbols))
			{
				await ProcessOrderBookSubscriptionsAsync(batch, _maxDepth, currTime, cancellationToken);
			}
		}

		var restTrades = _wsConnected
			? _tradesSubscriptions.Keys.Except(_wsTradesSubscriptions, StringComparer.InvariantCultureIgnoreCase).ToArray()
			: _tradesSubscriptions.Keys.ToArray();

		if (restTrades.Length > 0)
		{
			foreach (var batch in restTrades.Chunk(maxSymbols))
			{
				await ProcessTicksSubscriptionsAsync(batch, cancellationToken);
			}
		}

		if (_level1Subscriptions.Count > 0 && (!_wsConnected || !_wsTickerSubscribed))
		{
			foreach (var batch in _level1Subscriptions.Chunk(maxSymbols))
			{
				await ProcessLevel1SubscriptionsAsync(batch, cancellationToken);
			}
		}
	}

	private void SetWsConnectionState(bool connected)
	{
		_wsConnected = connected;
	}

	private async ValueTask<long?> TryGetPairIdAsync(string currency, CancellationToken cancellationToken)
	{
		using (_sync.EnterScope())
		{
			if (_pairIds.TryGetValue(currency, out var pairId))
				return pairId;
		}

		try
		{
			var pairId = await _httpClient.GetPairIdAsync(currency, cancellationToken);

			using (_sync.EnterScope())
				_pairIds[currency] = pairId;

			return pairId;
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
			return null;
		}
	}

	private async ValueTask TrySubscribeTickerAsync(CancellationToken cancellationToken)
	{
		if (_pusherClient == null || _wsTickerSubscribed)
			return;

		try
		{
			await _pusherClient.SubscribeTickerAsync(cancellationToken);
			_wsTickerSubscribed = true;
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
		}
	}

	private async ValueTask TryUnSubscribeTickerAsync(CancellationToken cancellationToken)
	{
		if (_pusherClient == null || !_wsTickerSubscribed)
			return;

		try
		{
			await _pusherClient.UnSubscribeTickerAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
		}
		finally
		{
			_wsTickerSubscribed = false;
		}
	}

	private async ValueTask TrySubscribeTradesAsync(string currency, CancellationToken cancellationToken)
	{
		if (_pusherClient == null || _wsTradesSubscriptions.Contains(currency))
			return;

		var pairId = await TryGetPairIdAsync(currency, cancellationToken);

		if (pairId == null)
			return;

		try
		{
			await _pusherClient.SubscribeTradesAsync(pairId.Value, currency, cancellationToken);
			_wsTradesSubscriptions.Add(currency);
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
		}
	}

	private async ValueTask TryUnSubscribeTradesAsync(string currency, CancellationToken cancellationToken)
	{
		if (_pusherClient == null || !_wsTradesSubscriptions.Remove(currency))
			return;

		long? pairId;

		using (_sync.EnterScope())
		{
			if (!_pairIds.TryGetValue(currency, out pairId))
				pairId = null;
		}

		if (pairId == null)
			return;

		try
		{
			await _pusherClient.UnSubscribeTradesAsync(pairId.Value, cancellationToken);
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
		}
	}

	private async ValueTask TrySubscribeOrderBookAsync(string currency, CancellationToken cancellationToken)
	{
		if (_pusherClient == null || _wsOrderBookSubscriptions.Contains(currency))
			return;

		var pairId = await TryGetPairIdAsync(currency, cancellationToken);

		if (pairId == null)
			return;

		try
		{
			await _pusherClient.SubscribeOrderBookAsync(pairId.Value, currency, cancellationToken);
			_wsOrderBookSubscriptions.Add(currency);
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
		}
	}

	private async ValueTask TryUnSubscribeOrderBookAsync(string currency, CancellationToken cancellationToken)
	{
		if (_pusherClient == null || !_wsOrderBookSubscriptions.Remove(currency))
			return;

		long? pairId;

		using (_sync.EnterScope())
		{
			if (!_pairIds.TryGetValue(currency, out pairId))
				pairId = null;
		}

		if (pairId == null)
			return;

		try
		{
			await _pusherClient.UnSubscribeOrderBookAsync(pairId.Value, cancellationToken);
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
		}
	}

	private ValueTask SessionOnTickerChanged(string currency, Ticker ticker, CancellationToken cancellationToken)
	{
		if (!_wsTickerSubscribed || !_level1Subscriptions.Contains(currency))
			return default;

		var serverTime = ticker.Updated == default ? CurrentTime : ticker.Updated;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = currency.ToStockSharp(),
			ServerTime = serverTime,
		}
		.TryAdd(Level1Fields.AveragePrice, ticker.Avg)
		.TryAdd(Level1Fields.HighPrice, ticker.High)
		.TryAdd(Level1Fields.LowPrice, ticker.Low)
		.TryAdd(Level1Fields.LastTradePrice, ticker.Last)
		.TryAdd(Level1Fields.BestBidPrice, ticker.Buy)
		.TryAdd(Level1Fields.BestAskPrice, ticker.Sell)
		.TryAdd(Level1Fields.Volume, ticker.Volume), cancellationToken);
	}

	private ValueTask SessionOnNewTrade(string currency, Trade trade, CancellationToken cancellationToken)
	{
		if (!_wsTradesSubscriptions.Contains(currency) || !_tradesSubscriptions.ContainsKey(currency))
			return default;

		var lastId = _tradesSubscriptions.TryGetValue(currency);

		if (lastId != null && trade.Id <= lastId)
			return default;

		_tradesSubscriptions[currency] = trade.Id;
		return ProcessTickAsync(0, currency.ToStockSharp(), trade, cancellationToken);
	}

	private ValueTask SessionOnOrderBookChanged(string currency, OrderBook book, CancellationToken cancellationToken)
	{
		if (!_wsOrderBookSubscriptions.Contains(currency) || !_orderBookSubscriptions.Contains(currency))
			return default;

		IEnumerable<QuoteChange> bids = book.Bids?.Select(e => new QuoteChange(e.Price, e.Size)) ?? Enumerable.Empty<QuoteChange>();
		IEnumerable<QuoteChange> asks = book.Asks?.Select(e => new QuoteChange(e.Price, e.Size)) ?? Enumerable.Empty<QuoteChange>();

		if (_maxDepth != null)
		{
			bids = bids.Take(_maxDepth.Value);
			asks = asks.Take(_maxDepth.Value);
		}

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = currency.ToStockSharp(),
			Bids = bids.ToArray(),
			Asks = asks.ToArray(),
			ServerTime = CurrentTime,
		}, cancellationToken);
	}

	private void ClearWsSubscriptionState()
	{
		_wsConnected = false;
		_wsTickerSubscribed = false;
		_wsOrderBookSubscriptions.Clear();
		_wsTradesSubscriptions.Clear();

		using (_sync.EnterScope())
			_pairIds.Clear();
	}

	private async ValueTask ProcessOrderBookSubscriptionsAsync(IEnumerable<string> batch, int? depth, DateTime currTime, CancellationToken cancellationToken)
	{
		var depths = await _httpClient.GetDepthsAsync(batch, depth ?? 5, cancellationToken);

		foreach (var pair in depths)
		{
			var book = pair.Value;

			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = pair.Key.ToStockSharp(),
				Bids = book.Bids?.Select(e => new QuoteChange(e.Price, e.Size)).ToArray() ?? [],
				Asks = book.Asks?.Select(e => new QuoteChange(e.Price, e.Size)).ToArray() ?? [],
				ServerTime = currTime,
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessTicksSubscriptionsAsync(IEnumerable<string> ids, CancellationToken cancellationToken)
	{
		foreach (var pair in await _httpClient.GetTicksAsync(ids, 10, cancellationToken))
		{
			var secId = pair.Key.ToStockSharp();

			var lastId = _tradesSubscriptions.TryGetValue(pair.Key);

			foreach (var trade in pair.Value)
			{
				if (lastId != null && trade.Id <= lastId)
					continue;

				lastId = trade.Id;
				await ProcessTickAsync(0, secId, trade, cancellationToken);
			}

			_tradesSubscriptions[pair.Key] = lastId;
		}
	}

	private async ValueTask<long?> ProcessTicksSubscriptionAsync(string id, long? lastId, CancellationToken cancellationToken)
	{
		foreach (var pair in await _httpClient.GetTicksAsync([id], 10, cancellationToken))
		{
			var secId = pair.Key.ToStockSharp();

			foreach (var trade in pair.Value)
			{
				if (lastId != null && trade.Id <= lastId)
					continue;

				lastId = trade.Id;
				await ProcessTickAsync(0, secId, trade, cancellationToken);
			}

			return lastId;
		}

		return null;
	}

	private async ValueTask ProcessLevel1SubscriptionsAsync(IEnumerable<string> ids, CancellationToken cancellationToken)
	{
		foreach (var pair in await _httpClient.GetTickersAsync(ids, cancellationToken))
		{
			var ticker = pair.Value;

			await SendOutMessageAsync(new Level1ChangeMessage
			{
				SecurityId = pair.Key.ToStockSharp(),
				ServerTime = ticker.Updated,
			}
			.TryAdd(Level1Fields.AveragePrice, ticker.Avg)
			.TryAdd(Level1Fields.HighPrice, ticker.High)
			.TryAdd(Level1Fields.LowPrice, ticker.Low)
			.TryAdd(Level1Fields.LastTradePrice, ticker.Last)
			.TryAdd(Level1Fields.BestBidPrice, ticker.Buy)
			.TryAdd(Level1Fields.BestAskPrice, ticker.Sell)
			.TryAdd(Level1Fields.Volume, ticker.Volume), cancellationToken);
		}
	}

	private ValueTask ProcessTickAsync(long transactionId, SecurityId securityId, Trade trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = securityId,
			OriginSide = trade.Type.ToSide(),
			TradeId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			ServerTime = trade.Timestamp,
			OriginalTransactionId = transactionId,
		}, cancellationToken);
	}
}
