namespace StockSharp.TradingTechnologies;

using Native;

partial class TradingTechnologiesMessageAdapter
{
	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var query = message.SecurityId.SecurityCode;
		if (query.IsEmpty())
		{
			await SendSubscriptionResultAsync(message, cancellationToken);
			return;
		}

		var count = message.Count is > 0 ? (int)Math.Min(message.Count.Value, 1000) : 100;
		var instruments = await EnsureClient().SearchInstrumentsAsync(
			query,
			message.SecurityId.BoardCode,
			count,
			cancellationToken);
		var types = message.GetSecurityTypes();

		foreach (var instrument in instruments)
		{
			var securityId = instrument.ToSecurityId();
			securityId.Isin = instrument.Isin;
			_securityIds[instrument.Id] = securityId;
			var security = new SecurityMessage
			{
				OriginalTransactionId = message.TransactionId,
				SecurityId = securityId,
				Name = instrument.Name,
				ShortName = instrument.Alias,
				SecurityType = instrument.ProductType.ToSecurityType(),
				Currency = Enum.TryParse<CurrencyTypes>(instrument.Currency, true, out var currency) ? currency : null,
				PriceStep = instrument.TickSize,
				VolumeStep = instrument.LotSize,
				MinVolume = instrument.MinimumQuantity,
				Multiplier = instrument.PointValue,
				ExpiryDate = instrument.ExpirationDate,
				Strike = instrument.Strike,
				OptionType = instrument.OptionType.ToOptionType(),
			};

			if (security.IsMatch(message, types))
				await SendOutMessageAsync(security, cancellationToken);
		}

		await SendSubscriptionResultAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessMarketSubscriptionAsync(message, TradingTechnologiesMarketDataKinds.Level1, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessMarketSubscriptionAsync(message, TradingTechnologiesMarketDataKinds.MarketDepth, cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(MarketDataMessage message, CancellationToken cancellationToken)
		=> ProcessMarketSubscriptionAsync(message, TradingTechnologiesMarketDataKinds.Ticks, cancellationToken);

	private async ValueTask ProcessMarketSubscriptionAsync(
		MarketDataMessage message,
		TradingTechnologiesMarketDataKinds kind,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(message.TransactionId, cancellationToken);
		var client = EnsureClient();

		if (!message.IsSubscribe)
		{
			await client.UnsubscribeAsync(message.OriginalTransactionId, cancellationToken);
			_marketSubscriptions.TryRemove(message.OriginalTransactionId, out _);
			return;
		}

		if (message.IsHistoryOnly() || message.From != null || message.To != null)
			throw new NotSupportedException("The TT .NET SDK provides real-time subscriptions, not historical market data.");

		if (!_marketSubscriptions.TryAdd(message.TransactionId, new()
		{
			SecurityId = message.SecurityId,
			Kind = kind,
		}))
		{
			throw new InvalidOperationException($"TT subscription {message.TransactionId} already exists.");
		}

		try
		{
			var instrument = await client.SubscribeAsync(
				message.TransactionId,
				kind,
				message.SecurityId.GetNativeId(),
				message.SecurityId.SecurityCode,
				message.SecurityId.BoardCode,
				cancellationToken);
			var securityId = message.SecurityId;
			securityId.SecurityCode = securityId.SecurityCode.IsEmpty(instrument.Alias.IsEmpty(instrument.Name));
			securityId.BoardCode = securityId.BoardCode.IsEmpty(instrument.Market.IsEmpty(BoardCodes.TradingTechnologies));
			securityId.Native = instrument.Id;
			_marketSubscriptions[message.TransactionId] = new() { SecurityId = securityId, Kind = kind };
			_securityIds[instrument.Id] = securityId;
			await SendSubscriptionResultAsync(message, cancellationToken);
		}
		catch
		{
			_marketSubscriptions.TryRemove(message.TransactionId, out _);
			throw;
		}
	}

	private async ValueTask ProcessLevel1Async(TradingTechnologiesLevel1Update update, CancellationToken cancellationToken)
	{
		foreach (var subscriptionId in update.SubscriptionIds)
		{
			if (!_marketSubscriptions.TryGetValue(subscriptionId, out var subscription) ||
				subscription.Kind != TradingTechnologiesMarketDataKinds.Level1)
				continue;

			var message = new Level1ChangeMessage
			{
				OriginalTransactionId = subscriptionId,
				SecurityId = subscription.SecurityId,
				ServerTime = update.ServerTime,
			}
			.TryAdd(Level1Fields.BestBidPrice, update.BidPrice)
			.TryAdd(Level1Fields.BestBidVolume, update.BidVolume)
			.TryAdd(Level1Fields.BestAskPrice, update.AskPrice)
			.TryAdd(Level1Fields.BestAskVolume, update.AskVolume)
			.TryAdd(Level1Fields.LastTradePrice, update.LastPrice)
			.TryAdd(Level1Fields.LastTradeVolume, update.LastVolume)
			.TryAdd(Level1Fields.OpenPrice, update.OpenPrice)
			.TryAdd(Level1Fields.HighPrice, update.HighPrice)
			.TryAdd(Level1Fields.LowPrice, update.LowPrice)
			.TryAdd(Level1Fields.ClosePrice, update.ClosePrice)
			.TryAdd(Level1Fields.SettlementPrice, update.SettlementPrice)
			.TryAdd(Level1Fields.Volume, update.Volume)
			.TryAdd(Level1Fields.OpenInterest, update.OpenInterest);

			if (message.Changes.Count > 0)
				await SendOutMessageAsync(message, cancellationToken);
		}
	}

	private async ValueTask ProcessDepthAsync(TradingTechnologiesDepthUpdate update, CancellationToken cancellationToken)
	{
		foreach (var subscriptionId in update.SubscriptionIds)
		{
			if (!_marketSubscriptions.TryGetValue(subscriptionId, out var subscription) ||
				subscription.Kind != TradingTechnologiesMarketDataKinds.MarketDepth)
				continue;

			await SendOutMessageAsync(new QuoteChangeMessage
			{
				OriginalTransactionId = subscriptionId,
				SecurityId = subscription.SecurityId,
				ServerTime = update.ServerTime,
				Bids = update.Bids.Select(level => new QuoteChange(level.Price, level.Volume)).ToArray(),
				Asks = update.Asks.Select(level => new QuoteChange(level.Price, level.Volume)).ToArray(),
				State = QuoteChangeStates.SnapshotComplete,
			}, cancellationToken);
		}
	}

	private async ValueTask ProcessTickAsync(TradingTechnologiesTick update, CancellationToken cancellationToken)
	{
		foreach (var subscriptionId in update.SubscriptionIds)
		{
			if (!_marketSubscriptions.TryGetValue(subscriptionId, out var subscription) ||
				subscription.Kind != TradingTechnologiesMarketDataKinds.Ticks)
				continue;

			await SendOutMessageAsync(new ExecutionMessage
			{
				DataTypeEx = DataType.Ticks,
				OriginalTransactionId = subscriptionId,
				SecurityId = subscription.SecurityId,
				TradeStringId = update.TradeId,
				TradePrice = update.Price,
				TradeVolume = update.Volume,
				OriginSide = update.Direction?.ToUpperInvariant() switch
				{
					"UPTICK" or "BUY" => Sides.Buy,
					"DOWNTICK" or "SELL" => Sides.Sell,
					_ => null,
				},
				ServerTime = update.ServerTime,
			}, cancellationToken);
		}
	}
}
