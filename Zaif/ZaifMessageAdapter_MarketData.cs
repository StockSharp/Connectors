namespace StockSharp.Zaif;

partial class ZaifMessageAdapter
{
	private readonly SynchronizedSet<string> _wsSubscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedSet<string> _tickSubscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedSet<string> _depthSubscriptions = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly SynchronizedSet<string> _tickerSubscriptions = new(StringComparer.InvariantCultureIgnoreCase);

	private readonly SynchronizedDictionary<string, long> _lastTickIds = new(StringComparer.InvariantCultureIgnoreCase);

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
				SecurityId = new SecurityId
				{
					SecurityCode = symbol.Name,
					BoardCode = BoardCodes.Zaif,
				},
				Name = symbol.Title,
				SecurityType = SecurityTypes.CryptoCurrency,
				PriceStep = symbol.ItemUnitStep.ToDecimal(),
				VolumeStep = symbol.ItemUnitMin.ToDecimal(),
				OriginalTransactionId = lookupMsg.TransactionId
			};

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
			await TryAddSocketAsync(currency, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_tickerSubscriptions.Remove(currency);
			TryRemoveSocket(currency);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			_depthSubscriptions.Add(currency);
			await TryAddSocketAsync(currency, cancellationToken);

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_depthSubscriptions.Remove(currency);
			TryRemoveSocket(currency);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var currency = mdMsg.SecurityId.ToCurrency();

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			if (mdMsg.To != null)
			{
				var trades = (await _httpClient.GetTradesAsync(currency, cancellationToken)).ToArray();

				foreach (var tick in trades.OrderBy(t => t.Time))
				{
					await ProcessTickAsync(mdMsg.TransactionId, tick, cancellationToken);
					_lastTickIds[tick.CurrencyPair] = tick.Id;
				}
			}
			else
			{
				_tickSubscriptions.Add(currency);
				await TryAddSocketAsync(currency, cancellationToken);
			}

			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
		{
			_tickSubscriptions.Remove(currency);
			TryRemoveSocket(currency);
		}
	}

	private async ValueTask TryAddSocketAsync(string currency, CancellationToken cancellationToken)
	{
		if (_wsSubscriptions.TryAdd(currency))
			await _pusherClient.SubscribeAsync(currency, cancellationToken);
	}

	private void TryRemoveSocket(string currency)
	{
		if (!_wsSubscriptions.Contains(currency))
			return;

		if (_tickSubscriptions.Contains(currency) || _depthSubscriptions.Contains(currency) || _tickerSubscriptions.Contains(currency))
			return;

		_pusherClient.UnSubscribe(currency);
		_wsSubscriptions.Remove(currency);
	}

	private ValueTask SessionOnTickerChanged(string currencyPair, Ticker ticker, CancellationToken cancellationToken)
	{
		if (!_tickerSubscriptions.Contains(currencyPair))
			return default;

		return SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = currencyPair.ToStockSharp(),
			ServerTime = CurrentTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, ticker.Price.ToDecimal())
		.TryAdd(Level1Fields.LastTradeOrigin, ticker.Action.ToSide()), cancellationToken);
	}

	private ValueTask SessionOnNewTrade(string currencyPair, Trade trade, CancellationToken cancellationToken)
	{
		if (!_tickSubscriptions.Contains(currencyPair))
			return default;

		if (_lastTickIds.TryGetValue(currencyPair, out var tickId) && tickId >= trade.Id)
			return default;

		_lastTickIds[currencyPair] = trade.Id;

		return ProcessTickAsync(0L, trade, cancellationToken);
	}

	private ValueTask ProcessTickAsync(long originalTransactionId, Trade trade, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			SecurityId = trade.CurrencyPair.ToStockSharp(),
			TradeId = trade.Id,
			TradePrice = trade.Price,
			TradeVolume = trade.Amount,
			ServerTime = trade.Time,
			OriginSide = trade.Type.ToSide(),
			OriginalTransactionId = originalTransactionId,
		}, cancellationToken);
	}

	private ValueTask SessionOnOrderBookChanged(string currencyPair, OrderBook book, CancellationToken cancellationToken)
	{
		if (!_depthSubscriptions.Contains(currencyPair))
			return default;

		return SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = currencyPair.ToStockSharp(),
			Bids = book.Bids?.Select(e => new QuoteChange(e.Price, e.Size)).ToArray() ?? [],
			Asks = book.Asks?.Select(e => new QuoteChange(e.Price, e.Size)).ToArray() ?? [],
			ServerTime = book.Timestamp.ToDto(),
		}, cancellationToken);
	}
}
