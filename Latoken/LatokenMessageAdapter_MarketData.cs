namespace StockSharp.LATOKEN;

partial class LatokenMessageAdapter
{
	private readonly SynchronizedPairSet<string, string> _currencyIds = new(StringComparer.InvariantCultureIgnoreCase);

	private async Task EnsureCurrencyIdsAsync(CancellationToken cancellationToken)
	{
		using (_currencyIds.EnterScope())
		{
			if (_currencyIds.Count > 0)
				return;
		}

		var currencies = await _httpClient.GetCurrenciesAsync(cancellationToken);

		using (_currencyIds.EnterScope())
		{
			foreach (var currency in currencies)
				_currencyIds.Add(currency.Id, currency.Tag);
		}
	}

	private async ValueTask<string> GetCurrencyId(string currencyCode, CancellationToken cancellationToken)
	{
		await EnsureCurrencyIdsAsync(cancellationToken);

		return _currencyIds.GetKey(currencyCode);
	}

	private async ValueTask<string> GetCurrencyCode(string currencyId, CancellationToken cancellationToken)
	{
		await EnsureCurrencyIdsAsync(cancellationToken);

		return _currencyIds.GetValue(currencyId);
	}

	private ValueTask<(string code, string board)> GetCurrenciesId(SecurityId securityId, CancellationToken cancellationToken)
	{
		return GetCurrenciesId(securityId.SecurityCode, cancellationToken);
	}

	private async ValueTask<(string code, string board)> GetCurrenciesId(string symbol, CancellationToken cancellationToken)
	{
		var parts = symbol.Split('/');
		return (await GetCurrencyId(parts[0], cancellationToken), await GetCurrencyId(parts[1], cancellationToken));
	}

	private ValueTask<SecurityId> GetSecurityId(BaseEntity entity, CancellationToken cancellationToken)
	{
		return GetSecurityId(entity.BaseCurrency, entity.QuoteCurrency, cancellationToken);
	}

	private async ValueTask<SecurityId> GetSecurityId(string baseCurrId, string quoteCurrId, CancellationToken cancellationToken)
	{
		return $"{await GetCurrencyCode(baseCurrId, cancellationToken)}/{await GetCurrencyCode(quoteCurrId, cancellationToken)}".ToStockSharp();
	}

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		
		await EnsureCurrencyIdsAsync(cancellationToken);

		var secTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var symbol in await _httpClient.GetSymbolsAsync(cancellationToken))
		{
			var secMsg = new SecurityMessage
			{
				SecurityId = await GetSecurityId(symbol, cancellationToken),
				Decimals = symbol.PriceDecimals,
				PriceStep = (decimal)symbol.PriceTick,
				VolumeStep = (decimal)symbol.QuantityTick,
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityType = SecurityTypes.CryptoCurrency,
			}.TryFillUnderlyingId(symbol.BaseCurrency);

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
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var (code, board) = await GetCurrenciesId(mdMsg.SecurityId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeTicker(code, board, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeTicker(code, board, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var (code, board) = await GetCurrenciesId(mdMsg.SecurityId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeTrades(code, board, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeTrades(code, board, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		var (code, board) = await GetCurrenciesId(mdMsg.SecurityId, cancellationToken);

		if (mdMsg.IsSubscribe)
		{
			await _pusherClient.SubscribeOrderBook(code, board, cancellationToken);
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
		}
		else
			await _pusherClient.UnSubscribeOrderBook(code, board, cancellationToken);
	}

	private async ValueTask SessionOnTickerChanged(Ticker ticker, CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new Level1ChangeMessage
		{
			SecurityId = await GetSecurityId(ticker, cancellationToken),
			ServerTime = CurrentTime,
		}
		.TryAdd(Level1Fields.Change, (decimal?)ticker.Change24H)
		.TryAdd(Level1Fields.Volume, (decimal?)ticker.Volume24H)
		.TryAdd(Level1Fields.LowPrice, (decimal?)ticker.LastPrice), cancellationToken);
	}

	private async ValueTask SessionOnNewTrade(Trade trade, CancellationToken cancellationToken)
	{
		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			ServerTime = trade.Timestamp,
			SecurityId = await GetSecurityId(trade,cancellationToken),
			TradeStringId = trade.Id,
			TradePrice = (decimal)trade.Price,
			TradeVolume = (decimal)trade.Quantity,
			OriginSide = trade.Direction.ToSide(),
		}, cancellationToken);
	}

	private async ValueTask SessionOnOrderBookChanged(string code, string board, OrderBook book, bool isSnapshot, CancellationToken cancellationToken)
	{
		var secId = await GetSecurityId(code, board, cancellationToken);

		static QuoteChange toChange(OrderBookEntry entry)
			=> new(entry.Price, entry.Size);

		await SendOutMessageAsync(new QuoteChangeMessage
		{
			SecurityId = secId,
			ServerTime = CurrentTime,
			Bids = book.Bids?.Select(toChange).ToArray() ?? [],
			Asks = book.Asks?.Select(toChange).ToArray() ?? [],
			State = isSnapshot ? QuoteChangeStates.SnapshotComplete : QuoteChangeStates.Increment,
		}, cancellationToken);
	}
}
