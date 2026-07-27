namespace StockSharp.Ventura;

public partial class VenturaMessageAdapter
{
	private readonly SynchronizedDictionary<
		string,
		SynchronizedDictionary<DataType, long>> _marketSubscriptions =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, VenturaInstrument>
		_instruments = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<
		string,
		(DateTime time, decimal price)> _lastTicks =
			new(StringComparer.OrdinalIgnoreCase);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(
		SecurityLookupMessage lookupMsg,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			lookupMsg.TransactionId,
			cancellationToken);
		var securityTypes = lookupMsg.GetSecurityTypes();
		var left = lookupMsg.Count ?? long.MaxValue;

		foreach (var instrument in await _restClient.GetInstruments(
			cancellationToken))
		{
			SecurityId securityId;
			string streamKey;
			try
			{
				securityId = instrument.ToSecurityId();
				streamKey = instrument.ToStreamKey(false);
			}
			catch (ArgumentException)
			{
				continue;
			}

			var type = instrument.ToSecurityType();
			var lotSize = instrument.LotSize > 0
				? instrument.LotSize
				: 1m;
			var security = new SecurityMessage
			{
				OriginalTransactionId = lookupMsg.TransactionId,
				SecurityId = securityId,
				SecurityType = type,
				Name = instrument.Name
					.IsEmpty(instrument.TradingSymbol)
					.IsEmpty(instrument.ExchangeToken),
				ShortName = instrument.TradingSymbol
					.IsEmpty(instrument.Name),
				Class = instrument.Segment,
				Currency = CurrencyTypes.INR,
				PriceStep = instrument.TickSize > 0
					? instrument.TickSize
					: null,
				VolumeStep = lotSize,
				Multiplier = lotSize,
				ExpiryDate = instrument.Expiry.ToExpiry(),
				Strike = instrument.Strike > 0
					? instrument.Strike
					: null,
				OptionType = instrument.Instrument.ToOptionType(),
			};
			if (type is SecurityTypes.Future or SecurityTypes.Option &&
				!instrument.Name.IsEmpty())
			{
				security.UnderlyingSecurityId = new()
				{
					SecurityCode = instrument.Name,
					BoardCode = instrument.Exchange,
				};
			}
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			RememberInstrument(streamKey, securityId, instrument);
			await SendOutMessageAsync(security, cancellationToken);
			if (--left <= 0)
				break;
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask OnLevel1SubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(
			mdMsg,
			DataType.Level1,
			false,
			cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(
			mdMsg,
			DataType.Ticks,
			false,
			cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnMarketDepthSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
	{
		var depth = mdMsg.MaxDepth ?? 5;
		if (depth is < 1 or > 5)
		{
			throw new ArgumentOutOfRangeException(
				nameof(mdMsg.MaxDepth),
				depth,
				"Ventura EaseAPI provides five market-depth levels.");
		}
		return ProcessRealtimeSubscription(
			mdMsg,
			DataType.MarketDepth,
			true,
			cancellationToken);
	}

	private async ValueTask ProcessRealtimeSubscription(
		MarketDataMessage mdMsg,
		DataType dataType,
		bool depth,
		CancellationToken cancellationToken)
	{
		await SendSubscriptionReplyAsync(
			mdMsg.TransactionId,
			cancellationToken);
		if (!mdMsg.IsSubscribe)
		{
			await RemoveRealtimeSubscription(
				mdMsg,
				dataType,
				depth,
				cancellationToken);
			return;
		}

		var instrument = await ResolveInstrument(
			mdMsg.SecurityId,
			cancellationToken);
		var streamKey = instrument.ToStreamKey(depth);
		RememberInstrument(streamKey, mdMsg.SecurityId, instrument);

		var snapshot = await _restClient.GetMarketUpdate(
			instrument,
			depth,
			cancellationToken);
		await SendMarketSnapshot(
			snapshot,
			mdMsg.SecurityId,
			mdMsg.TransactionId,
			dataType,
			cancellationToken);

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(
				mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var subscriptions = _marketSubscriptions.SafeAdd(streamKey);
		var first = subscriptions.Count == 0;
		subscriptions[dataType] = mdMsg.TransactionId;
		if (first)
		{
			await _marketDataClient.Subscribe(
				instrument.ToStreamAction(depth),
				instrument.ToStreamToken(),
				cancellationToken);
		}
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask RemoveRealtimeSubscription(
		MarketDataMessage mdMsg,
		DataType dataType,
		bool depth,
		CancellationToken cancellationToken)
	{
		var instrument = await ResolveInstrument(
			mdMsg.SecurityId,
			cancellationToken);
		var streamKey = instrument.ToStreamKey(depth);
		if (!_marketSubscriptions.TryGetValue(
			streamKey,
			out var subscriptions))
			return;

		if (subscriptions.TryGetValue(dataType, out var subscriptionId) &&
			subscriptionId == mdMsg.OriginalTransactionId)
			subscriptions.Remove(dataType);
		if (subscriptions.Count > 0)
			return;

		_marketSubscriptions.Remove(streamKey);
		_lastTicks.Remove(streamKey);
		await _marketDataClient.Unsubscribe(
			instrument.ToStreamAction(depth),
			instrument.ToStreamToken(),
			cancellationToken);
	}

	private async ValueTask SendMarketSnapshot(
		VenturaMarketUpdate update,
		SecurityId securityId,
		long transactionId,
		DataType dataType,
		CancellationToken cancellationToken)
	{
		if (dataType == DataType.Level1)
		{
			var level1 = CreateLevel1(
				update,
				securityId,
				transactionId);
			if (level1.Changes.Count > 0)
				await SendOutMessageAsync(level1, cancellationToken);
		}
		else if (dataType == DataType.Ticks && update.LastPrice > 0)
		{
			await SendOutMessageAsync(
				new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = transactionId,
					SecurityId = securityId,
					TradeStringId =
						$"LTP:{update.Token}:{update.ServerTime.Ticks}",
					TradePrice = update.LastPrice,
					ServerTime = update.ServerTime,
				},
				cancellationToken);
		}
		else if (dataType == DataType.MarketDepth &&
			update.Depth.Length > 0)
		{
			await SendOutMessageAsync(
				CreateDepth(update, securityId, transactionId),
				cancellationToken);
		}
	}

	private async ValueTask OnMarketDataReceived(
		VenturaMarketUpdate update,
		CancellationToken cancellationToken)
	{
		if (update == null)
			return;
		var streamKey = VenturaExtensions.CreateStreamKey(
			update.Action,
			update.Token);
		if (!_marketSubscriptions.TryGetValue(
			streamKey,
			out var subscriptions))
			return;
		if (!_securityIds.TryGetValue(streamKey, out var securityId))
			return;

		if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
		{
			var level1 = CreateLevel1(update, securityId, level1Id);
			if (level1.Changes.Count > 0)
				await SendOutMessageAsync(level1, cancellationToken);
		}

		if (update.LastPrice > 0 &&
			subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
		{
			var trade = (update.ServerTime, update.LastPrice);
			if (!_lastTicks.TryGetValue(streamKey, out var previous) ||
				previous != trade)
			{
				_lastTicks[streamKey] = trade;
				await SendOutMessageAsync(
					new ExecutionMessage
					{
						DataTypeEx = DataType.Ticks,
						OriginalTransactionId = ticksId,
						SecurityId = securityId,
						TradeStringId =
							$"{streamKey}:{update.ServerTime.Ticks}:{update.LastPrice.ToString(CultureInfo.InvariantCulture)}",
						TradePrice = update.LastPrice,
						ServerTime = update.ServerTime,
					},
					cancellationToken);
			}
		}

		if (subscriptions.TryGetValue(
				DataType.MarketDepth,
				out var depthId) &&
			update.Depth.Length > 0)
		{
			await SendOutMessageAsync(
				CreateDepth(update, securityId, depthId),
				cancellationToken);
		}
	}

	private static Level1ChangeMessage CreateLevel1(
		VenturaMarketUpdate update,
		SecurityId securityId,
		long transactionId)
	{
		var bestBid = update.Depth
			.Where(level => level.BuyPrice > 0)
			.OrderByDescending(level => level.BuyPrice)
			.FirstOrDefault();
		var bestAsk = update.Depth
			.Where(level => level.SellPrice > 0)
			.OrderBy(level => level.SellPrice)
			.FirstOrDefault();
		return new Level1ChangeMessage
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = update.ServerTime,
		}
		.TryAdd(Level1Fields.LastTradePrice, Positive(update.LastPrice))
		.TryAdd(
			Level1Fields.LastTradeTime,
			update.LastPrice > 0 ? update.ServerTime : null)
		.TryAdd(Level1Fields.OpenPrice, Positive(update.OpenPrice))
		.TryAdd(Level1Fields.HighPrice, Positive(update.HighPrice))
		.TryAdd(Level1Fields.LowPrice, Positive(update.LowPrice))
		.TryAdd(
			Level1Fields.ClosePrice,
			Positive(update.PreviousClose))
		.TryAdd(Level1Fields.Volume, Positive(update.Volume))
		.TryAdd(
			Level1Fields.BidsVolume,
			Positive(update.TotalBuyQuantity))
		.TryAdd(
			Level1Fields.AsksVolume,
			Positive(update.TotalSellQuantity))
		.TryAdd(Level1Fields.MaxPrice, Positive(update.UpperCircuit))
		.TryAdd(Level1Fields.MinPrice, Positive(update.LowerCircuit))
		.TryAdd(
			Level1Fields.BestBidPrice,
			Positive(bestBid?.BuyPrice ?? 0m))
		.TryAdd(
			Level1Fields.BestBidVolume,
			Positive(bestBid?.BuyQuantity ?? 0m))
		.TryAdd(
			Level1Fields.BestAskPrice,
			Positive(bestAsk?.SellPrice ?? 0m))
		.TryAdd(
			Level1Fields.BestAskVolume,
			Positive(bestAsk?.SellQuantity ?? 0m));
	}

	private static QuoteChangeMessage CreateDepth(
		VenturaMarketUpdate update,
		SecurityId securityId,
		long transactionId)
		=> new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = securityId,
			ServerTime = update.ServerTime,
			Bids =
			[
				..
					update.Depth
						.Where(level => level.BuyPrice > 0)
						.OrderByDescending(level => level.BuyPrice)
						.Take(5)
						.Select(level => new QuoteChange(
							level.BuyPrice,
							Math.Max(0, level.BuyQuantity))
						{
							OrdersCount = (int)Math.Min(
								Math.Max(0, level.BuyOrders),
								int.MaxValue),
						})
			],
			Asks =
			[
				..
					update.Depth
						.Where(level => level.SellPrice > 0)
						.OrderBy(level => level.SellPrice)
						.Take(5)
						.Select(level => new QuoteChange(
							level.SellPrice,
							Math.Max(0, level.SellQuantity))
						{
							OrdersCount = (int)Math.Min(
								Math.Max(0, level.SellOrders),
								int.MaxValue),
						})
			],
		};

	private async Task<VenturaInstrument> ResolveInstrument(
		SecurityId securityId,
		CancellationToken cancellationToken)
	{
		if (securityId.Native != null)
		{
			var key = securityId.Native.ParseInstrumentKey();
			var byNative = await _restClient.GetInstrument(
				key.exchange,
				key.exchangeToken,
				cancellationToken);
			if (byNative != null)
				return byNative;
		}

		var bySymbol = await _restClient.FindInstrument(
			securityId.BoardCode,
			securityId.SecurityCode,
			cancellationToken);
		return bySymbol ??
			throw new InvalidOperationException(
				$"Ventura EaseAPI instrument '{securityId}' was not found in the public instrument master.");
	}

	private void RememberInstrument(
		string streamKey,
		SecurityId securityId,
		VenturaInstrument instrument)
	{
		_securityIds[streamKey] = securityId;
		_instruments[streamKey] = instrument;
	}

	private static decimal? Positive(decimal value)
		=> value > 0 ? value : null;
}
