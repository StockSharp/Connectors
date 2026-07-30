namespace StockSharp.HdfcSecurities;

public partial class HdfcMessageAdapter
{
	private readonly SynchronizedDictionary<
		string,
		SynchronizedDictionary<DataType, long>> _marketSubscriptions =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, SecurityId> _securityIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, HdfcInstrument>
		_instruments = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<
		string,
		(DateTime time, decimal price, decimal volume)> _lastTicks =
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
			string streamId;
			try
			{
				securityId = instrument.ToSecurityId();
				streamId = instrument.ToStreamId();
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
				Name = instrument.SymbolName
					.IsEmpty(instrument.UnderlyingSymbol)
					.IsEmpty(instrument.SecurityId),
				ShortName = instrument.SymbolName
					.IsEmpty(instrument.UnderlyingSymbol),
				Class = instrument.InstrumentSegment,
				Currency = CurrencyTypes.INR,
				PriceStep = instrument.TickSize > 0
					? instrument.TickSize
					: null,
				VolumeStep = lotSize,
				Multiplier = lotSize,
				ExpiryDate = instrument.ExpiryDate.ToExpiry(),
				Strike = instrument.StrikePrice is > 0
					? instrument.StrikePrice
					: null,
				OptionType = instrument.OptionType.ToOptionType(),
			};
			if (type is SecurityTypes.Future or SecurityTypes.Option &&
				!instrument.UnderlyingSymbol.IsEmpty())
			{
				security.UnderlyingSecurityId = new()
				{
					SecurityCode = instrument.UnderlyingSymbol,
					BoardCode = instrument.Exchange,
				};
			}
			if (!security.IsMatch(lookupMsg, securityTypes))
				continue;

