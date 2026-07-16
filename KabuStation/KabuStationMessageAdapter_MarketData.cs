namespace StockSharp.KabuStation;

public partial class KabuStationMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(lookupMsg.TransactionId, cancellationToken);
		var code = lookupMsg.SecurityId.SecurityCode;
		if (code.IsEmpty())
		{
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
			return;
		}

		var requestedType = lookupMsg.SecurityType;
		var exchange = lookupMsg.SecurityId.ToKabuExchange(requestedType);
		var preliminary = new KabuStationSecurityInfo
		{
			Symbol = code,
			Exchange = exchange,
			BoardCode = KabuStationExtensions.ToBoardCode(exchange),
			SecurityType = requestedType ?? SecurityTypes.Stock,
			NativeSecurityType = (requestedType ?? SecurityTypes.Stock).ToNativeSecurityType(),
		};

		try
		{
			var symbol = await _rest.GetSymbol(preliminary, cancellationToken);
			var board = await _rest.GetBoard(preliminary, cancellationToken);
			var securityType = board.NativeSecurityType.ToSecurityType();
			var security = new KabuStationSecurityInfo
			{
				Symbol = symbol.Symbol.IsEmpty(code),
				Exchange = symbol.Exchange == 0 ? exchange : symbol.Exchange,
				BoardCode = KabuStationExtensions.ToBoardCode(symbol.Exchange == 0 ? exchange : symbol.Exchange),
				SecurityType = securityType,
				NativeSecurityType = board.NativeSecurityType,
			};
			var securityId = security.ToSecurityId();
			var message = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = securityId,
				SecurityType = securityType,
				Currency = CurrencyTypes.JPY,
				Name = symbol.SymbolName.IsEmpty(board.SymbolName),
				ShortName = symbol.DisplayName.IsEmpty(symbol.SymbolName).IsEmpty(board.SymbolName),
				VolumeStep = symbol.TradingUnit,
				ExpiryDate = KabuStationExtensions.ParseApiDate(symbol.TradeEnd),
				Strike = symbol.StrikePrice,
				OptionType = symbol.PutOrCall switch
				{
					1 => OptionTypes.Put,
					2 => OptionTypes.Call,
					_ => null,
				},
				UnderlyingSecurityId = symbol.Underlying.IsEmpty()
					? default
					: new SecurityId { SecurityCode = symbol.Underlying, BoardCode = security.BoardCode },
			};
			if (message.IsMatch(lookupMsg, lookupMsg.GetSecurityTypes()))
			{
				CacheSecurity(security);
				await SendOutMessageAsync(message, cancellationToken);
			}
		}
		catch (KabuStationApiException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
		{
			this.AddDebugLog("kabu Station symbol {0}@{1} was not found: {2}", code, exchange, ex.Message);
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.Ticks, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
		=> ProcessMarketSubscription(mdMsg, DataType.MarketDepth, cancellationToken);

	private async ValueTask ProcessMarketSubscription(MarketDataMessage mdMsg, DataType dataType,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			if (_marketSubscriptions.TryGetAndRemove(mdMsg.OriginalTransactionId, out var previous))
				await UpdateRegistration(previous, false, cancellationToken);
			return;
		}

		var security = ResolveSecurity(mdMsg.SecurityId, mdMsg.SecurityType);
		var board = await _rest.GetBoard(security, cancellationToken);
		var subscription = new MarketSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = mdMsg.SecurityId,
			Security = security,
			DataType = dataType,
			MaxDepth = Math.Clamp(mdMsg.MaxDepth ?? 10, 1, 10),
		};
		await SendBoard(subscription, board, cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
			return;
		}

		_marketSubscriptions[mdMsg.TransactionId] = subscription;
		try
		{
			await UpdateRegistration(subscription, true, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask UpdateRegistration(MarketSubscription changed, bool isAdding,
		CancellationToken cancellationToken)
	{
		var key = GetNativeKey(changed.Security);
		var hasOther = _marketSubscriptions.CachedValues.Any(subscription =>
			subscription.TransactionId != changed.TransactionId &&
			GetNativeKey(subscription.Security).Equals(key, StringComparison.OrdinalIgnoreCase));
		if (hasOther)
			return;

		if (isAdding)
		{
			if (_registeredSecurities.ContainsKey(key))
				return;
			if (_registeredSecurities.Count >= 50)
				throw new InvalidOperationException("kabu Station PUSH supports at most 50 registered instruments, shared with REST lookups.");
			await _rest.Register(changed.Security, cancellationToken);
			_registeredSecurities[key] = changed.Security;
		}
		else if (_registeredSecurities.TryGetValue(key, out var security))
		{
			await _rest.Unregister(security, cancellationToken);
			_registeredSecurities.Remove(key);
		}
	}

	private async ValueTask OnBoardReceived(KabuStationBoard board, CancellationToken cancellationToken)
	{
		if (board?.Symbol.IsEmpty() != false)
			return;
		var subscriptions = _marketSubscriptions.CachedValues
			.Where(subscription => subscription.Security.Symbol.Equals(board.Symbol, StringComparison.OrdinalIgnoreCase) &&
				subscription.Security.Exchange == board.Exchange)
			.ToArray();
		foreach (var subscription in subscriptions)
			await SendBoard(subscription, board, cancellationToken);
	}

	private ValueTask SendBoard(MarketSubscription subscription, KabuStationBoard board,
		CancellationToken cancellationToken)
		=> subscription.DataType switch
		{
			_ when subscription.DataType == DataType.Level1 => SendOutMessageAsync(
				CreateLevel1(subscription.TransactionId, subscription.SecurityId, board), cancellationToken),
			_ when subscription.DataType == DataType.MarketDepth => SendOutMessageAsync(
				CreateDepth(subscription.TransactionId, subscription.SecurityId, board, subscription.MaxDepth), cancellationToken),
			_ when subscription.DataType == DataType.Ticks => SendTick(subscription, board, cancellationToken),
			_ => default,
		};

	private async ValueTask SendTick(MarketSubscription subscription, KabuStationBoard board,
		CancellationToken cancellationToken)
	{
		if (board.CurrentPrice is not { } price)
			return;
		var serverTime = KabuStationExtensions.ParseJapanTime(board.CurrentPriceTime) ?? CurrentTime;
		if (subscription.LastTickTime == serverTime && subscription.LastTickPrice == price)
			return;

		decimal? volume = null;
		if (board.TradingVolume is { } cumulative && subscription.LastCumulativeVolume is { } previous && cumulative >= previous)
			volume = cumulative - previous;
		if (board.TradingVolume is { } total)
			subscription.LastCumulativeVolume = total;
		subscription.LastTickTime = serverTime;
		subscription.LastTickPrice = price;

		await SendOutMessageAsync(new ExecutionMessage
		{
			DataTypeEx = DataType.Ticks,
			OriginalTransactionId = subscription.TransactionId,
			SecurityId = subscription.SecurityId,
			TradePrice = price,
			TradeVolume = volume is > 0 ? volume : null,
			ServerTime = serverTime,
		}, cancellationToken);
	}

	private static Level1ChangeMessage CreateLevel1(long transactionId, SecurityId securityId,
		KabuStationBoard board)
		=> new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = KabuStationExtensions.ParseJapanTime(board.CurrentPriceTime) ?? DateTime.UtcNow,
		}
		.TryAdd(Level1Fields.LastTradePrice, board.CurrentPrice)
		.TryAdd(Level1Fields.OpenPrice, board.OpenPrice)
		.TryAdd(Level1Fields.HighPrice, board.HighPrice)
		.TryAdd(Level1Fields.LowPrice, board.LowPrice)
		.TryAdd(Level1Fields.ClosePrice, board.PreviousClose)
		.TryAdd(Level1Fields.Change, board.ChangePreviousClose)
		.TryAdd(Level1Fields.Volume, board.TradingVolume)
		.TryAdd(Level1Fields.Turnover, board.TradingValue)
		.TryAdd(Level1Fields.VWAP, board.Vwap)
		.TryAdd(Level1Fields.BestBidPrice, board.BuyQuotePrice)
		.TryAdd(Level1Fields.BestBidVolume, board.BuyQuoteQuantity)
		.TryAdd(Level1Fields.BestAskPrice, board.SellQuotePrice)
		.TryAdd(Level1Fields.BestAskVolume, board.SellQuoteQuantity)
		.TryAdd(Level1Fields.ImpliedVolatility, board.ImpliedVolatility)
		.TryAdd(Level1Fields.Delta, board.Delta)
		.TryAdd(Level1Fields.Gamma, board.Gamma)
		.TryAdd(Level1Fields.Theta, board.Theta)
		.TryAdd(Level1Fields.Vega, board.Vega);

	private static QuoteChangeMessage CreateDepth(long transactionId, SecurityId securityId,
		KabuStationBoard board, int maxDepth)
	{
		var bids = board.GetBuys().Where(level => level?.Price is > 0)
			.OrderByDescending(level => level.Price).Take(maxDepth)
			.Select(level => new QuoteChange(level.Price.Value, level.Quantity ?? 0)).ToArray();
		var asks = board.GetSells().Where(level => level?.Price is > 0)
			.OrderBy(level => level.Price).Take(maxDepth)
			.Select(level => new QuoteChange(level.Price.Value, level.Quantity ?? 0)).ToArray();
		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = KabuStationExtensions.ParseJapanTime(board.CurrentPriceTime) ?? DateTime.UtcNow,
			Bids = bids,
			Asks = asks,
			State = QuoteChangeStates.SnapshotComplete,
		};
	}
}
