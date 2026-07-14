namespace StockSharp.TradeOgre;

partial class TradeOgreMessageAdapter
{
	private readonly SynchronizedDictionary<string, DateTime> _tickSubscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedSet<string> _depthSubscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedSet<string> _tickerSubscriptions = new(StringComparer.InvariantCultureIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var pair in await _httpClient.GetTickersAsync(cancellationToken))
		{
			var secMsg = new SecurityMessage
			{
				SecurityId = pair.Key.ToStockSharp(),
				OriginalTransactionId = lookupMsg.TransactionId,
			}.FillDefaultCryptoFields();

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
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			_tickerSubscriptions.Add(currency);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			_tickerSubscriptions.Remove(currency);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			_depthSubscriptions.Add(currency);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			_depthSubscriptions.Remove(currency);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			_tickSubscriptions.Add(currency, DateTime.UnixEpoch);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			_tickSubscriptions.Remove(currency);
	}

	private async ValueTask ProcessSubscriptionsAsync(CancellationToken cancellationToken)
	{
		var currentTime = CurrentTime;

		if (_tickerSubscriptions.Count > 0)
		{
			if (_tickerSubscriptions.Count > 2)
			{
				var tickers = await _httpClient.GetTickersAsync(cancellationToken);

				foreach (var pair in tickers)
				{
					if (!_tickerSubscriptions.Contains(pair.Key))
						continue;

					var ticker = pair.Value;

					await ProcessTickerAsync(pair.Key, ticker, currentTime, cancellationToken);
				}
			}
			else
			{
				foreach (var symbol in _tickerSubscriptions)
				{
					var ticker = await _httpClient.GetTickerAsync(symbol, cancellationToken);
					await ProcessTickerAsync(symbol, ticker, currentTime, cancellationToken);
				}
			}
		}

		foreach (var symbol in _depthSubscriptions)
		{
			var book = await _httpClient.GetOrderBookAsync(symbol, cancellationToken);

			await SendOutMessageAsync(new QuoteChangeMessage
			{
				SecurityId = symbol.ToStockSharp(),
				ServerTime = CurrentTime,
				Bids = book.Bids.Select(p => new QuoteChange((decimal)p.Key, (decimal)p.Value)).ToArray(),
				Asks = book.Asks.Select(p => new QuoteChange((decimal)p.Key, (decimal)p.Value)).ToArray(),
			}, cancellationToken);
		}

		if (_tickSubscriptions.Count > 0)
		{
			foreach (var pair in _tickSubscriptions.ToArray())
			{
				var symbol = pair.Key;
				var trades = await _httpClient.GetTradeHistoryAsync(pair.Key, cancellationToken);

				var lastTime = pair.Value;

				var secId = symbol.ToStockSharp();

				foreach (var trade in trades.OrderBy(t => t.Time))
				{
					if (trade.Time <= lastTime)
						continue;

					await SendOutMessageAsync(new ExecutionMessage
					{
						DataTypeEx = DataType.Ticks,
						SecurityId = secId,
						ServerTime = trade.Time,
						TradePrice = (decimal)trade.Price,
						TradeVolume = (decimal)trade.Quantity,
						OriginSide = trade.Type.ToSide(),
					}, cancellationToken);

					lastTime = trade.Time;
				}

				_tickSubscriptions[symbol] = lastTime;
			}
		}
	}

	private ValueTask ProcessTickerAsync(string symbol, Ticker ticker, DateTime currentTime, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = symbol.ToStockSharp(),
			ServerTime = currentTime,
		}
		.TryAdd(Level1Fields.BestBidPrice, ticker.Bid?.ToDecimal())
		.TryAdd(Level1Fields.BestAskPrice, ticker.Ask?.ToDecimal())
		.TryAdd(Level1Fields.OpenPrice, ticker.InitialPrice?.ToDecimal())
		.TryAdd(Level1Fields.HighPrice, ticker.High?.ToDecimal())
		.TryAdd(Level1Fields.LowPrice, ticker.Low?.ToDecimal())
		.TryAdd(Level1Fields.ClosePrice, ticker.Price?.ToDecimal())
		.TryAdd(Level1Fields.Volume, ticker.Volume?.ToDecimal()), cancellationToken);
	}
}