			RememberInstrument(streamId, securityId, instrument);
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
			cancellationToken);

	/// <inheritdoc />
	protected override ValueTask OnTicksSubscriptionAsync(
		MarketDataMessage mdMsg,
		CancellationToken cancellationToken)
		=> ProcessRealtimeSubscription(
			mdMsg,
			DataType.Ticks,
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
				"HDFC Securities provides five market-depth levels.");
		}
		return ProcessRealtimeSubscription(
			mdMsg,
			DataType.MarketDepth,
			cancellationToken);
	}

	private async ValueTask ProcessRealtimeSubscription(
		MarketDataMessage mdMsg,
		DataType dataType,
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
				cancellationToken);
			return;
		}

		if (dataType == DataType.MarketDepth && mdMsg.IsHistoryOnly())
		{
			throw new NotSupportedException(
				"HDFC Securities does not expose a REST market-depth snapshot.");
		}

		var instrument = await ResolveInstrument(
			mdMsg.SecurityId,
			cancellationToken);
		var streamId = instrument.ToStreamId();
		RememberInstrument(streamId, mdMsg.SecurityId, instrument);

		if (dataType == DataType.Level1 || dataType == DataType.Ticks)
		{
			var snapshot = (await _restClient.GetLtp(
				[instrument],
				cancellationToken)).FirstOrDefault();
			if (snapshot != null)
			{
				await SendLtpSnapshot(
					snapshot,
					mdMsg.SecurityId,
					mdMsg.TransactionId,
					dataType,
					cancellationToken);
			}
		}

		if (mdMsg.IsHistoryOnly())
		{
			await SendSubscriptionFinishedAsync(
				mdMsg.TransactionId,
				cancellationToken);
			return;
		}

		var subscriptions = _marketSubscriptions.SafeAdd(streamId);
		var first = subscriptions.Count == 0;
		subscriptions[dataType] = mdMsg.TransactionId;
		if (first)
			await _socketClient.Subscribe(streamId, cancellationToken);
		await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask RemoveRealtimeSubscription(
		MarketDataMessage mdMsg,
		DataType dataType,
		CancellationToken cancellationToken)
	{
		var instrument = await ResolveInstrument(
			mdMsg.SecurityId,
			cancellationToken);
		var streamId = instrument.ToStreamId();
		if (!_marketSubscriptions.TryGetValue(
			streamId,
			out var subscriptions))
			return;

		if (subscriptions.TryGetValue(dataType, out var subscriptionId) &&
			subscriptionId == mdMsg.OriginalTransactionId)
			subscriptions.Remove(dataType);
		if (subscriptions.Count > 0)
			return;

		_marketSubscriptions.Remove(streamId);
		_lastTicks.Remove(streamId);
		await _socketClient.Unsubscribe(streamId, cancellationToken);
	}

	private async ValueTask SendLtpSnapshot(
		HdfcLtp snapshot,
		SecurityId securityId,
		long transactionId,
		DataType dataType,
		CancellationToken cancellationToken)
	{
		var serverTime = CurrentTime;
		if (dataType == DataType.Level1)
		{
			var level1 = new Level1ChangeMessage
			{
				OriginalTransactionId = transactionId,
				SecurityId = securityId,
				ServerTime = serverTime,
			}
			.TryAdd(
				Level1Fields.LastTradePrice,
				Positive(snapshot.LastPrice))
			.TryAdd(
				Level1Fields.LastTradeTime,
				snapshot.LastPrice > 0 ? serverTime : null)
			.TryAdd(
				Level1Fields.ClosePrice,
				Positive(snapshot.PreviousClose));
			if (level1.Changes.Count > 0)
				await SendOutMessageAsync(level1, cancellationToken);
		}
		else if (snapshot.LastPrice > 0)
		{
			await SendOutMessageAsync(
				new ExecutionMessage
				{
					DataTypeEx = DataType.Ticks,
					OriginalTransactionId = transactionId,
					SecurityId = securityId,
					TradeStringId =
						$"LTP:{snapshot.Exchange}:{snapshot.Token}:{serverTime.Ticks}",
					TradePrice = snapshot.LastPrice,
					ServerTime = serverTime,
				},
				cancellationToken);
		}
	}

	private async ValueTask OnMarketDataReceived(
		HdfcMarketUpdate update,
		CancellationToken cancellationToken)
	{
		if (update == null ||
			!_marketSubscriptions.TryGetValue(
				update.StreamId,
				out var subscriptions))
			return;

		if (!_securityIds.TryGetValue(update.StreamId, out var securityId))
		{
			var instrument = await _restClient.GetInstrumentByStream(
				update.StreamId,
				cancellationToken);
			if (instrument == null)
				return;
			securityId = instrument.ToSecurityId();
			RememberInstrument(update.StreamId, securityId, instrument);
		}

		if (subscriptions.TryGetValue(DataType.Level1, out var level1Id))
		{
			var bestBid = update.Depth
				.Where(level => level.IsBid && level.Price > 0)
				.OrderByDescending(level => level.Price)
				.FirstOrDefault();
			var bestAsk = update.Depth
				.Where(level => !level.IsBid && level.Price > 0)
				.OrderBy(level => level.Price)
				.FirstOrDefault();
			var level1 = new Level1ChangeMessage
			{
				OriginalTransactionId = level1Id,
				SecurityId = securityId,
				ServerTime = update.ServerTime,
			}
			.TryAdd(
				Level1Fields.LastTradePrice,
				Positive(update.LastPrice))
			.TryAdd(
				Level1Fields.LastTradeVolume,
				Positive(update.LastQuantity))
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
				Level1Fields.AveragePrice,
				Positive(update.AveragePrice))
			.TryAdd(
				Level1Fields.BidsVolume,
				Positive(update.TotalBuyQuantity))
			.TryAdd(
				Level1Fields.AsksVolume,
				Positive(update.TotalSellQuantity))
			.TryAdd(Level1Fields.MinPrice, Positive(update.LowerLimit))
			.TryAdd(Level1Fields.MaxPrice, Positive(update.UpperLimit))
			.TryAdd(
				Level1Fields.OpenInterest,
				Positive(update.OpenInterest))
			.TryAdd(
				Level1Fields.BestBidPrice,
				Positive(bestBid?.Price ?? 0m))
			.TryAdd(
				Level1Fields.BestBidVolume,
				Positive(bestBid?.Quantity ?? 0L))
			.TryAdd(
				Level1Fields.BestAskPrice,
				Positive(bestAsk?.Price ?? 0m))
			.TryAdd(
				Level1Fields.BestAskVolume,
				Positive(bestAsk?.Quantity ?? 0L));
			if (level1.Changes.Count > 0)
				await SendOutMessageAsync(level1, cancellationToken);
		}

		if (update.LastPrice > 0 &&
			subscriptions.TryGetValue(DataType.Ticks, out var ticksId))
		{
			var trade = (
				update.ServerTime,
				update.LastPrice,
				(decimal)update.LastQuantity);
			if (!_lastTicks.TryGetValue(update.StreamId, out var previous) ||
				previous != trade)
			{
				_lastTicks[update.StreamId] = trade;
				await SendOutMessageAsync(
					new ExecutionMessage
					{
						DataTypeEx = DataType.Ticks,
						OriginalTransactionId = ticksId,
						SecurityId = securityId,
						TradeStringId =
							$"{update.StreamId}:{update.ServerTime.Ticks}:{update.LastPrice.ToString(CultureInfo.InvariantCulture)}:{update.LastQuantity}",
						TradePrice = update.LastPrice,
						TradeVolume = update.LastQuantity > 0
							? update.LastQuantity
							: null,
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
				new QuoteChangeMessage
				{
					OriginalTransactionId = depthId,
					SecurityId = securityId,
					ServerTime = update.ServerTime,
					Bids =
					[
						..
						update.Depth
							.Where(level => level.IsBid && level.Price > 0)
							.OrderByDescending(level => level.Price)
							.Take(5)
							.Select(ToQuote)
					],
					Asks =
					[
						..
						update.Depth
							.Where(level => !level.IsBid && level.Price > 0)
							.OrderBy(level => level.Price)
							.Take(5)
							.Select(ToQuote)
					],
				},
				cancellationToken);
		}
	}

	private async Task<HdfcInstrument> ResolveInstrument(
		SecurityId securityId,
		CancellationToken cancellationToken)
	{
		if (securityId.Native != null)
		{
			var key = securityId.Native.ParseInstrumentKey();
			var byNative = await _restClient.GetInstrument(
				key.exchange,
				key.securityId,
				cancellationToken);
			if (byNative != null)
				return byNative;
		}

		var instruments = await _restClient.GetInstruments(
			cancellationToken);
		var instrument = instruments.FirstOrDefault(item =>
			item.Exchange.EqualsIgnoreCase(securityId.BoardCode) &&
			(item.SymbolName.EqualsIgnoreCase(securityId.SecurityCode) ||
				item.SecurityId.EqualsIgnoreCase(securityId.SecurityCode) ||
				item.ExchangeSecurityId.EqualsIgnoreCase(
					securityId.SecurityCode)));
		return instrument ??
			throw new InvalidOperationException(
				$"HDFC Securities instrument '{securityId}' was not found in the public security master.");
	}

	private void RememberInstrument(
		string streamId,
		SecurityId securityId,
		HdfcInstrument instrument)
	{
		_securityIds[streamId] = securityId;
		_instruments[streamId] = instrument;
	}

	private static QuoteChange ToQuote(HdfcDepthLevel level)
		=> new(level.Price, Math.Max(0, level.Quantity))
		{
			OrdersCount = (int)Math.Min(
				Math.Max(0, level.Orders),
				int.MaxValue),
		};

	private static decimal? Positive(decimal value)
		=> value > 0 ? value : null;

	private static decimal? Positive(long value)
		=> value > 0 ? value : null;
}
