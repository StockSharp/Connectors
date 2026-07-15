namespace StockSharp.TastyTrade;

partial class TastyTradeMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var text = message.SecurityId.SecurityCode;
		if (text.IsEmpty())
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var types = message.GetSecurityTypes();
		var left = message.Count ?? long.MaxValue;
		foreach (var item in await _client.SearchSymbols(text, cancellationToken))
		{
			var security = new SecurityMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = new() { SecurityCode = item.Symbol, BoardCode = item.Exchange.IsEmpty("TASTYTRADE").ToUpperInvariant() },
				Name = item.Description,
				SecurityType = item.InstrumentType.ToSecurityType(),
			};
			if (!security.IsMatch(message, types))
				continue;

			try
			{
				var details = await _client.GetInstrument(item.Symbol, security.SecurityType, cancellationToken);
				security.SecurityId = new() { SecurityCode = item.Symbol, BoardCode = details.ToBoardCode() };
				security.Name = details.Description.IsEmpty(item.Description);
				security.PriceStep = details.TickSize;
				if (details is TastyDerivativeInstrument derivative)
				{
					security.ExpiryDate = derivative.ExpirationDate;
					security.Strike = derivative.StrikePrice;
					security.OptionType = derivative.OptionType == TastyOptionTypes.Call ? OptionTypes.Call : derivative.OptionType == TastyOptionTypes.Put ? OptionTypes.Put : null;
					security.Multiplier = derivative.SharesPerContract ?? derivative.Multiplier ?? derivative.ContractSize;
					security.UnderlyingSecurityId = derivative.UnderlyingSymbol.IsEmpty() ? default : new() { SecurityCode = derivative.UnderlyingSymbol, BoardCode = security.SecurityId.BoardCode };
				}
				_streamerSymbols[item.Symbol] = details.StreamerSymbol.IsEmpty(item.Symbol);
			}
			catch (HttpRequestException)
			{
				_streamerSymbols[item.Symbol] = item.Symbol;
			}

			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}
		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (message.IsSubscribe)
		{
			var symbol = await ResolveStreamerSymbol(message.SecurityId, message.SecurityType, cancellationToken);
			_marketSubscriptions[message.TransactionId] = (message.SecurityId, symbol, DxEventTypes.Quote, message.IsHistoryOnly());
			await _marketStreamer.Subscribe(DxEventTypes.Quote, symbol, null, cancellationToken);
			await _marketStreamer.Subscribe(DxEventTypes.Trade, symbol, null, cancellationToken);
			await _marketStreamer.Subscribe(DxEventTypes.Summary, symbol, null, cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		else if (_marketSubscriptions.TryGetAndRemove(message.OriginalTransactionId, out var subscription))
		{
			await _marketStreamer.Unsubscribe(DxEventTypes.Quote, subscription.symbol, cancellationToken);
			await _marketStreamer.Unsubscribe(DxEventTypes.Trade, subscription.symbol, cancellationToken);
			await _marketStreamer.Unsubscribe(DxEventTypes.Summary, subscription.symbol, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessSubscription(message, DxEventTypes.Trade, cancellationToken);

	/// <inheritdoc />
	protected override async ValueTask OnTFCandlesSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (message.IsSubscribe)
		{
			var streamerSymbol = await ResolveStreamerSymbol(message.SecurityId, message.SecurityType, cancellationToken);
			var candleSymbol = $"{streamerSymbol}{{={message.GetTimeFrame().ToDxPeriod()}}}";
			_marketSubscriptions[message.TransactionId] = (message.SecurityId, candleSymbol, DxEventTypes.Candle, message.IsHistoryOnly());
			await _marketStreamer.Subscribe(DxEventTypes.Candle, candleSymbol, message.From, cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		else if (_marketSubscriptions.TryGetAndRemove(message.OriginalTransactionId, out var subscription))
			await _marketStreamer.Unsubscribe(subscription.type, subscription.symbol, cancellationToken);
	}

	private async ValueTask ProcessSubscription(MarketDataMessage message, DxEventTypes type, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		if (message.IsSubscribe)
		{
			var symbol = await ResolveStreamerSymbol(message.SecurityId, message.SecurityType, cancellationToken);
			_marketSubscriptions[message.TransactionId] = (message.SecurityId, symbol, type, message.IsHistoryOnly());
			await _marketStreamer.Subscribe(type, symbol, message.From, cancellationToken);
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		else if (_marketSubscriptions.TryGetAndRemove(message.OriginalTransactionId, out var subscription))
			await _marketStreamer.Unsubscribe(subscription.type, subscription.symbol, cancellationToken);
	}

	private async Task<string> ResolveStreamerSymbol(SecurityId securityId, SecurityTypes? securityType, CancellationToken cancellationToken)
	{
		if (_streamerSymbols.TryGetValue(securityId.SecurityCode, out var symbol))
			return symbol;
		try
		{
			var instrument = await _client.GetInstrument(securityId.SecurityCode, securityType, cancellationToken);
			symbol = instrument.StreamerSymbol.IsEmpty(securityId.SecurityCode);
		}
		catch (HttpRequestException)
		{
			symbol = securityId.SecurityCode;
		}
		_streamerSymbols[securityId.SecurityCode] = symbol;
		return symbol;
	}

	private async ValueTask ProcessMarketData(DxEvent data, CancellationToken cancellationToken)
	{
		foreach (var subscription in _marketSubscriptions.Where(p =>
			p.Value.symbol.EqualsIgnoreCase(data.EventSymbol) &&
			(p.Value.type == data.EventType || p.Value.type == DxEventTypes.Quote && data.EventType is DxEventTypes.Trade or DxEventTypes.Summary)).ToArray())
		{
			var (securityId, _, _, isHistoryOnly) = subscription.Value;
			switch (data.EventType)
			{
				case DxEventTypes.Quote:
					await SendOutMessageAsync(new Level1ChangeMessage
					{
						OriginalTransactionId = subscription.Key,
						SecurityId = securityId,
						ServerTime = (data.BidTime ?? data.AskTime).ToUtcTime(),
					}
					.TryAdd(Level1Fields.BestBidPrice, data.BidPrice)
					.TryAdd(Level1Fields.BestBidVolume, data.BidSize)
					.TryAdd(Level1Fields.BestBidTime, data.BidTime is > 0 ? data.BidTime.Value.FromUnix(false) : null)
					.TryAdd(Level1Fields.BestAskPrice, data.AskPrice)
					.TryAdd(Level1Fields.BestAskVolume, data.AskSize)
					.TryAdd(Level1Fields.BestAskTime, data.AskTime is > 0 ? data.AskTime.Value.FromUnix(false) : null), cancellationToken);
					break;
				case DxEventTypes.Trade:
					if (subscription.Value.type == DxEventTypes.Quote)
					{
						await SendOutMessageAsync(new Level1ChangeMessage
						{
							OriginalTransactionId = subscription.Key,
							SecurityId = securityId,
							ServerTime = data.Time.ToUtcTime(),
						}
						.TryAdd(Level1Fields.LastTradePrice, data.Price)
						.TryAdd(Level1Fields.LastTradeVolume, data.Size)
						.TryAdd(Level1Fields.Volume, data.DayVolume), cancellationToken);
					}
					else
					{
						await SendOutMessageAsync(new ExecutionMessage
						{
							DataTypeEx = DataType.Ticks,
							OriginalTransactionId = subscription.Key,
							SecurityId = securityId,
							ServerTime = data.Time.ToUtcTime(),
							TradePrice = data.Price,
							TradeVolume = data.Size,
						}, cancellationToken);
					}
					break;
				case DxEventTypes.Summary:
					await SendOutMessageAsync(new Level1ChangeMessage
					{
						OriginalTransactionId = subscription.Key,
						SecurityId = securityId,
						ServerTime = DateTime.UtcNow,
					}
					.TryAdd(Level1Fields.OpenPrice, data.OpenPrice)
					.TryAdd(Level1Fields.HighPrice, data.HighPrice)
					.TryAdd(Level1Fields.LowPrice, data.LowPrice)
					.TryAdd(Level1Fields.ClosePrice, data.PreviousClosePrice), cancellationToken);
					break;
				case DxEventTypes.Candle:
					var timeFrame = data.EventSymbol.FromDxPeriod();
					var openTime = data.Time.ToUtcTime();
					await SendOutMessageAsync(new TimeFrameCandleMessage
					{
						OriginalTransactionId = subscription.Key,
						SecurityId = securityId,
						TypedArg = timeFrame,
						OpenTime = openTime,
						CloseTime = openTime + timeFrame,
						OpenPrice = data.Open ?? 0,
						HighPrice = data.High ?? 0,
						LowPrice = data.Low ?? 0,
						ClosePrice = data.Close ?? 0,
						TotalVolume = data.Volume ?? 0,
						OpenInterest = data.OpenInterest,
						State = CandleStates.Finished,
					}, cancellationToken);
					break;
			}

			if (isHistoryOnly && data.EventType == DxEventTypes.Candle && data.IsSnapshotComplete())
			{
				_marketSubscriptions.Remove(subscription.Key);
				await _marketStreamer.Unsubscribe(subscription.Value.type, subscription.Value.symbol, cancellationToken);
				await SendSubscriptionFinishedAsync(subscription.Key, cancellationToken);
			}
		}
	}
}
